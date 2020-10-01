// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.NodejsTools.TestAdapter.TestFrameworks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudioTools;
using MSBuild = Microsoft.Build.Evaluation;

namespace Microsoft.NodejsTools.TestAdapter
{
    // Keep in sync the method TypeScriptHelpers.IsSupportedTestProjectFile if there's a change on the supported projects.
    [FileExtension(NodejsConstants.NodejsProjectExtension), FileExtension(NodejsConstants.CSharpProjectExtension), FileExtension(NodejsConstants.VisualBasicProjectExtension)]
    [DefaultExecutorUri(NodejsConstants.ExecutorUriString)]
    public partial class ProjectTestDiscoverer : ITestDiscoverer
    {
        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            AssemblyResolver.SetupHandler();
            this.DiscoverTestsCore(sources, logger, discoverySink);
        }

        /// <summary>
        /// ITestDiscover, Given a list of test sources this method pulls out the test cases
        /// </summary>
        /// <param name="sources">List of test sources passed from client (Client can be VS or command line)</param>
        /// <param name="logger">Used to relay messages to registered loggers</param>
        /// <param name="discoverySink">Callback used to notify client upon discovery of test cases</param>
        private void DiscoverTestsCore(IEnumerable<string> sources, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            ValidateArg.NotNull(sources, nameof(sources));
            ValidateArg.NotNull(discoverySink, nameof(discoverySink));
            ValidateArg.NotNull(logger, nameof(logger));

            var env = new Dictionary<string, string>();
            var root = Environment.GetEnvironmentVariable("VSINSTALLDIR");

#if DEBUG
            logger.SendMessage(TestMessageLevel.Informational, $"VSINSTALLDIR: {root}");
#endif

            if (!string.IsNullOrEmpty(root))
            {
                env["VsInstallRoot"] = root;
                env["MSBuildExtensionsPath32"] = Path.Combine(root, "MSBuild");
            }

            using (var buildEngine = new MSBuild.ProjectCollection(env))
            {
                try
                {
                    // Load all the test containers passed in (.njsproj msbuild files)
                    foreach (var source in sources)
                    {
                        var cleanPath = source.Trim('\'', '"');
                        buildEngine.LoadProject(cleanPath);
                    }

                    FrameworkDiscoverer frameworkDiscoverer = null;

                    foreach (var proj in buildEngine.LoadedProjects)
                    {
                        var projectHome = Path.GetFullPath(Path.Combine(proj.DirectoryPath, "."));

                        var testItems = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                        var testRoot = proj.GetProperty(NodeProjectProperty.TestRoot)?.EvaluatedValue;
                        var testFramework = proj.GetProperty(NodeProjectProperty.TestFramework)?.EvaluatedValue;

                        if (!string.IsNullOrEmpty(testRoot) && string.IsNullOrEmpty(testFramework))
                        {
                            logger.SendMessage(TestMessageLevel.Warning, $"TestRoot specified for '{Path.GetFileName(proj.FullPath)}' but no TestFramework.");
                        }

                        // Provide all files to the test analyzer
                        foreach (var item in proj.Items)
                        {
                            string testFrameworkName;
                            string fileAbsolutePath;
                            if (!string.IsNullOrEmpty(testRoot) && !string.IsNullOrEmpty(testFramework))
                            {
                                testFrameworkName = testFramework;
                                var testRootPath = Path.GetFullPath(Path.Combine(proj.DirectoryPath, testRoot));

                                try
                                {
                                    fileAbsolutePath = CommonUtils.GetAbsoluteFilePath(projectHome, item.EvaluatedInclude);
                                }
                                catch (ArgumentException)
                                {
                                    // .Net core projects include invalid paths, ignore them and continue checking the items.
                                    continue;
                                }

                                if (!fileAbsolutePath.StartsWith(testRootPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                //Check to see if this is a TestCase
                                testFrameworkName = item.GetMetadataValue("TestFramework");
                                if (!TestFramework.IsValidTestFramework(testFrameworkName))
                                {
                                    continue;
                                }
                                fileAbsolutePath = CommonUtils.GetAbsoluteFilePath(projectHome, item.EvaluatedInclude);
                            }

                            var typeScriptTest = TypeScript.TypeScriptHelpers.IsTypeScriptFile(fileAbsolutePath);
                            if (typeScriptTest)
                            {
                                fileAbsolutePath = TypeScript.TypeScriptHelpers.GetTypeScriptBackedJavaScriptFile(proj, fileAbsolutePath);
                            }
                            else if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(fileAbsolutePath), ".js"))
                            {
                                continue;
                            }

                            if (!testItems.TryGetValue(testFrameworkName, out var fileList))
                            {
                                fileList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                testItems.Add(testFrameworkName, fileList);
                            }
                            fileList.Add(fileAbsolutePath);
                        }

                        if (testItems.Any())
                        {
                            frameworkDiscoverer = frameworkDiscoverer ?? new FrameworkDiscoverer();

                            var nodeExePath = Nodejs.GetAbsoluteNodeExePath(projectHome, proj.GetPropertyValue(NodeProjectProperty.NodeExePath));
                            if (string.IsNullOrEmpty(nodeExePath))
                            {
                                // if nothing specified in the project fallback to environment
                                nodeExePath = Nodejs.GetPathToNodeExecutableFromEnvironment();
                            }

                            var spaRoot = proj.GetProperty("SpaRoot")?.EvaluatedValue;
                            var workDir = Path.GetDirectoryName(proj.FullPath);
                            if (!String.IsNullOrEmpty(spaRoot))
                            {
                                workDir = Path.Combine(workDir, spaRoot);
                            }
                            this.DiscoverTests(testItems, frameworkDiscoverer, discoverySink, logger, nodeExePath,
                                proj.FullPath, workDir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.SendMessage(TestMessageLevel.Error, ex.Message);
                    throw;
                }
                finally
                {
                    // Disposing buildEngine does not clear the document cache in
                    // VS 2013, so manually unload all projects before disposing.
                    buildEngine.UnloadAllProjects();
                }
            }
        }

        private void DiscoverTests(Dictionary<string, HashSet<string>> testItems, FrameworkDiscoverer frameworkDiscoverer, ITestCaseDiscoverySink discoverySink, IMessageLogger logger, string nodeExePath, string projectFullPath, string workDir)
        {
            foreach (var testFx in testItems.Keys)
            {
                var testFramework = frameworkDiscoverer.GetFramework(testFx);
                if (testFramework == null)
                {
                    logger.SendMessage(TestMessageLevel.Warning, $"Ignoring unsupported test framework '{testFx}'.");
                    continue;
                }

                var fileList = testItems[testFx];

                var discoverWorker = new TestDiscovererWorker(projectFullPath, NodejsConstants.ExecutorUri, nodeExePath, workDir);
                discoverWorker.DiscoverTests(fileList, testFramework, logger, discoverySink);
            }
        }
    }
}
