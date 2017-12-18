﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.NodejsTools.Npm;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace Microsoft.NodejsTools.Workspace
{
    [ExportFileScanner(
        ProviderType, "PackageJsonProject",
        new string[] { "package.json" },
        new Type[] { typeof(IReadOnlyCollection<FileDataValue>), typeof(IReadOnlyCollection<FileReferenceInfo>) })]
    public sealed class PackageJsonScannerFactory : IWorkspaceProviderFactory<IFileScanner>
    {
        private const string ProviderType = "{D5D48E50-C894-4C5E-8666-761E2D7B301E}";
        private static readonly Guid ProviderTypeGuid = new Guid(ProviderType);

        public IFileScanner CreateProvider(IWorkspace workspaceContext)
        {
            return new PackageJsonScanner(workspaceContext);
        }

        private sealed class PackageJsonScanner : BaseFileScanner, IFileScanner, IFileScannerUpToDateCheck
        {
            public PackageJsonScanner(IWorkspace workspaceContext)
                : base(workspaceContext)
            {
            }

            protected override Task<bool> IsValidFileAsync(string filePath)
            {
                return Task.FromResult(PackageJsonHelpers.IsPackageJsonFile(filePath));
            }

            protected override Task<List<FileReferenceInfo>> ComputeFileReferencesAsync(string filePath, CancellationToken cancellationToken)
            {
                var packageJson = PackageJsonFactory.Create(filePath);

                var fileReferences = new List<FileReferenceInfo>();

                var main = packageJson.Main;

                fileReferences.Add(new FileReferenceInfo(main,
                    context: "Debug",
                    target: main,
                    referenceType: (int)FileReferenceInfoType.Output));

                return Task.FromResult(fileReferences);
            }

            protected override Task<List<FileDataValue>> ComputeFileDataValuesAsync(string filePath, CancellationToken cancellationToken)
            {
                var packageJson = PackageJsonFactory.Create(this.EnsureRooted(filePath));

                Debug.Assert(packageJson != null);

                var main = packageJson.Main;

                var fileDataValues = new List<FileDataValue>();
                if (!string.IsNullOrEmpty(main))
                {
                    var launchSettings = new PropertySettings
                    {
                        [LaunchConfigurationConstants.NameKey] = $"node {main} (package.json)",
                        [LaunchConfigurationConstants.TypeKey] = "default"
                    };

                    fileDataValues.Add(new FileDataValue(
                        DebugLaunchActionContext.ContextTypeGuid,
                        DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                        launchSettings,
                        target: main));

                    // Target has to match the name used in the debug action context so it can be found during project configuration
                    fileDataValues.Add(new FileDataValue(DebugLaunchActionContext.ContextTypeGuid, main, null, target: main));
                    // Also need a null target so that can be found for the context menu when querying for build configurations. 
                    // (See Microsoft.VisualStudio.Workspace.VSIntegration.UI.FileContextActionsCommandHandlersProvider.Provider.GetActionProviderForProjectConfiguration)
                    fileDataValues.Add(new FileDataValue(DebugLaunchActionContext.ContextTypeGuid, main, null, target: null));

                    fileDataValues.Add(new FileDataValue(
                        BuildConfigurationContext.ContextTypeGuid,
                        name: main,
                        value: null,
                        context: "Debug",
                        target: main));

                    fileDataValues.Add(new FileDataValue(
                        BuildConfigurationContext.ContextTypeGuid,
                        main,
                        null,
                        target: null)); // Also need a null target so that can be found for the context menu when querying for build configurations.
                }
                return Task.FromResult(fileDataValues);
            }
        }
    }
}
