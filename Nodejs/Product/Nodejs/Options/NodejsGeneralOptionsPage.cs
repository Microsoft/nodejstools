// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Microsoft.NodejsTools.Options
{
    [ComVisible(true)]
    public class NodejsGeneralOptionsPage : NodejsDialogPage
    {
        private const string WaitOnAbnormalExitSetting = "WaitOnAbnormalExit";
        private const string WaitOnNormalExitSetting = "WaitOnNormalExit";
        private const string EditAndContinueSetting = "EditAndContinue";
        private const string CheckForLongPathsSetting = "CheckForLongPaths";

        private NodejsGeneralOptionsControl _window;
        private bool _waitOnAbnormalExit;
        private bool _waitOnNormalExit;
        private bool _editAndContinue;
        private bool _waitOnAbnormalExitModified;
        private bool _waitOnNormalExitModified;
        private bool _editAndContinueModified;

        public NodejsGeneralOptionsPage()
            : base("General")
        {
        }

        // replace the default UI of the dialog page w/ our own UI.
        protected override IWin32Window Window
        {
            get
            {
                if (this._window == null)
                {
                    this._window = new NodejsGeneralOptionsControl();
                    LoadSettingsFromStorage();
                }
                return this._window;
            }
        }

        /// <summary>
        /// True if Node processes should pause for input before exiting
        /// if they exit abnormally.
        /// </summary>
        public bool WaitOnAbnormalExit
        {
            get => this._waitOnAbnormalExit;
            set
            {
                this._waitOnAbnormalExit = value;
                this._waitOnAbnormalExitModified = true;
            }
        }

        /// <summary>
        /// True if Node processes should pause for input before exiting
        /// if they exit normally.
        /// </summary>
        public bool WaitOnNormalExit
        {
            get => this._waitOnNormalExit;
            set
            {
                this._waitOnNormalExit = value;
                this._waitOnNormalExitModified = true;
            }
        }

        /// <summary>
        /// Indicates whether Edit and Continue feature should be enabled.
        /// </summary>
        public bool EditAndContinue
        {
            get => this._editAndContinue;
            set
            {
                this._editAndContinue = value;
                this._editAndContinueModified = true;
            }
        }

        /// <summary>
        /// Resets settings back to their defaults. This should be followed by
        /// a call to <see cref="SaveSettingsToStorage" /> to commit the new
        /// values.
        /// </summary>
        public override void ResetSettings()
        {
            this.WaitOnAbnormalExit = true;
            this.WaitOnNormalExit = false;
            this.EditAndContinue = true;
        }

        public override void LoadSettingsFromStorage()
        {
            this._waitOnAbnormalExit = LoadBool(WaitOnAbnormalExitSetting) ?? true;
            this._waitOnNormalExit = LoadBool(WaitOnNormalExitSetting) ?? false;
            this._editAndContinue = LoadBool(EditAndContinueSetting) ?? true;
            this._waitOnAbnormalExitModified = false;
            this._waitOnNormalExitModified = false;
            this._editAndContinueModified = false;

            if (this._window != null)
            {
                this._window.SyncControlWithPageSettings(this);
            }
        }

        internal void RefreshSettingsFromStorage()
        {
            if (!this._waitOnAbnormalExitModified)
            {
                this._waitOnAbnormalExit = LoadBool(WaitOnAbnormalExitSetting) ?? true;
            }

            if (!this._waitOnNormalExitModified)
            {
                this._waitOnNormalExit = LoadBool(WaitOnNormalExitSetting) ?? false;
            }

            if (!this._editAndContinueModified)
            {
                this._editAndContinue = LoadBool(EditAndContinueSetting) ?? true;
            }

            if (this._window != null)
            {
                this._window.SyncControlWithPageSettings(this);
            }
        }

        public override void SaveSettingsToStorage()
        {
            if (this._window != null)
            {
                this._window.SyncPageWithControlSettings(this);
            }

            SaveBool(WaitOnNormalExitSetting, this.WaitOnNormalExit);
            SaveBool(WaitOnAbnormalExitSetting, this.WaitOnAbnormalExit);
            SaveBool(EditAndContinueSetting, this.EditAndContinue);
            this._waitOnAbnormalExitModified = false;
            this._waitOnNormalExitModified = false;
            this._editAndContinueModified = false;
        }
    }
}
