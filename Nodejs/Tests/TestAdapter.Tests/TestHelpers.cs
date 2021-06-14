// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.NodejsTools.TestAdapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using TestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;

namespace TestAdapter.Tests
{
    public static class TestHelpers
    {
        public static void AssertTestCasesAreEqual(IEnumerable<TestCase> expected, IEnumerable<TestCase> actual)
        {
            if (expected.Count() != actual.Count())
            {
                Assert.Fail($"Expected and actual does not have the same amount of items. Expected.Count = {expected.Count()}, Actual.Count = {actual.Count()}");
            }

            var expectedCopy = new List<TestCase>(expected);
            foreach (var testCase in actual)
            {
                var found = expectedCopy.Find(x => AreTestCasesEqual(x, testCase));
                if (found == null)
                {
                    Assert.Fail($"Expected does not have item: {JsonConvert.SerializeObject(testCase)}");
                }

                expectedCopy.Remove(found);
            }
        }

        public static void AssertTestResultsAreEqual(IEnumerable<TestResult> expected, IEnumerable<TestResult> actual)
        {
            if (expected.Count() != actual.Count())
            {
                Assert.Fail($"Expected and actual does not have the same amount of items. Expected.Count = {expected.Count()}, Actual.Count = {actual.Count()}");
            }

            var expectedCopy = new List<TestResult>(expected);
            foreach (var testResult in actual)
            {
                var found = expectedCopy.Find(x =>
                    AreTestCasesEqual(x.TestCase, testResult.TestCase)
                    && x.Outcome == testResult.Outcome
                    && AreMessagesEqual(x.Messages, testResult.Messages));

                if (found == null)
                {
                    Assert.Fail($"Expected does not have item: {JsonConvert.SerializeObject(testResult)}");
                }

                expectedCopy.Remove(found);
            }
        }

        public static void AssertRecordEnds(IEnumerable<(TestCase, TestOutcome)> expected, IEnumerable<(TestCase, TestOutcome)> actual)
        {
            if (expected.Count() != actual.Count())
            {
                Assert.Fail($"Expected and actual does not have the same amount of items. Expected.Count = {expected.Count()}, Actual.Count = {actual.Count()}");
            }

            var expectedCopy = new List<(TestCase, TestOutcome)>(expected);
            foreach (var (testCase, testOutcome) in actual)
            {
                var found = expectedCopy.Find(x =>
                    AreTestCasesEqual(x.Item1, testCase)
                    && x.Item2 == testOutcome);

                if (found.Item1 == null)
                {
                    Assert.Fail($"Expected does not have item: {JsonConvert.SerializeObject(testCase)}");
                }

                expectedCopy.Remove(found);
            }
        }

        public static void AssureNodeModules(string path)
        {
            if (Directory.Exists(Path.Combine(path, "node_modules")))
            {
                return;
            }

            var processStartInfo = new ProcessStartInfo()
            {
                FileName = "cmd.exe",
                Arguments = "/C npm install",
                WorkingDirectory = path
            };

            var process = Process.Start(processStartInfo);
            process.WaitForExit(10 * 60 * 1000); // 10 minutes
        }

        private static bool PathEquals(string path1, string path2)
        {
            return (path1 == null && path2 == null)
                || (path1 != null && path2 != null && string.Equals(Path.GetFullPath(path1), Path.GetFullPath(path2), StringComparison.OrdinalIgnoreCase));
        }

        private static bool AreTestCasesEqual(TestCase expected, TestCase actual)
        {
            return expected.FullyQualifiedName == actual.FullyQualifiedName
                && expected.DisplayName == actual.DisplayName
                && expected.LineNumber == actual.LineNumber
                && expected.ExecutorUri == actual.ExecutorUri
                && PathEquals(expected.Source, actual.Source)
                && PathEquals(expected.CodeFilePath, actual.CodeFilePath)
                && string.Equals(expected.GetPropertyValue<string>(JavaScriptTestCaseProperties.TestFramework, default), actual.GetPropertyValue<string>(JavaScriptTestCaseProperties.TestFramework, default), StringComparison.OrdinalIgnoreCase) // For some reason, mocha test framework is all lowercase.
                && PathEquals(expected.GetPropertyValue<string>(JavaScriptTestCaseProperties.WorkingDir, default), actual.GetPropertyValue<string>(JavaScriptTestCaseProperties.WorkingDir, default))
                && PathEquals(expected.GetPropertyValue<string>(JavaScriptTestCaseProperties.ProjectRootDir, default), actual.GetPropertyValue<string>(JavaScriptTestCaseProperties.ProjectRootDir, default))
                && PathEquals(expected.GetPropertyValue<string>(JavaScriptTestCaseProperties.TestFile, default), actual.GetPropertyValue<string>(JavaScriptTestCaseProperties.TestFile, default))
                && PathEquals(expected.GetPropertyValue<string>(JavaScriptTestCaseProperties.ConfigDirPath, default), actual.GetPropertyValue<string>(JavaScriptTestCaseProperties.ConfigDirPath, default));
        }

        private static bool AreMessagesEqual(IEnumerable<TestResultMessage> expected, IEnumerable<TestResultMessage> actual)
        {
            if (expected.Count() != actual.Count())
            {
                return false;
            }

            var expectedCopy = new List<TestResultMessage>(expected);
            foreach (var message in actual)
            {
                var found = expectedCopy.Find(x =>
                    x.Category == message.Category
                    && x.Text == message.Text);

                if (found == null)
                {
                    return false;
                }

                expectedCopy.Remove(found);
            }

            return true;
        }
    }
}
