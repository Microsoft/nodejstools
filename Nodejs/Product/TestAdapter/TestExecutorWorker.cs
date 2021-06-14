// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.NodejsTools.TestAdapter.TestFrameworks;
using Microsoft.NodejsTools.TestFrameworks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;
using Newtonsoft.Json;

namespace Microsoft.NodejsTools.TestAdapter
{
    public class TestCaseResult
    {
        public TestCase TestCase;
        public TestResult TestResult;
    }

    internal sealed partial class TestExecutorWorker
    {
        private static readonly Version Node8Version = new Version(8, 0);

        //get from NodeRemoteDebugPortSupplier::PortSupplierId
        private static readonly Guid NodejsRemoteDebugPortSupplierUnsecuredId = new Guid("{9E16F805-5EFC-4CE5-8B67-9AE9B643EF80}");

        private readonly ManualResetEvent cancelRequested = new ManualResetEvent(false);
        private readonly ManualResetEvent testsCompleted = new ManualResetEvent(false);

        private readonly IFrameworkHandle frameworkHandle;
        private readonly IRunContext runContext;
        private List<TestCaseResult> currentTests;
        private ProcessOutput nodeProcess;
        private ResultObject currentResultObject;

        private readonly FrameworkDiscoverer frameworkDiscoverer;

        public TestExecutorWorker(IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            this.frameworkHandle = frameworkHandle;
            this.runContext = runContext;

            var settings = new UnitTestSettings(runContext.RunSettings, runContext.IsBeingDebugged);

            this.frameworkDiscoverer = new FrameworkDiscoverer(settings.TestFrameworksLocation);
        }

        public void Cancel()
        {
            //let us just kill the node process there, rather do it late, because VS engine process 
            //could exit right after this call and our node process will be left running.
            this.KillNodeProcess();
            this.cancelRequested.Set();
        }

        /// <summary>
        /// This is the equivalent of "RunAll" functionality
        /// </summary>
        /// <param name="sources">Refers to the list of test sources passed to the test adapter from the client.  (Client could be VS or command line)</param>
        public void RunTests(IEnumerable<string> sources, ITestDiscoverer discoverer)
        {
            ValidateArg.NotNull(sources, nameof(sources));
            ValidateArg.NotNull(discoverer, nameof(discoverer));

            this.cancelRequested.Reset();

            var receiver = new TestReceiver();
            discoverer.DiscoverTests(sources, this.runContext, this.frameworkHandle, receiver);

            if (this.cancelRequested.WaitOne(0))
            {
                return;
            }

            this.RunTests(receiver.Tests);
        }

        /// <summary>
        /// This is the equivalent of "Run Selected Tests" functionality.
        /// </summary>
        /// <param name="tests">The list of TestCases selected to run</param>
        public void RunTests(IEnumerable<TestCase> tests)
        {
            ValidateArg.NotNull(tests, nameof(tests));
            this.cancelRequested.Reset();

            var testFramework = tests.First().GetPropertyValue(JavaScriptTestCaseProperties.TestFramework, defaultValue: TestFrameworkDirectories.ExportRunnerFrameworkName);
            if (string.Equals(testFramework, TestFrameworkDirectories.AngularFrameworkName, StringComparison.OrdinalIgnoreCase))
            {
                // Every angular run initializes karma, browser, caching, etc, 
                // so is preferable to let the test adapter handle any optimization.
                RunAllTests(tests);
            }
            else
            {
                RunTestsByFile(tests);
            }
        }

        private void RunAllTests(IEnumerable<TestCase> tests)
        {
            var testCaseResults = tests.Select(x => new TestCaseResult { TestCase = x });
            this.currentTests = testCaseResults.ToList();

            this.RunTestCases(testCaseResults);
        }

        /// <summary>
        /// Runs all of the test cases by executing each one of the files sequentially.
        /// </summary>
        private void RunTestsByFile(IEnumerable<TestCase> tests)
        {
            // .ts file path -> project settings
            var fileToTests = new Dictionary<string, List<TestCaseResult>>();

            // put tests into dictionary where key is their source file
            foreach (var test in tests)
            {
                if (!fileToTests.ContainsKey(test.CodeFilePath))
                {
                    fileToTests[test.CodeFilePath] = new List<TestCaseResult>();
                }
                fileToTests[test.CodeFilePath].Add(new TestCaseResult() { TestCase = test });
            }

            // where key is the file and value is a list of tests
            foreach (var testCaseList in fileToTests.Values)
            {
                this.currentTests = testCaseList;

                // Run all test cases in a given file
                this.RunTestCases(testCaseList);
            }
        }

        private void RunTestCases(IEnumerable<TestCaseResult> tests)
        {
            // May be null, but this is handled by RunTestCase if it matters.
            // No VS instance just means no debugging, but everything else is okay.
            if (!tests.Any())
            {
                return;
            }

            var startedFromVs = this.HasVisualStudioProcessId(out var vsProcessId);

            var nodeArgs = new List<string>();

            var testObjects = new List<TestCaseObject>();

            // All tests being run are for the same test file, so just use the first test listed to get the working dir
            var firstTest = tests.First().TestCase;
            var workingDir = firstTest.GetPropertyValue(JavaScriptTestCaseProperties.WorkingDir, defaultValue: Path.GetDirectoryName(firstTest.CodeFilePath));
            var nodeExePath = firstTest.GetPropertyValue<string>(JavaScriptTestCaseProperties.NodeExePath, defaultValue: null);
            var projectRootDir = firstTest.GetPropertyValue(JavaScriptTestCaseProperties.ProjectRootDir, defaultValue: Path.GetDirectoryName(firstTest.CodeFilePath));

            if (string.IsNullOrEmpty(nodeExePath) || !File.Exists(nodeExePath))
            {
                this.frameworkHandle.SendMessage(TestMessageLevel.Error, "Interpreter path does not exist: " + nodeExePath);
                return;
            }

            var nodeVersion = Nodejs.GetNodeVersion(nodeExePath);

            foreach (var test in tests)
            {
                if (this.cancelRequested.WaitOne(0))
                {
                    break;
                }

                var args = this.GetInterpreterArgs(test.TestCase, workingDir, projectRootDir);

                // Fetch the run_tests argument for starting node.exe if not specified yet
                if (!nodeArgs.Any())
                {
                    nodeArgs.Add(args.RunTestsScriptFile);
                }

                testObjects.Add(new TestCaseObject(
                    framework: args.TestFramework,
                    fullyQualifiedName: args.fullyQualifiedName,
                    testFile: args.TestFile,
                    workingFolder: args.WorkingDirectory,
                    projectFolder: args.ProjectRootDir,
                    configDirPath: args.ConfigDirPath));
            }

            var port = 0;
            if (this.runContext.IsBeingDebugged && startedFromVs)
            {
                this.DetachDebugger(vsProcessId);
                // Ensure that --debug-brk or --inspect-brk is the first argument
                nodeArgs.Insert(0, GetDebugArgs(nodeVersion, out port));
            }

            // make sure the tests completed is not signalled.
            this.testsCompleted.Reset();

            this.nodeProcess = ProcessOutput.Run(
                nodeExePath,
                nodeArgs,
                workingDir,
                env: null,
                visible: false,
                redirector: new TestExecutionRedirector(this.ProcessTestRunnerEmit),
                quoteArgs: false);

            if (this.runContext.IsBeingDebugged && startedFromVs)
            {
                this.AttachDebugger(vsProcessId, port, nodeVersion);
            }

            var serializedObjects = JsonConvert.SerializeObject(testObjects);

            // Send the process the list of tests to run and wait for it to complete
            this.nodeProcess.WriteInputLine(serializedObjects);

            // for node 8 the process doesn't automatically exit when debugging, so always detach
            WaitHandle.WaitAny(new[] { this.nodeProcess.WaitHandle, this.testsCompleted });
            if (this.runContext.IsBeingDebugged && startedFromVs)
            {
                this.DetachDebugger(vsProcessId);
            }

            // Automatically fail tests that haven't been run by this point (failures in before() hooks)
            foreach (var notRunTest in this.currentTests)
            {
                var result = new TestResult(notRunTest.TestCase)
                {
                    Outcome = TestOutcome.Failed
                };

                if (this.currentResultObject != null)
                {
                    result.Messages.Add(new TestResultMessage(TestResultMessage.StandardOutCategory, this.currentResultObject.stdout));
                    result.Messages.Add(new TestResultMessage(TestResultMessage.StandardErrorCategory, this.currentResultObject.stderr));
                }
                this.frameworkHandle.RecordResult(result);
                this.frameworkHandle.RecordEnd(notRunTest.TestCase, TestOutcome.Failed);
            }
        }

        private void ProcessTestRunnerEmit(string line)
        {
            try
            {
                var testEvent = JsonConvert.DeserializeObject<TestEvent>(line);
                // Extract test from list of tests
                var test = this.currentTests
                               .Where(n => n.TestCase.FullyQualifiedName == testEvent.fullyQualifiedName)
                               .FirstOrDefault();

                if (test != null)
                {
                    switch (testEvent.type)
                    {
                        case "test start":
                            test.TestResult = new TestResult(test.TestCase)
                            {
                                StartTime = DateTimeOffset.Now
                            };
                            this.frameworkHandle.RecordStart(test.TestCase);
                            break;
                        case "result":
                            RecordEnd(test, testEvent.result);
                            break;
                        case "pending":
                            test.TestResult = new TestResult(test.TestCase);
                            RecordEnd(test, testEvent.result);
                            break;
                    }
                }
                else if (testEvent.type == "end")
                {
                    this.currentResultObject = testEvent.result;
                    this.testsCompleted.Set();
                }
            }
            catch (JsonReaderException)
            {
                // Often lines emitted while running tests are not test results, and thus will fail to parse above
            }

            void RecordEnd(TestCaseResult test, ResultObject resultObject)
            {
                var standardOutputLines = resultObject.stdout.Split('\n');
                var standardErrorLines = resultObject.stderr.Split('\n');

                if (resultObject.pending == true)
                {
                    test.TestResult.Outcome = TestOutcome.Skipped;
                }
                else
                {
                    test.TestResult.EndTime = DateTimeOffset.Now;
                    test.TestResult.Duration = test.TestResult.EndTime - test.TestResult.StartTime;
                    test.TestResult.Outcome = resultObject.passed ? TestOutcome.Passed : TestOutcome.Failed;
                }

                var errorMessage = string.Join(Environment.NewLine, standardErrorLines);

                test.TestResult.Messages.Add(new TestResultMessage(TestResultMessage.StandardOutCategory, string.Join(Environment.NewLine, standardOutputLines)));
                test.TestResult.Messages.Add(new TestResultMessage(TestResultMessage.StandardErrorCategory, errorMessage));
                test.TestResult.Messages.Add(new TestResultMessage(TestResultMessage.AdditionalInfoCategory, errorMessage));
                this.frameworkHandle.RecordResult(test.TestResult);
                this.frameworkHandle.RecordEnd(test.TestCase, test.TestResult.Outcome);
                this.currentTests.Remove(test);
            }
        }

        private bool HasVisualStudioProcessId(out int processId)
        {
            processId = 0;
            var pid = Environment.GetEnvironmentVariable(NodejsConstants.NodeToolsProcessIdEnvironmentVariable);
            return pid == null ? false : int.TryParse(pid, out processId);
        }

        private void DetachDebugger(int vsProcessId)
        {
#if !NETSTANDARD2_0
            VisualStudioApp.DetachDebugger(vsProcessId);
#endif
        }

        private void AttachDebugger(int vsProcessId, int port, Version nodeVersion)
        {
#if !NETSTANDARD2_0
            try
            {
                if (nodeVersion >= Node8Version)
                {
                    VisualStudioApp.AttachToProcessNode2DebugAdapter(vsProcessId, port);
                }
                else
                {
                    //the '#ping=0' is a special flag to tell VS node debugger not to connect to the port,
                    //because a connection carries the consequence of setting off --debug-brk, and breakpoints will be missed.
                    var qualifierUri = string.Format("tcp://localhost:{0}#ping=0", port);
                    while (!VisualStudioApp.AttachToProcess(this.nodeProcess.Process, vsProcessId, NodejsRemoteDebugPortSupplierUnsecuredId, qualifierUri))
                    {
                        if (this.nodeProcess.Wait(TimeSpan.FromMilliseconds(500)))
                        {
                            break;
                        }
                    }
                }
#if DEBUG
            }
            catch (COMException ex)
            {
                this.frameworkHandle.SendMessage(TestMessageLevel.Error, "Error occurred connecting to debuggee.");
                this.frameworkHandle.SendMessage(TestMessageLevel.Error, ex.ToString());
                this.KillNodeProcess();
            }
#else
            }
            catch (COMException)
            {
                this.frameworkHandle.SendMessage(TestMessageLevel.Error, "Error occurred connecting to debuggee.");
                this.KillNodeProcess();
            }
#endif
#endif
        }


        private static int GetFreePort()
        {
            return Enumerable.Range(new Random().Next(49152, 65536), 60000).Except(
                from connection in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                select connection.LocalEndPoint.Port
            ).First();
        }

        private TestFramework.ArgumentsToRunTests GetInterpreterArgs(TestCase test, string workingDir, string projectRootDir)
        {
            var testFile = test.GetPropertyValue(JavaScriptTestCaseProperties.TestFile, defaultValue: test.CodeFilePath);
            var testFramework = test.GetPropertyValue<string>(JavaScriptTestCaseProperties.TestFramework, defaultValue: null);
            var configDirPath = test.GetPropertyValue<string>(JavaScriptTestCaseProperties.ConfigDirPath, defaultValue: null);
            return this.frameworkDiscoverer.GetFramework(testFramework).GetArgumentsToRunTests(test.FullyQualifiedName, testFile, workingDir, projectRootDir, configDirPath);
        }

        private static string GetDebugArgs(Version nodeVersion, out int port)
        {
            port = GetFreePort();

            return nodeVersion >= Node8Version ? $"--inspect-brk={port}" : $"--debug-brk={port}";
        }

        private void KillNodeProcess()
        {
            this.nodeProcess?.Kill();
        }
    }
}
