// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.NodejsTools.SourceMapping;
using Microsoft.NodejsTools.TestAdapter.TestFrameworks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudioTools;

namespace Microsoft.NodejsTools.TestAdapter
{
    public sealed class TestDiscovererWorker
    {
        private readonly string testSource;
        private readonly string workingDir;
        private readonly Uri testExecutorUri;
        private readonly string nodeExePath;

        public TestDiscovererWorker(string testSource, Uri testExecutorUri, string nodeExePath, string workDir)
        {
            this.testSource = testSource;
            this.workingDir = workDir;
            this.testExecutorUri = testExecutorUri;
            this.nodeExePath = nodeExePath;
        }

        public TestDiscovererWorker(string testSource, Uri testExecutorUri, string nodeExePath)
        {
            this.testSource = testSource;
            this.workingDir = Path.GetDirectoryName(this.testSource);
            this.testExecutorUri = testExecutorUri;
            this.nodeExePath = nodeExePath;
        }

        public void DiscoverTests(string testFolderPath, TestFramework testFx, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            var fileList = Directory.GetFiles(testFolderPath, "*.js", SearchOption.AllDirectories);
            this.DiscoverTests(fileList, testFx, logger, discoverySink);
        }

        public void DiscoverTests(IEnumerable<string> fileList, TestFramework testFx, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            if (!File.Exists(this.nodeExePath))
            {
                logger.SendMessage(TestMessageLevel.Error, "Node.exe was not found. Please install Node.js before running tests.");
                return;
            }

            var files = string.Join(";", fileList);
            logger.SendMessage(TestMessageLevel.Informational, $"Processing: {files}");

            var discoveredTestCases = testFx.FindTests(fileList, this.nodeExePath, logger, projectRoot: this.workingDir);
            if (!discoveredTestCases.Any())
            {
                logger.SendMessage(TestMessageLevel.Warning, "Discovered 0 test cases.");
                return;
            }

            foreach (var discoveredTest in discoveredTestCases)
            {
                var qualifiedName = discoveredTest.FullyQualifiedName;
                const string indent = "  ";
                logger.SendMessage(TestMessageLevel.Informational, $"{indent}Creating Test Case:{qualifiedName}");
                //figure out the test source info such as line number
                var filePath = CommonUtils.GetAbsoluteFilePath(this.workingDir, discoveredTest.TestFile);

                // We try to map every time, so we work with .ts files and post-processed js files.
                var fi = SourceMapper.MaybeMap(new FunctionInformation(string.Empty,
                                                                          discoveredTest.TestName,
                                                                          discoveredTest.SourceLine,
                                                                         filePath));

                var testCase = new TestCase(qualifiedName, NodejsConstants.ExecutorUri, this.testSource)
                {
                    CodeFilePath = fi?.Filename ?? filePath,
                    LineNumber = fi?.LineNumber ?? discoveredTest.SourceLine,
                    DisplayName = discoveredTest.TestName
                };

                testCase.SetPropertyValue(JavaScriptTestCaseProperties.TestFramework, testFx.Name);
                testCase.SetPropertyValue(JavaScriptTestCaseProperties.WorkingDir, this.workingDir);
                testCase.SetPropertyValue(JavaScriptTestCaseProperties.ProjectRootDir, this.workingDir);
                testCase.SetPropertyValue(JavaScriptTestCaseProperties.NodeExePath, this.nodeExePath);
                testCase.SetPropertyValue(JavaScriptTestCaseProperties.TestFile, filePath);

                discoverySink.SendTestCase(testCase);
            }

            logger.SendMessage(TestMessageLevel.Informational, $"Processing finished for framework '{testFx.Name}'.");
        }
    }
}
