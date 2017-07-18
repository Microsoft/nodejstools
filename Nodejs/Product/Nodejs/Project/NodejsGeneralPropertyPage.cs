// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.NodejsTools.Project
{
    [Guid("62E8E091-6914-498E-A47B-6F198DC1873D")]
    internal class NodejsGeneralPropertyPage : CommonPropertyPage
    {
        private readonly NodejsGeneralPropertyPageControl _control;

        public NodejsGeneralPropertyPage()
        {
            this._control = new NodejsGeneralPropertyPageControl(this);
        }

        public override System.Windows.Forms.Control Control => this._control;

        internal override CommonProjectNode Project
        {
            get
            {
                return base.Project;
            }
            set
            {
                if (value == null && base.Project != null)
                {
                    base.Project.PropertyPage = null;
                }
                base.Project = value;
                if (value != null)
                {
                    ((NodejsProjectNode)value).PropertyPage = this;
                }
            }
        }
        public override void Apply()
        {
            this.Project.SetProjectProperty(NodeProjectProperty.NodeExePath, this._control.NodeExePath);
            this.Project.SetProjectProperty(NodeProjectProperty.NodeExeArguments, this._control.NodeExeArguments);
            this.Project.SetProjectProperty(CommonConstants.StartupFile, this._control.ScriptFile);
            this.Project.SetProjectProperty(NodeProjectProperty.ScriptArguments, this._control.ScriptArguments);
            this.Project.SetProjectProperty(NodeProjectProperty.NodejsPort, this._control.NodejsPort);
            this.Project.SetProjectProperty(NodeProjectProperty.StartWebBrowser, this._control.StartWebBrowser.ToString());
            this.Project.SetProjectProperty(CommonConstants.WorkingDirectory, this._control.WorkingDirectory);
            this.Project.SetProjectProperty(NodeProjectProperty.LaunchUrl, this._control.LaunchUrl);
            this.Project.SetProjectProperty(NodeProjectProperty.DebuggerPort, this._control.DebuggerPort);
            this.Project.SetProjectProperty(NodeProjectProperty.Environment, this._control.Environment);
            this.IsDirty = false;
        }

        public override void LoadSettings()
        {
            this.Loading = true;
            try
            {
                this._control.NodeExeArguments = this.Project.GetUnevaluatedProperty(NodeProjectProperty.NodeExeArguments);
                this._control.NodeExePath = this.Project.GetUnevaluatedProperty(NodeProjectProperty.NodeExePath);
                this._control.ScriptFile = this.Project.GetUnevaluatedProperty(CommonConstants.StartupFile);
                this._control.ScriptArguments = this.Project.GetUnevaluatedProperty(NodeProjectProperty.ScriptArguments);
                this._control.WorkingDirectory = this.Project.GetUnevaluatedProperty(CommonConstants.WorkingDirectory);
                this._control.LaunchUrl = this.Project.GetUnevaluatedProperty(NodeProjectProperty.LaunchUrl);
                this._control.NodejsPort = this.Project.GetUnevaluatedProperty(NodeProjectProperty.NodejsPort);
                this._control.DebuggerPort = this.Project.GetUnevaluatedProperty(NodeProjectProperty.DebuggerPort);
                this._control.Environment = this.Project.GetUnevaluatedProperty(NodeProjectProperty.Environment);

                // Attempt to parse the boolean.  If we fail, assume it is true.
                if (!Boolean.TryParse(this.Project.GetUnevaluatedProperty(NodeProjectProperty.StartWebBrowser), out var startWebBrowser))
                {
                    startWebBrowser = true;
                }
                this._control.StartWebBrowser = startWebBrowser;
            }
            finally
            {
                this.Loading = false;
            }
        }

        public override string Name => "General";
    }
}
