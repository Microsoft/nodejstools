﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.NodejsTools.TestAdapter.TestFrameworks;
using Microsoft.NodejsTools.TestFrameworks;
using Microsoft.NodejsTools.TypeScript;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Microsoft.NodejsTools.TestAdapter
{
    [FileExtension(".dll")]
    [DefaultExecutorUri(NodejsConstants.ExecutorUriString)]
    public sealed class NetCoreTestDiscoverer : ITestDiscoverer
    {
        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            // we can ignore the sources argument since this will be a collection of assemblies.
            ValidateArg.NotNull(discoverySink, nameof(discoverySink));
            ValidateArg.NotNull(logger, nameof(logger));

            // extract the project file from the discovery context.
            var unitTestSettings = new UnitTestSettings(discoveryContext.RunSettings);

            if (string.IsNullOrEmpty(unitTestSettings.TestSource))
            {
                // no need to log since the test executor will report 'no tests'
                return;
            }

            if (string.IsNullOrEmpty(unitTestSettings.TestFrameworksLocation))
            {
                logger.SendMessage(TestMessageLevel.Error, "Failed to locate the test frameworks.");
                return;
            }

            sources = new[] { unitTestSettings.TestSource };
            var frameworkDiscoverer = new FrameworkDiscoverer(unitTestSettings.TestFrameworksLocation);

            var projects = new List<(string projectFilePath, IEnumerable<XElement> propertyGroup)>();

            // There's an issue when loading the project using the .NET Core msbuild bits,
            // so we load the xml, and extract the properties we care about.
            // Downside is we only have the raw contents of the XmlElements, i.e. we don't
            // expand any variables.
            // The issue we encountered is that the msbuild implementation was not able to 
            // locate the SDK targets/props files. See: https://github.com/Microsoft/msbuild/issues/3434
            try
            {
                foreach (var source in sources)
                {
                    var cleanPath = source.Trim('\'', '"');

                    // we only support project files, e.g. csproj, vbproj, etc.
                    if (!cleanPath.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var project = XDocument.Load(cleanPath);

                    // structure looks like Project/PropertyGroup/JsTestRoot and Project/PropertyGroup/JsTestFramework
                    var propertyGroup = project.Descendants("Project").Descendants("PropertyGroup");

                    projects.Add((cleanPath, propertyGroup));
                }

                foreach (var (projectFile, propertyGroup) in projects)
                {
                    var projectHome = Path.GetDirectoryName(projectFile);
                    // Assume is Angular as it's the only one we have configuration files support so far.
                    var testFrameworkName = TestFrameworkDirectories.AngularFrameworkName;

                    // Prioritize configuration files over manually setup tests files.
                    var testItems = this.GetConfigItems(projectHome);
                    if (!testItems.Any())
                    {
                        testFrameworkName = propertyGroup.Descendants(NodeProjectProperty.TestFramework).FirstOrDefault()?.Value;
                        var testRoot = propertyGroup.Descendants(NodeProjectProperty.TestRoot).FirstOrDefault()?.Value;
                        var outDir = propertyGroup.Descendants(NodeProjectProperty.TypeScriptOutDir).FirstOrDefault()?.Value;

                        if (string.IsNullOrEmpty(testRoot) || string.IsNullOrEmpty(testFrameworkName))
                        {
                            logger.SendMessage(TestMessageLevel.Warning, $"No TestRoot or TestFramework specified for '{Path.GetFileName(projectFile)}'.");
                            continue;
                        }

                        var testFolder = Path.Combine(projectHome, testRoot);

                        if (!Directory.Exists(testFolder))
                        {
                            logger.SendMessage(TestMessageLevel.Warning, $"Test folder path '{testFolder}' doesn't exist.");
                            continue;
                        }

                        testItems = this.GetTestItems(testFolder, outDir);
                    }

                    if (testItems.Any())
                    {
                        var nodeExePath = Nodejs.GetAbsoluteNodeExePath(projectHome, propertyGroup.Descendants(NodeProjectProperty.NodeExePath).FirstOrDefault()?.Value);
                        if (string.IsNullOrEmpty(nodeExePath))
                        {
                            // if nothing specified in the project fallback to environment
                            nodeExePath = Nodejs.GetPathToNodeExecutableFromEnvironment();
                        }

                        this.DiscoverTests(testItems, frameworkDiscoverer, discoverySink, logger, nodeExePath, projectFile, testFrameworkName);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.SendMessage(TestMessageLevel.Error, ex.Message);
                throw;
            }
        }

        private void DiscoverTests(IEnumerable<string> testItems, FrameworkDiscoverer frameworkDiscoverer, ITestCaseDiscoverySink discoverySink, IMessageLogger logger, string nodeExePath, string projectFullPath, string testFrameworkName)
        {
            var testFramework = frameworkDiscoverer.GetFramework(testFrameworkName);
            if (testFramework == null)
            {
                logger.SendMessage(TestMessageLevel.Warning, $"Ignoring unsupported test framework '{testFrameworkName}'.");
            }

            var discoverWorker = new TestDiscovererWorker(projectFullPath, nodeExePath);
            discoverWorker.DiscoverTests(testItems, testFramework, logger, discoverySink, nameof(NetCoreTestDiscoverer));
        }

        private IEnumerable<string> GetConfigItems(string projectRoot)
        {
            return Directory.EnumerateFiles(projectRoot, "angular.json", SearchOption.AllDirectories)
                .Where(x => !x.Contains("\\node_modules\\"));
        }

        private IEnumerable<string> GetTestItems(string projectRoot, string outDir)
        {
            // TODO: Do our own directory traversal. It's better for performance.

            // If we find ts or tsx files, get the JS file and return.
            var files = Directory.EnumerateFiles(projectRoot, "*.ts?", SearchOption.AllDirectories)
                .Where(x => !x.Contains("\\node_modules\\"));
            if (files.Any())
            {
                return files
                    .Where(p => TypeScriptHelpers.IsTypeScriptFile(p))
                    .Select(p => TypeScriptHelpers.GetTypeScriptBackedJavaScriptFile(p, outDir, projectRoot));
            }

            return Directory.EnumerateFiles(projectRoot, "*.js", SearchOption.AllDirectories)
                .Where(p => !p.Contains("\\node_modules\\"));
        }
    }
}
