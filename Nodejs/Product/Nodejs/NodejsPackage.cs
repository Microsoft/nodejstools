// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.NodejsTools.Commands;
using Microsoft.NodejsTools.Options;
using Microsoft.NodejsTools.Project;
using Microsoft.NodejsTools.ProjectWizard;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.InteractiveWindow.Shell;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudioTools;
using MigrateToJsps;

namespace Microsoft.NodejsTools
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    internal sealed class ProvideUnifiedSettingsOptionPageAttribute : RegistrationAttribute
    {
        private readonly string categoryName;
        private readonly string pageName;

        public ProvideUnifiedSettingsOptionPageAttribute(string categoryName, string pageName)
        {
            this.categoryName = categoryName;
            this.pageName = pageName;
        }

        public override void Register(RegistrationContext context)
        {
            using (var pageKey = context.CreateKey(
                string.Format(
                    CultureInfo.InvariantCulture,
                    @"ToolsOptionsPages\{0}\{1}",
                    this.categoryName,
                    this.pageName)))
            {
                pageKey.SetValue("IsInUnifiedSettings", 1);
            }
        }

        public override void Unregister(RegistrationContext context)
        {
        }
    }

    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [Guid(Guids.NodejsPackageString)]
    [ProvideOptionPage(typeof(NodejsGeneralOptionsPage), "Node.js Tools", "General", 114, 115, true)]
    [ProvideUnifiedSettingsOptionPage("Node.js Tools", "General")]
    [WebSiteProject("JavaScript", "JavaScript")]
    [ProvideProjectFactory(typeof(NodejsProjectFactory), null, null, null, null, ".\\NullPath", LanguageVsTemplate = NodejsConstants.Nodejs, SortPriority = 0x17)]   // outer flavor, no file extension
    [ProvideMenuResource("Menus.ctmenu", 1)]                              // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideLanguageTemplates("{349C5851-65DF-11DA-9384-00065B846F21}", NodejsConstants.JavaScript, Guids.NodejsPackageString, "Web", "Node.js Project Templates", "{" + Guids.NodejsBaseProjectFactoryString + "}", ".js", NodejsConstants.Nodejs, "{" + Guids.NodejsBaseProjectFactoryString + "}")]
    [ProvideProjectItem(typeof(BaseNodeProjectFactory), NodejsConstants.Nodejs, "FileTemplates\\NewItem", 0)]
    internal sealed partial class NodejsPackage : CommonPackage
    {
        internal const string NodeExpressionEvaluatorGuid = "{F16F2A71-1C45-4BAB-BECE-09D28CFDE3E6}";
        internal static NodejsPackage Instance;
        internal HashSet<ITextBuffer> ChangedBuffers = new HashSet<ITextBuffer>();

        // Hold references for the subscribed events. Otherwise the callbacks will be garbage collected
        // after the initialization
        private readonly List<EnvDTE.CommandEvents> subscribedCommandEvents = new List<EnvDTE.CommandEvents>();

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public NodejsPackage()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
            Debug.Assert(Instance == null, "NodejsPackage created multiple times");
            Instance = this;
        }

        public NodejsGeneralOptionsPage GeneralOptionsPage => GetDialogPage<NodejsGeneralOptionsPage>();

        public EnvDTE.DTE DTE => (EnvDTE.DTE)GetService(typeof(EnvDTE.DTE));

        /////////////////////////////////////////////////////////////////////////////
        // Overridden Package Implementation
        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));
            base.Initialize();

            SubscribeToVsCommandEvents(
                (int)VSConstants.VSStd97CmdID.AddNewProject,
                delegate { NewProjectFromExistingWizard.IsAddNewProjectCmd = true; },
                delegate { NewProjectFromExistingWizard.IsAddNewProjectCmd = false; }
            );

            ((IServiceContainer)this).AddService(typeof(ClipboardServiceBase), new ClipboardService(), true);

            RegisterProjectFactory(new NodejsProjectFactory(this));

            // Add our command handlers for menu (commands must exist in the .vsct file)
            var importWizardCmd = new List<Command> {
                new ImportWizardCommand()
            };

            var migrateToJspsCmd = new List<Command> {
                new MigrateToJspsCommand(),
                new RevertMigrationCommand()
            };

            RegisterCommands(importWizardCmd, Guids.NodejsCmdSet);
            RegisterCommands(migrateToJspsCmd, Guids.MigrateToJspsCmdSet);

            // The variable is inherited by child processes backing Test Explorer, and is used in
            // the NTVS test discoverer and test executor to connect back to VS.
            Environment.SetEnvironmentVariable(NodejsConstants.NodeToolsProcessIdEnvironmentVariable, Process.GetCurrentProcess().Id.ToString());
        }

        private void SubscribeToVsCommandEvents(
            int eventId,
            EnvDTE._dispCommandEvents_BeforeExecuteEventHandler beforeExecute = null,
            EnvDTE._dispCommandEvents_AfterExecuteEventHandler afterExecute = null)
        {
            var commandEventGuid = typeof(VSConstants.VSStd97CmdID).GUID.ToString("B");
            var targetEvent = this.DTE.Events.CommandEvents[commandEventGuid, eventId];
            if (beforeExecute != null)
            {
                targetEvent.BeforeExecute += beforeExecute;
            }
            if (afterExecute != null)
            {
                targetEvent.AfterExecute += afterExecute;
            }
            this.subscribedCommandEvents.Add(targetEvent);
        }

        public new IComponentModel ComponentModel => this.GetComponentModel();

        internal static bool TryGetStartupFileAndDirectory(System.IServiceProvider serviceProvider, out string fileName, out string directory)
        {
            var startupProject = GetStartupProject(serviceProvider);
            if (startupProject != null)
            {
                fileName = startupProject.GetStartupFile();
                directory = startupProject.GetWorkingDirectory();
            }
            else
            {
                var textView = CommonPackage.GetActiveTextView(serviceProvider);
                if (textView == null)
                {
                    fileName = null;
                    directory = null;
                    return false;
                }
                fileName = textView.GetFilePath();
                directory = Path.GetDirectoryName(fileName);
            }
            return true;
        }

        #endregion

        internal override VisualStudioTools.Navigation.LibraryManager CreateLibraryManager(CommonPackage package)
        {
            return new NodejsLibraryManager(this);
        }

        public override Type GetLibraryManagerType()
        {
            return typeof(NodejsLibraryManager);
        }

        public override bool IsRecognizedFile(string filename)
        {
            var ext = Path.GetExtension(filename);

            return StringComparer.OrdinalIgnoreCase.Equals(ext, NodejsConstants.JavaScriptExtension);
        }

        internal new object GetService(Type serviceType)
        {
            return base.GetService(serviceType);
        }

        private T GetDialogPage<T>() where T : NodejsDialogPage
        {
            return (T)GetDialogPage(typeof(T));
        }

        public string BrowseForDirectory(IntPtr owner, string initialDirectory = null)
        {
            var uiShell = GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell == null)
            {
                using (var ofd = new FolderBrowserDialog())
                {
                    ofd.RootFolder = Environment.SpecialFolder.Desktop;
                    ofd.ShowNewFolderButton = false;
                    DialogResult result;
                    if (owner == IntPtr.Zero)
                    {
                        result = ofd.ShowDialog();
                    }
                    else
                    {
                        result = ofd.ShowDialog(NativeWindow.FromHandle(owner));
                    }
                    if (result == DialogResult.OK)
                    {
                        return ofd.SelectedPath;
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            if (owner == IntPtr.Zero)
            {
                ErrorHandler.ThrowOnFailure(uiShell.GetDialogOwnerHwnd(out owner));
            }

            var browseInfo = new VSBROWSEINFOW[1];
            browseInfo[0].lStructSize = (uint)Marshal.SizeOf(typeof(VSBROWSEINFOW));
            browseInfo[0].pwzInitialDir = initialDirectory;
            browseInfo[0].hwndOwner = owner;
            browseInfo[0].nMaxDirName = 260;
            var pDirName = IntPtr.Zero;
            try
            {
                browseInfo[0].pwzDirName = pDirName = Marshal.AllocCoTaskMem(520);
                var hr = uiShell.GetDirectoryViaBrowseDlg(browseInfo);
                if (hr == VSConstants.OLE_E_PROMPTSAVECANCELLED)
                {
                    return null;
                }
                ErrorHandler.ThrowOnFailure(hr);
                return Marshal.PtrToStringAuto(browseInfo[0].pwzDirName);
            }
            finally
            {
                if (pDirName != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pDirName);
                }
            }
        }
    }
}
