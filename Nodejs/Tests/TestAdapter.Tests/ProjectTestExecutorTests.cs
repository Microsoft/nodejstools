// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.NodejsTools.TestAdapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using TestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;

namespace TestAdapter.Tests
{
    [TestClass]
    public class ProjectTestExecutorTests
    {
        private static void AssertProject(TestProjectFactory.ProjectName projectName)
        {
            // Arrange
            var testCaseResult = TestProjectFactory.GetTestCaseResults(projectName);
            var expectedTestCases = testCaseResult.Select(x => x.TestCase);
            var expectedResults = testCaseResult.Select(x => x.TestResult);
            var expectedEnds = testCaseResult.Select(x => (x.TestCase, x.TestResult.Outcome));

            var runContext = new Mock<IRunContext>();
            runContext.Setup(x => x.RunSettings.SettingsXml)
                .Returns(@"<EmptyXml></EmptyXml>");

            var actualStarts = new List<TestCase>();
            var actualResults = new List<TestResult>();
            var actualEnds = new List<(TestCase, TestOutcome)>();

            var frameworkHandle = new Mock<IFrameworkHandle>();
            frameworkHandle.Setup(x => x.RecordStart(It.IsAny<TestCase>()))
                .Callback<TestCase>(x => actualStarts.Add(x));
            frameworkHandle.Setup(x => x.RecordResult(It.IsAny<TestResult>()))
                .Callback<TestResult>(x => actualResults.Add(x));
            frameworkHandle.Setup(x => x.RecordEnd(It.IsAny<TestCase>(), It.IsAny<TestOutcome>()))
                .Callback<TestCase, TestOutcome>((tc, to) => actualEnds.Add((tc, to)));

            TestHelpers.AssureNodeModules(TestProjectFactory.GetProjectDirPath(projectName));

            var testExecutor = new ProjectTestExecutor();

            // Act
            testExecutor.RunTests(expectedTestCases, runContext.Object, frameworkHandle.Object);

            // Assert
            frameworkHandle.Verify(x => x.RecordStart(It.IsAny<TestCase>()), Times.Exactly(expectedTestCases.Count()));
            frameworkHandle.Verify(x => x.RecordResult(It.IsAny<TestResult>()), Times.Exactly(expectedTestCases.Count()));
            frameworkHandle.Verify(x => x.RecordEnd(It.IsAny<TestCase>(), It.IsAny<TestOutcome>()), Times.Exactly(expectedTestCases.Count()));

            TestHelpers.AssertTestCasesAreEqual(expectedTestCases, actualStarts);
            TestHelpers.AssertTestResultsAreEqual(expectedResults, actualResults);
            TestHelpers.AssertRecordEnds(expectedEnds, actualEnds);

        }

        [TestInitialize]
        public void InitializeTests()
        {
            // Setup the variable due to LoadProjects looks for a specific path of Microsoft.NodejsToolsV2.targets.
            // TODO: Probably we could remove this dependency by configuring the BuildOutput path, but I haven't found how to do it.
            Environment.SetEnvironmentVariable("VSINSTALLDIR", @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview");
        }

        [TestMethod]
        public void ExecutesTests_NodeAppWithTestsConfiguredPerFile()
        {
            // TODO: Note that this tests is going to fail until the following issues had been resolved:
            // - Jasmine stdout contains the configuration output plus a random seed. Probably don't want to include it.
            // - Jest test does not runs the tests. This is an issue with the test not the adapter.
            AssertProject(TestProjectFactory.ProjectName.NodeAppWithTestsConfiguredPerFile);
        }

        [TestMethod]
        public void ExecutesTests_NodeAppWithTestsConfiguredOnProject()
        {
            AssertProject(TestProjectFactory.ProjectName.NodeAppWithTestsConfiguredOnProject);
        }

        [TestMethod]
        public void ExecutesTests_NodeAppWithAngularTests()
        {
            AssertProject(TestProjectFactory.ProjectName.NodeAppWithAngularTests);
        }
    }
}
