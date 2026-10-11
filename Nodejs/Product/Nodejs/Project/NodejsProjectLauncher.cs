// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web;
using System.Windows.Forms;
using Microsoft.NodejsTools.Npm.SPI;
using Microsoft.NodejsTools.Options;
using Microsoft.NodejsTools.SharedProject;
using Microsoft.NodejsTools.Telemetry;
using Microsoft.NodejsTools.TypeScript;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudioTools.Project;
using Microsoft.Internal.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Microsoft.NodejsTools.Debugger;

namespace Microsoft.NodejsTools.Project
{
    internal class NodejsProjectLauncher : IProjectLauncher
    {
        private readonly NodejsProjectNode _project;
        private int? _testServerPort;

        internal static readonly Guid WebKitDebuggerV2Guid = Guid.Parse("30d423cc-6d0b-4713-b92d-6b2a374c3d89");
        internal static readonly Guid JsCdpDebuggerV3Guid = Guid.Parse("394120B6-2FF9-4D0D-8953-913EF5CD0BCD");

        public NodejsProjectLauncher(NodejsProjectNode project)
        {
            this._project = project;

            var portNumber = this._project.GetProjectProperty(NodeProjectProperty.NodejsPort);
            if (int.TryParse(portNumber, out var portNum))
            {
                this._testServerPort = portNum;
            }
        }

        #region IProjectLauncher Members
        public int LaunchProject(bool debug)
        {
            return LaunchFile(ResolveStartupFile(), debug);
        }

        public int LaunchFile(string file, bool debug)
        {
            var nodePath = GetNodePath();

            if (this._project.IsInstallingMissingModules)
            {
                Nodejs.ShowNpmIsInstalling();
                this._project.NpmOutputPane?.Show();

                return VSConstants.S_OK;
            }

            if (nodePath == null)
            {
                Nodejs.ShowNodejsNotInstalled();
                return VSConstants.S_OK;
            }

            var nodeVersion = Nodejs.GetNodeVersion(nodePath);
            var shouldStartBrowser = ShouldStartBrowser();
            var browserPath = this.GetBrowserPath();

            // The call to Version.ToString() is safe, since changes to the ToString method are very unlikely, as the current output is widely documented.
            if (debug)
            {
                if (nodeVersion >= new Version(8, 0))
                {
                    StartWithChromeDebugger(file, nodePath, shouldStartBrowser, browserPath);
                    TelemetryHelper.LogDebuggingStarted("ChromeV2", nodeVersion.ToString());
                }
                else
                {
                    var output = OutputWindowRedirector.GetGeneral(NodejsPackage.Instance);
                    output.WriteErrorLine(string.Format(CultureInfo.CurrentCulture, Resources.ErrorDebuggingNode7NotSupported));
                    output.ShowAndActivate();
                }
            }
            else
            {
                StartNodeProcess(file, nodePath, shouldStartBrowser, browserPath);
                TelemetryHelper.LogDebuggingStarted("None", nodeVersion.ToString());
            }

            return VSConstants.S_OK;
        }

        // todo: move usersettings to separate class, so we can use this from other places.

        internal static bool CheckEnableDiagnosticLoggingOption()
        {
            var optionString = NodejsDialogPage.LoadString(name: "DiagnosticLogging", cat: "Debugging");

            return !StringComparer.OrdinalIgnoreCase.Equals(optionString, "false");
        }

        private void StartNodeProcess(string file, string nodePath, bool shouldStartBrowser, string browserPath)
        {
            //TODO: looks like this duplicates a bunch of code in NodeDebugger
            var psi = new ProcessStartInfo()
            {
                UseShellExecute = false,

                FileName = nodePath,
                Arguments = GetFullArguments(file, includeNodeArgs: true),
                WorkingDirectory = _project.GetWorkingDirectory()
            };

            var webBrowserUrl = GetFullUrl();
            Uri uri = null;
            if (!String.IsNullOrWhiteSpace(webBrowserUrl))
            {
                uri = new Uri(webBrowserUrl);
                psi.EnvironmentVariables["PORT"] = uri.Port.ToString();
            }

            foreach (var nameValue in GetEnvironmentVariables())
            {
                psi.EnvironmentVariables[nameValue.Key] = nameValue.Value;
            }

            var generalOptions = NodejsPackage.Instance.GeneralOptionsPage;
            generalOptions.RefreshSettingsFromStorage();

            var process = NodeProcess.Start(
                psi,
                waitOnAbnormal: generalOptions.WaitOnAbnormalExit,
                waitOnNormal: generalOptions.WaitOnNormalExit);

            this._project.OnDispose += process.ResponseToTerminateEvent;

            if (shouldStartBrowser && uri != null)
            {
                OnPortOpenedHandler.CreateHandler(
                    uri.Port,
                    shortCircuitPredicate: () => process.HasExited,
                    action: () => OpenBrowser(browserPath, webBrowserUrl)
                );
            }
        }

        private string GetFullArguments(string file, bool includeNodeArgs)
        {
            var res = string.Empty;
            if (includeNodeArgs)
            {
                var nodeArgs = this._project.GetProjectProperty(NodeProjectProperty.NodeExeArguments);
                if (!string.IsNullOrWhiteSpace(nodeArgs))
                {
                    res = nodeArgs + " ";
                }
            }

            res += "\"" + file + "\"";
            var scriptArgs = this._project.GetProjectProperty(NodeProjectProperty.ScriptArguments);
            if (!string.IsNullOrWhiteSpace(scriptArgs))
            {
                res += " " + scriptArgs;
            }
            return res;
        }

        private string GetNodePath()
        {
            var overridePath = this._project.GetProjectProperty(NodeProjectProperty.NodeExePath);
            return Nodejs.GetAbsoluteNodeExePath(this._project.ProjectHome, overridePath);
        }

        #endregion

        private string GetFullUrl()
        {
            var host = this._project.GetProjectProperty(NodeProjectProperty.LaunchUrl);

            try
            {
                return GetFullUrl(host, this.TestServerPort);
            }
            catch (UriFormatException)
            {
                var output = OutputWindowRedirector.GetGeneral(NodejsPackage.Instance);
                output.WriteErrorLine(string.Format(CultureInfo.CurrentCulture, Resources.ErrorInvalidLaunchUrl, host));
                output.ShowAndActivate();
                return string.Empty;
            }
        }

        internal static string GetFullUrl(string host, int port)
        {
            UriBuilder builder;
            if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
            {
                builder = new UriBuilder(uri);
            }
            else
            {
                builder = new UriBuilder();
                builder.Scheme = Uri.UriSchemeHttp;
                builder.Host = "localhost";
                builder.Path = host;
            }

            builder.Port = port;

            return builder.ToString();
        }

        internal static Guid GetDebuggerGuid()
        {
            return ShouldUseV3CdpDebugger() ? JsCdpDebuggerV3Guid : WebKitDebuggerV2Guid;
        }

        internal static bool ShouldUseV3CdpDebugger()
        {
            var userRegistryRoot = VSRegistry.RegistryRoot(__VsLocalRegistryType.RegType_UserSettings, writable: false);
            try
            {
                object userDebuggerOption = userRegistryRoot.OpenSubKey("Debugger")?.GetValue("EnableJavaScriptMultitargetNodeDebug");
                if (userDebuggerOption is int optionVal)
                {
                    return optionVal != 0;
                }
            }
            catch (Exception) { } // do nothing

            return true;
        }

        private int TestServerPort
        {
            get
            {
                if (!this._testServerPort.HasValue)
                {
                    this._testServerPort = GetFreePort();
                }
                return this._testServerPort.Value;
            }
        }

        private void StartWithChromeDebugger(string file, string nodePath, bool shouldStartBrowser, string browserPath)
        {
            var serviceProvider = _project.Site;

            // Here we need to massage the env variables into the format expected by node and vs code
            var webBrowserUrl = GetFullUrl();
            var envVars = GetEnvironmentVariables(webBrowserUrl);
            var debuggerPort = this._project.GetProjectProperty(NodeProjectProperty.DebuggerPort);
            if (string.IsNullOrWhiteSpace(debuggerPort))
            {
                debuggerPort = NodejsConstants.DefaultDebuggerPort.ToString();
            }

            var runtimeArguments = ConvertArguments(this._project.GetProjectProperty(NodeProjectProperty.NodeExeArguments));
            // If we supply the port argument we also need to manually add --inspect-brk=port to the runtime arguments
            runtimeArguments = runtimeArguments.Append($"--inspect-brk={debuggerPort}");
            var scriptArguments = ConvertArguments(this._project.GetProjectProperty(NodeProjectProperty.ScriptArguments));

            var cwd = _project.GetWorkingDirectory(); // Current working directory

            var config = new NodePinezorroDebugLaunchConfig(file,
                                                            scriptArguments,
                                                            nodePath,
                                                            runtimeArguments,
                                                            debuggerPort,
                                                            cwd,
                                                            CheckEnableDiagnosticLoggingOption(),
                                                            _project.ProjectGuid.ToString(),
                                                            JObject.FromObject(envVars));

            var jsonContent = JObject.FromObject(config).ToString();

            var debugTargets = new[] {
                new VsDebugTargetInfo4() {
                    dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
                    guidLaunchDebugEngine = GetDebuggerGuid(),
                    bstrExe = file,
                    bstrOptions = jsonContent
                }
            };

            var processInfo = new VsDebugTargetProcessInfo[debugTargets.Length];

            var debugger = (IVsDebugger4)serviceProvider.GetService(typeof(SVsShellDebugger));
            debugger.LaunchDebugTargets4((uint)debugTargets.Length, debugTargets, processInfo);

            // Launch browser 
            if (shouldStartBrowser && !string.IsNullOrWhiteSpace(webBrowserUrl))
            {
                var uri = new Uri(webBrowserUrl);
                OnPortOpenedHandler.CreateHandler(
                    uri.Port,
                    timeout: 5_000, // 5 seconds
                    action: () => OpenBrowser(browserPath, webBrowserUrl)
                );
            }
        }

        private static string[] ConvertArguments(string argumentString)
        {
            if (argumentString != null)
            {
                return argumentString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }
            return Array.Empty<string>();
        }

        private void LaunchDebugger(IServiceProvider provider, VsDebugTargetInfo dbgInfo)
        {
            if (!Directory.Exists(dbgInfo.bstrCurDir))
            {
                MessageBox.Show(string.Format(CultureInfo.CurrentCulture, Resources.DebugWorkingDirectoryDoesNotExistErrorMessage, dbgInfo.bstrCurDir), SR.ProductName);
            }
            else if (!File.Exists(dbgInfo.bstrExe))
            {
                MessageBox.Show(string.Format(CultureInfo.CurrentCulture, Resources.DebugInterpreterDoesNotExistErrorMessage, dbgInfo.bstrExe), SR.ProductName);
            }
            else if (DoesProjectSupportDebugging())
            {
                VsShellUtilities.LaunchDebugger(provider, dbgInfo);
            }
        }

        private bool DoesProjectSupportDebugging()
        {
            var typeScriptOutFile = this._project.GetProjectProperty("TypeScriptOutFile");
            if (!string.IsNullOrEmpty(typeScriptOutFile))
            {
                return MessageBox.Show(
                    Resources.DebugTypeScriptCombineNotSupportedWarningMessage,
                    SR.ProductName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            }

            return true;
        }

        private static void AppendOption(ref VsDebugTargetInfo dbgInfo, string option, string value)
        {
            if (!string.IsNullOrWhiteSpace(dbgInfo.bstrOptions))
            {
                dbgInfo.bstrOptions += ";";
            }

            dbgInfo.bstrOptions += option + "=" + HttpUtility.UrlEncode(value);
        }

        private string GetEnvironmentVariablesString(string url)
        {
            var env = GetEnvironmentVariables(url);
            if (env.Count > 0)
            {
                //Environment variables should be passed as a
                //null-terminated block of null-terminated strings. 
                //Each string is in the following form:name=value\0
                var buf = new StringBuilder();
                foreach (var entry in env)
                {
                    buf.AppendFormat("{0}={1}\0", entry.Key, entry.Value);
                }
                buf.Append("\0");
                return buf.ToString();
            }

            return null;
        }

        private Dictionary<string, string> GetEnvironmentVariables(string url)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(url))
            {
                var webUrl = new Uri(url);
                env["PORT"] = webUrl.Port.ToString();
            }

            foreach (var nameValue in GetEnvironmentVariables())
            {
                env[nameValue.Key] = nameValue.Value;
            }

            if (env.Count > 0)
            {
                // add any inherited env vars
                var variables = Environment.GetEnvironmentVariables();
                foreach (var key in variables.Keys)
                {
                    var strKey = (string)key;
                    if (!env.ContainsKey(strKey))
                    {
                        env.Add(strKey, (string)variables[key]);
                    }
                }
            }

            return env;
        }

        private bool ShouldStartBrowser()
        {
            var startBrowser = this._project.GetProjectProperty(NodeProjectProperty.StartWebBrowser);
            if (!string.IsNullOrEmpty(startBrowser) &&
                bool.TryParse(startBrowser, out var fStartBrowser))
            {
                return fStartBrowser;
            }

            return true;
        }

        private string GetBrowserPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var uiShellOpenDocument = (IVsUIShellOpenDocument)ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShellOpenDocument));
            uiShellOpenDocument.GetFirstDefaultPreviewer(out var defaultBrowserPath, out _, out _);

            return defaultBrowserPath;
        }

        private static void OpenBrowser(string browserPath, string webBrowserUrl)
        {
            // Chrome has known issues with being launched as admin.
            if (!string.IsNullOrEmpty(browserPath) && browserPath.EndsWith(NodejsConstants.ChromeApplicationName))
            {
                SystemUtility.ExecuteProcessUnElevated(browserPath, webBrowserUrl);
            }
            else
            {
                VsShellUtilities.OpenBrowser(webBrowserUrl, (uint)__VSOSPFLAGS.OSP_LaunchNewBrowser);
            }
        }

        private IEnumerable<KeyValuePair<string, string>> GetEnvironmentVariables()
        {
            var envVars = this._project.GetProjectProperty(NodeProjectProperty.Environment);
            if (envVars != null)
            {
                foreach (var envVar in envVars.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var nameValue = envVar.Split(new[] { '=' }, 2);
                    if (nameValue.Length == 2)
                    {
                        yield return new KeyValuePair<string, string>(nameValue[0], nameValue[1]);
                    }
                }
            }
        }

        private static int GetFreePort()
        {
            return Enumerable.Range(new Random().Next(1200, 2000), 60000).Except(
                from connection in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                select connection.LocalEndPoint.Port
            ).First();
        }

        private string ResolveStartupFile()
        {
            var startupFile = this._project.GetStartupFile();
            if (string.IsNullOrEmpty(startupFile))
            {
                throw new ApplicationException(Resources.DebugCouldNotResolveStartupFileErrorMessage);
            }

            if (TypeScriptHelpers.IsTypeScriptFile(startupFile))
            {
                startupFile = TypeScriptHelpers.GetTypeScriptBackedJavaScriptFile(this._project, startupFile);
                if (startupFile == null)
                {
                    // Expected to find a JS file
                    throw new ArgumentException();
                }
            }
            return startupFile;
        }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    internal class NodePinezorroDebugLaunchConfig
    {
        public NodePinezorroDebugLaunchConfig() { }

        public NodePinezorroDebugLaunchConfig (string program, 
                                               string[] programArgs, 
                                               string runtimeExecutable, 
                                               string[] runtimeArgs, 
                                               string port, 
                                               string currWorkingDir,
                                               bool trace,
                                               string projectGuid,
                                               object environmentSettings = null)
        {
            this.ConfigName = "Debug Node.js program from Visual Studio";
            this.Request = "launch";
            this.DebugType = "node";
            this.Console = "externalTerminal";
            this.Program = program;
            this.ProgramArgs = programArgs;
            this.RuntimeExecutable = runtimeExecutable;
            this.RuntimeArgs = runtimeArgs;
            this.Port = port;
            this.WorkingDir = currWorkingDir;
            this.WebRoot = currWorkingDir;
            this.Environment = environmentSettings;
            this.Trace = trace;
            this.ProjectGuid = projectGuid;
        }

        [JsonProperty("name")]
        public string ConfigName { get; set; } 

        [JsonProperty("type")]
        public string DebugType { get; set; }

        [JsonProperty("request")]
        public string Request { get; set; } 

        [JsonProperty("program")]
        public string Program { get; set; }

        [JsonProperty("args")]
        public string[] ProgramArgs { get; set; }

        [JsonProperty("runtimeExecutable")]
        public string RuntimeExecutable { get; set; }

        [JsonProperty("runtimeArgs")]
        public string[] RuntimeArgs { get; set; }

        [JsonProperty("port")]
        public string Port { get; set; }

        [JsonProperty("cwd")]
        public string WorkingDir { get; set; }

        [JsonProperty("console")]
        public string Console { get; set; }

        [JsonProperty("env")]
        public object Environment { get; set; }

        [JsonProperty("trace")]
        public bool Trace { get; set; }

        [JsonProperty("sourceMaps")]
        public bool SourceMaps { get; set; } = true;

        [JsonProperty("stopOnEntry")]
        public bool StopOnEntry { get; set; } = true;

        [JsonProperty("url")]
        public string BrowserUrl { get; set; }

        [JsonProperty("server")]
        public object Server { get; set; } 

        [JsonProperty("sourceMapPathOverrides")]
        public Dictionary<string, string> SourceMapPathOverrides { get; set; }

        [JsonProperty("restart")]
        public bool RestartPolicy { get; set; }

        [JsonProperty("userDataDir")]
        public object BrowserUserDataDir { get; set; } = null;

        [JsonProperty("webRoot")]
        public string WebRoot { get; set; }

        [JsonProperty("projectGuid")]
        public string ProjectGuid { get; set; }

        public object toPwaChromeServerConfig()
        {
            this.Console = "internalConsole";
            this.SourceMapPathOverrides = new Dictionary<string, string>();
            return this;
        }
    }

    internal class OnPortOpenedHandler
    {
        private class OnPortOpenedInfo
        {
            public readonly int Port;
            public readonly TimeSpan? Timeout;
            public readonly int Sleep;
            public readonly Func<bool> ShortCircuitPredicate;
            public readonly Action Action;
            public readonly DateTime StartTime;

            public OnPortOpenedInfo(
                int port,
                int? timeout = null,
                int? sleep = null,
                Func<bool> shortCircuitPredicate = null,
                Action action = null
            )
            {
                this.Port = port;
                if (timeout.HasValue)
                {
                    this.Timeout = TimeSpan.FromMilliseconds(Convert.ToDouble(timeout));
                }
                this.Sleep = sleep ?? 500;                                   // 1/2 second sleep
                this.ShortCircuitPredicate = shortCircuitPredicate ?? (() => false);
                this.Action = action ?? (() => { });
                this.StartTime = System.DateTime.Now;
            }
        }

        internal static void CreateHandler(
            int port,
            int? timeout = null,
            int? sleep = null,
            Func<bool> shortCircuitPredicate = null,
            Action action = null
        )
        {
            ThreadPool.QueueUserWorkItem(
                OnPortOpened,
                new OnPortOpenedInfo(
                    port,
                    timeout,
                    sleep,
                    shortCircuitPredicate,
                    action
                )
            );
        }

        private static void OnPortOpened(object infoObj)
        {
            var info = (OnPortOpenedInfo)infoObj;

            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Blocking = true;
                try
                {
                    while (true)
                    {
                        // Short circuit
                        if (info.ShortCircuitPredicate())
                        {
                            return;
                        }

                        // Try connect
                        try
                        {
                            socket.Connect(IPAddress.Loopback, info.Port);
                            break;
                        }
                        catch
                        {
                            // Connect failure
                            // Fall through
                        }

                        // Timeout
                        if (info.Timeout.HasValue && (System.DateTime.Now - info.StartTime) >= info.Timeout)
                        {
                            break;
                        }

                        // Sleep
                        System.Threading.Thread.Sleep(info.Sleep);
                    }
                }
                finally
                {
                    socket.Close();
                }
            }

            // Launch browser (if not short-circuited)
            if (!info.ShortCircuitPredicate())
            {
                info.Action();
            }
        }
    }
}
