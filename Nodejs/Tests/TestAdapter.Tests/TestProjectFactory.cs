// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.NodejsTools;
using Microsoft.NodejsTools.TestAdapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using static Microsoft.NodejsTools.TestAdapter.TestFrameworks.TestFramework;

namespace TestAdapter.Tests
{
    public static class TestProjectFactory
    {
        public enum ProjectName
        {
            NodeAppWithTestsConfiguredOnProject,
            NodeAppWithTestsConfiguredPerFile,
            NodeAppWithAngularTests,
        }

        private struct TestCaseOptions
        {
            public SupportedFramework TestFramework;
            public string Source;
            public string WorkingDir;
            public string ConfigDirPath;
            public string NodeExePath;
        }

        public static string GetProjectDirPath(ProjectName projectName)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), $@"..\..\..\..\MockProjects\{projectName}");
        }

        public static string GetProjectFilePath(ProjectName projectName)
        {
            switch (projectName)
            {
                case ProjectName.NodeAppWithTestsConfiguredOnProject:
                case ProjectName.NodeAppWithTestsConfiguredPerFile:
                case ProjectName.NodeAppWithAngularTests:
                    return Path.Combine(GetProjectDirPath(projectName), $"{projectName}.njsproj");
            }

            throw new NotImplementedException($"ProjectName {projectName} has not been implemented.");
        }

        public static List<TestCaseResult> GetTestCaseResults(ProjectName projectName)
        {
            switch (projectName)
            {
                case ProjectName.NodeAppWithTestsConfiguredOnProject:
                    return new List<TestCaseResult>(GetTestCases(projectName, SupportedFramework.Mocha));

                case ProjectName.NodeAppWithTestsConfiguredPerFile:
                    var result = new List<TestCaseResult>();
                    result.AddRange(GetTestCases(projectName, SupportedFramework.Jasmine));
                    result.AddRange(GetTestCases(projectName, SupportedFramework.ExportRunner));
                    result.AddRange(GetTestCases(projectName, SupportedFramework.Jest));
                    result.AddRange(GetTestCases(projectName, SupportedFramework.Mocha));
                    result.AddRange(GetTestCases(projectName, SupportedFramework.Tape));

                    return result;

                case ProjectName.NodeAppWithAngularTests:
                    return new List<TestCaseResult>(GetTestCases(projectName, SupportedFramework.Angular));
            };

            throw new NotImplementedException($"ProjectName {projectName} has not been implemented.");
        }

        private static TestCaseResult[] GetTestCases(ProjectName projectName, SupportedFramework testFramework)
        {
            var testCaseOptions = new TestCaseOptions()
            {
                TestFramework = testFramework,
                WorkingDir = GetProjectDirPath(projectName),
                Source = GetProjectFilePath(projectName),
                ConfigDirPath = projectName == ProjectName.NodeAppWithAngularTests ? GetProjectDirPath(projectName) : null,
                NodeExePath = Nodejs.GetPathToNodeExecutableFromEnvironment()
            };

            switch (testFramework)
            {
                case SupportedFramework.Jasmine:
                    {
                        var filePath = Path.Combine(GetProjectDirPath(projectName), "JasmineUnitTest.js");
                        return new[]
                        {
                            GetTestCaseResult(
                                testCaseOptions,
                                "JasmineUnitTest.js::Test Suite 1::Test 1",
                                "Test 1",
                                1,
                                filePath,
                                TestOutcome.Passed,
                                "."),
                            GetTestCaseResult(
                                testCaseOptions,
                                "JasmineUnitTest.js::Test Suite 1::Test 2",
                                "Test 2",
                                1,
                                filePath,
                                TestOutcome.Failed,
                                "F"),
                        };
                    }
                case SupportedFramework.ExportRunner:
                    {
                        var filePath = Path.Combine(GetProjectDirPath(projectName), "ExportRunnerUnitTest.js");
                        return new[]
                        {
                            GetTestCaseResult(
                                testCaseOptions,
                                "ExportRunnerUnitTest.js::global::Test 1",
                                "Test 1",
                                1,
                                filePath,
                                TestOutcome.Passed,
                                "Test passed.\r\n\r\n"),
                            GetTestCaseResult(
                                testCaseOptions,
                                "ExportRunnerUnitTest.js::global::Test 2",
                                "Test 2",
                                1,
                                filePath,
                                TestOutcome.Failed,
                                "Test passed.\r\n\r\n", // TODO: Fix bug on the stdout stating that the test passed when the outcome is failure. Outcome is the corect status.
                                "AssertionError\r\nThis should fail\r\n",
                                "AssertionError\r\nThis should fail\r\n"),
                        };
                    }
                case SupportedFramework.Jest:
                    {
                        var filePath = Path.Combine(GetProjectDirPath(projectName), "JestUnitTest.js");
                        return new[]
                        {
                            GetTestCaseResult(
                                testCaseOptions,
                                "JestUnitTest.js::Test Suite 1::Test 1 - This shouldn't fail",
                                "Test 1 - This shouldn't fail",
                                3,
                                filePath,
                                TestOutcome.Passed),
                            GetTestCaseResult(
                                testCaseOptions,
                                "JestUnitTest.js::Test Suite 1::Test 2 - This should fail",
                                "Test 2 - This should fail",
                                7,
                                filePath,
                                TestOutcome.Failed),
                        };
                    }
                case SupportedFramework.Mocha:
                    {
                        var filePath = Path.Combine(GetProjectDirPath(projectName), "MochaUnitTest.js");
                        return new[]
                        {
                            GetTestCaseResult(
                                testCaseOptions,
                                "MochaUnitTest.js::Test Suite 1::Test 1",
                                "Test 1",
                                1,
                                filePath,
                                TestOutcome.Passed,
                                "Using default Mocha settings\r\nok 1 Test Suite 1 Test 1\r\n"), // TODO: Bug fix. Exclude the settings message.
                            GetTestCaseResult(
                                testCaseOptions,
                                "MochaUnitTest.js::Test Suite 1::Test 2",
                                "Test 2",
                                1,
                                filePath,
                                TestOutcome.Failed,
                                "not ok 2 Test Suite 1 Test 2\r\n  This should fail\r\n  AssertionError [ERR_ASSERTION]: This should fail\r\n      at Context.<anonymous> (MochaUnitTest.js:10:16)\r\n      at processImmediate (internal/timers.js:456:21)\r\n"),
                        };
                    }
                case SupportedFramework.Tape:
                    {
                        var filePath = Path.Combine(GetProjectDirPath(projectName), "TapeUnitTest.js");
                        return new[]
                        {
                            GetTestCaseResult(
                                testCaseOptions,
                                "TapeUnitTest.js::global::Test A",
                                "Test A",
                                1,
                                filePath,
                                TestOutcome.Passed,
                                "Operator: ok. Expected: true. Actual: true. evt: {\"id\":0,\"ok\":true,\"todo\":false,\"name\":\"This shouldn't fail\",\"operator\":\"ok\",\"objectPrintDepth\":5,\"actual\":true,\"expected\":true,\"test\":0,\"type\":\"assert\"}\r\n"),
                            GetTestCaseResult(
                                testCaseOptions,
                                "TapeUnitTest.js::global::Test B",
                                "Test B",
                                1,
                                filePath,
                                TestOutcome.Failed,
                                "Operator: ok. Expected: true. Actual: true. evt: {\"id\":0,\"ok\":true,\"todo\":false,\"name\":\"This shouldn't fail\",\"operator\":\"ok\",\"objectPrintDepth\":5,\"actual\":true,\"expected\":true,\"test\":1,\"type\":\"assert\"}\r\n",
                                "Operator: equal. Expected: 2. Actual: 1. evt: {\"id\":1,\"ok\":false,\"todo\":false,\"name\":\"This should fail\",\"operator\":\"equal\",\"objectPrintDepth\":5,\"actual\":1,\"expected\":2,\"error\":{},\"functionName\":\"Test.<anonymous>\",\"file\":\"C:\\\\Repos\\\\nodejstools\\\\Nodejs\\\\Tests\\\\MockProjects\\\\NodeAppWithTestsConfiguredPerFile\\\\TapeUnitTest.js:11:7\",\"line\":11,\"column\":7,\"at\":\"Test.<anonymous> (C:\\\\Repos\\\\nodejstools\\\\Nodejs\\\\Tests\\\\MockProjects\\\\NodeAppWithTestsConfiguredPerFile\\\\TapeUnitTest.js:11:7)\",\"test\":1,\"type\":\"assert\"}\r\nError: This should fail\r\n    at Test.assert [as _assert] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:260:54)\r\n    at Test.bound [as _assert] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Test.strictEqual (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:424:10)\r\n    at Test.bound [as equal] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Test.<anonymous> (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\TapeUnitTest.js:11:7)\r\n    at Test.bound [as _cb] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Test.run (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:101:31)\r\n    at Test.bound [as run] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Immediate.next [as _onImmediate] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\results.js:85:19)\r\n    at processImmediate (internal/timers.js:456:21)\r\n",
                                "Operator: equal. Expected: 2. Actual: 1. evt: {\"id\":1,\"ok\":false,\"todo\":false,\"name\":\"This should fail\",\"operator\":\"equal\",\"objectPrintDepth\":5,\"actual\":1,\"expected\":2,\"error\":{},\"functionName\":\"Test.<anonymous>\",\"file\":\"C:\\\\Repos\\\\nodejstools\\\\Nodejs\\\\Tests\\\\MockProjects\\\\NodeAppWithTestsConfiguredPerFile\\\\TapeUnitTest.js:11:7\",\"line\":11,\"column\":7,\"at\":\"Test.<anonymous> (C:\\\\Repos\\\\nodejstools\\\\Nodejs\\\\Tests\\\\MockProjects\\\\NodeAppWithTestsConfiguredPerFile\\\\TapeUnitTest.js:11:7)\",\"test\":1,\"type\":\"assert\"}\r\nError: This should fail\r\n    at Test.assert [as _assert] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:260:54)\r\n    at Test.bound [as _assert] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Test.strictEqual (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:424:10)\r\n    at Test.bound [as equal] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Test.<anonymous> (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\TapeUnitTest.js:11:7)\r\n    at Test.bound [as _cb] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Test.run (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:101:31)\r\n    at Test.bound [as run] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\test.js:84:32)\r\n    at Immediate.next [as _onImmediate] (C:\\Repos\\nodejstools\\Nodejs\\Tests\\MockProjects\\NodeAppWithTestsConfiguredPerFile\\node_modules\\tape\\lib\\results.js:85:19)\r\n    at processImmediate (internal/timers.js:456:21)\r\n"),
                        };
                    }
                case SupportedFramework.Angular:
                    return new[]
                    {
                        GetTestCaseResult(
                            testCaseOptions,
                            @"src\app\app.component.spec.ts::AppComponent::should create the app","should create the app",
                            14,
                            Path.Combine(GetProjectDirPath(projectName),
                            @"src\app\app.component.spec.ts"),
                            TestOutcome.Passed),
                        GetTestCaseResult(
                            testCaseOptions,
                            @"src\app\app.component.spec.ts::AppComponent::should have as title 'my-app'","should have as title 'my-app'",
                            20,
                            Path.Combine(GetProjectDirPath(projectName),
                            @"src\app\app.component.spec.ts"),
                            TestOutcome.Passed),
                        GetTestCaseResult(
                            testCaseOptions,
                            @"src\app\app.component.spec.ts::AppComponent::should render title","should render title",
                            26,
                            Path.Combine(GetProjectDirPath(projectName),
                            @"src\app\app.component.spec.ts"),
                            TestOutcome.Passed),
                        GetTestCaseResult(
                            testCaseOptions,
                            @"src\app\customTest.spec.ts::CustomTest::should succeed","should succeed",
                            4,
                            Path.Combine(GetProjectDirPath(projectName),
                            @"src\app\customTest.spec.ts"),
                            TestOutcome.Passed),
                        GetTestCaseResult(
                            testCaseOptions,
                            @"src\app\customTest.spec.ts::CustomTest::should fail","should fail",
                            8,
                            Path.Combine(GetProjectDirPath(projectName),
                            @"src\app\customTest.spec.ts"),
                            TestOutcome.Failed,
                            string.Empty,
                            "Error: Expected false to be truthy.\r\n    at <Jasmine>\r\n    at UserContext.<anonymous> (http://localhost:9876/_karma_webpack_/src/app/customTest.spec.ts:8:19)\r\n    at ZoneDelegate.invoke (http://localhost:9876/_karma_webpack_/node_modules/zone.js/dist/zone-evergreen.js:364:1)\r\n    at ProxyZoneSpec.push../node_modules/zone.js/dist/zone-testing.js.ProxyZoneSpec.onInvoke (http://localhost:9876/_karma_webpack_/node_modules/zone.js/dist/zone-testing.js:292:1)\r\n",
                            "Error: Expected false to be truthy.\r\n    at <Jasmine>\r\n    at UserContext.<anonymous> (http://localhost:9876/_karma_webpack_/src/app/customTest.spec.ts:8:19)\r\n    at ZoneDelegate.invoke (http://localhost:9876/_karma_webpack_/node_modules/zone.js/dist/zone-evergreen.js:364:1)\r\n    at ProxyZoneSpec.push../node_modules/zone.js/dist/zone-testing.js.ProxyZoneSpec.onInvoke (http://localhost:9876/_karma_webpack_/node_modules/zone.js/dist/zone-testing.js:292:1)\r\n"),
                    };
            }

            throw new NotImplementedException($"ProjectName {projectName} has not been implemented.");
        }

        private static TestCaseResult GetTestCaseResult(TestCaseOptions testCaseOptions, string fullyQualifiedName, string displayName, int lineNumber, string filePath, TestOutcome testOutcome, string standardOutMessage = "", string standardErrorMessage = "", string additionalInfo = "")
        {
            var testCase = new TestCase()
            {
                FullyQualifiedName = fullyQualifiedName,
                DisplayName = displayName,
                LineNumber = lineNumber,
                ExecutorUri = NodejsConstants.ExecutorUri,
                Source = testCaseOptions.Source,
                CodeFilePath = filePath,
            };

            testCase.SetPropertyValue(JavaScriptTestCaseProperties.TestFramework, testCaseOptions.TestFramework.ToString());
            testCase.SetPropertyValue(JavaScriptTestCaseProperties.WorkingDir, testCaseOptions.WorkingDir);
            testCase.SetPropertyValue(JavaScriptTestCaseProperties.ProjectRootDir, testCaseOptions.WorkingDir);
            testCase.SetPropertyValue(JavaScriptTestCaseProperties.NodeExePath, testCaseOptions.NodeExePath);
            testCase.SetPropertyValue(JavaScriptTestCaseProperties.TestFile, filePath);
            testCase.SetPropertyValue(JavaScriptTestCaseProperties.ConfigDirPath, testCaseOptions.ConfigDirPath);

            var testResult = new TestResult(testCase)
            {
                Outcome = testOutcome,
            };

            testResult.Messages.Add(new TestResultMessage(TestResultMessage.StandardOutCategory, standardOutMessage));
            testResult.Messages.Add(new TestResultMessage(TestResultMessage.StandardErrorCategory, standardErrorMessage));
            testResult.Messages.Add(new TestResultMessage(TestResultMessage.AdditionalInfoCategory, additionalInfo));

            return new TestCaseResult()
            {
                TestCase = testCase,
                TestResult = testResult
            };
        }
    }
}
