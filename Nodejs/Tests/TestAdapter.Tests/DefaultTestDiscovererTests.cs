// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.NodejsTools.TestAdapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace TestAdapter.Tests
{
    [TestClass]
    public class DefaultTestDiscovererTests
    {
        private static void AssertProject(TestProjectFactory.ProjectName projectName, int expectedTests)
        {
            // Arrange
            var actual = new List<TestCase>();
            var expected = TestProjectFactory.GetTestCaseResults(projectName).Select(x => x.TestCase);

            var discoverContext = new Mock<IDiscoveryContext>();
            var messageLogger = new Mock<IMessageLogger>();
            var testCaseDiscoverySink = new Mock<ITestCaseDiscoverySink>();

            testCaseDiscoverySink.Setup(x => x.SendTestCase(It.IsAny<TestCase>()))
                .Callback<TestCase>(x => actual.Add(x));

            var testSource = TestProjectFactory.GetProjectFilePath(projectName);
            var sources = new List<string>() { testSource };

            TestHelpers.AssureNodeModules(TestProjectFactory.GetProjectDirPath(projectName));

            var testDiscoverer = new DefaultTestDiscoverer();

            // Act
            testDiscoverer.DiscoverTests(sources, discoverContext.Object, messageLogger.Object, testCaseDiscoverySink.Object);

            // Assert
            testCaseDiscoverySink.Verify(x => x.SendTestCase(It.IsAny<TestCase>()), Times.Exactly(expectedTests));
            TestHelpers.AssertTestCasesAreEqual(expected, actual);
        }

        [TestInitialize]
        public void InitializeTests()
        {
            // Setup the variable due to LoadProjects looks for a specific path of Microsoft.NodejsToolsV2.targets.
            // TODO: Probably we could remove this dependency by configuring the BuildOutput path, but I haven't found how to do it.
            Environment.SetEnvironmentVariable("VSINSTALLDIR", @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview");
        }

        [TestMethod]
        public void DiscoversTests_NodeAppWithTestsConfiguredPerFile()
        {
            AssertProject(TestProjectFactory.ProjectName.NodeAppWithTestsConfiguredPerFile, 10);
        }

        [TestMethod]
        public void DiscoversTests_NodeAppWithTestsConfiguredOnProject()
        {
            AssertProject(TestProjectFactory.ProjectName.NodeAppWithTestsConfiguredOnProject, 2);
        }

        [TestMethod]
        public void DiscoversTests_NodeAppWithAngularTests()
        {
            AssertProject(TestProjectFactory.ProjectName.NodeAppWithAngularTests, 5);
        }
    }
}
