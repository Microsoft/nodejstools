// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.VisualStudioTools
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal class ProvideDebugEngineAttribute : RegistrationAttribute
    {
        private readonly string _id, _name;
        private readonly bool _setNextStatement, _hitCountBp, _justMyCodeStepping;
        private readonly Type _programProvider, _debugEngine;

        public ProvideDebugEngineAttribute(string name, Type programProvider, Type debugEngine, string id, bool setNextStatement = true, bool hitCountBp = false, bool justMyCodeStepping = true)
        {
            this._name = name;
            this._programProvider = programProvider;
            this._debugEngine = debugEngine;
            this._id = id;
            this._setNextStatement = setNextStatement;
            this._hitCountBp = hitCountBp;
            this._justMyCodeStepping = justMyCodeStepping;
        }

        public override void Register(RegistrationContext context)
        {
            var engineKey = context.CreateKey("AD7Metrics\\Engine\\" + this._id);
            engineKey.SetValue("Name", this._name);

            engineKey.SetValue("CLSID", this._debugEngine.GUID.ToString("B"));
            engineKey.SetValue("ProgramProvider", this._programProvider.GUID.ToString("B"));
            engineKey.SetValue("PortSupplier", "{708C1ECA-FF48-11D2-904F-00C04FA302A1}"); // {708C1ECA-FF48-11D2-904F-00C04FA302A1}

            engineKey.SetValue("Attach", 1);
            engineKey.SetValue("AddressBP", 0);
            engineKey.SetValue("AutoSelectPriority", 6);
            engineKey.SetValue("CallstackBP", 1);
            engineKey.SetValue("ConditionalBP", 1);
            engineKey.SetValue("Exceptions", 1);
            engineKey.SetValue("SetNextStatement", this._setNextStatement ? 1 : 0);
            engineKey.SetValue("RemoteDebugging", 1);
            engineKey.SetValue("HitCountBP", this._hitCountBp ? 1 : 0);
            engineKey.SetValue("JustMyCodeStepping", this._justMyCodeStepping ? 1 : 0);
            //engineKey.SetValue("FunctionBP", 1); // TODO: Implement PythonLanguageInfo.ResolveName

            // provide class / assembly so we can be created remotely from the GAC w/o registering a CLSID 
            engineKey.SetValue("EngineClass", this._debugEngine.FullName);
            engineKey.SetValue("EngineAssembly", this._debugEngine.Assembly.FullName);

            // load locally so we don't need to create MSVSMon which would need to know how to
            // get at our provider type.  See AD7ProgramProvider.GetProviderProcessData for more info
            engineKey.SetValue("LoadProgramProviderUnderWOW64", 1);
            engineKey.SetValue("AlwaysLoadProgramProviderLocal", 1);
            engineKey.SetValue("LoadUnderWOW64", 1);

            using (var incompatKey = engineKey.CreateSubkey("IncompatibleList"))
            {
                // In VS 2013, mixed-mode debugging is supported with any engine that does not exclude us specifically
                // (everyone should be using the new debugging APIs that permit arbitrary mixing), except for the legacy
                // .NET 2.0/3.0/3.5 engine.
                //
                // In VS 2012, only native/Python mixing is supported - other stock engines are not updated yet, and
                // in particular throwing managed into the mix will cause the old native engine to be used.
                //
                // In VS 2010, mixed-mode debugging is not supported at all.
                incompatKey.SetValue("guidCOMPlusOnlyEng2", "{5FFF7536-0C87-462D-8FD2-7971D948E6DC}");
            }

            using (var autoSelectIncompatKey = engineKey.CreateSubkey("AutoSelectIncompatibleList"))
            {
                autoSelectIncompatKey.SetValue("guidNativeOnlyEng", "{3B476D35-A401-11D2-AAD4-00C04F990171}");
            }

            var clsidKey = context.CreateKey("CLSID");
            var clsidGuidKey = clsidKey.CreateSubkey(this._debugEngine.GUID.ToString("B"));
            clsidGuidKey.SetValue("Assembly", this._debugEngine.Assembly.FullName);
            clsidGuidKey.SetValue("Class", this._debugEngine.FullName);
            clsidGuidKey.SetValue("InprocServer32", context.InprocServerPath);
            clsidGuidKey.SetValue("CodeBase", Path.Combine(context.ComponentPath, this._debugEngine.Module.Name));
            clsidGuidKey.SetValue("ThreadingModel", "Free");

            clsidGuidKey = clsidKey.CreateSubkey(this._programProvider.GUID.ToString("B"));
            clsidGuidKey.SetValue("Assembly", this._programProvider.Assembly.FullName);
            clsidGuidKey.SetValue("Class", this._programProvider.FullName);
            clsidGuidKey.SetValue("InprocServer32", context.InprocServerPath);
            clsidGuidKey.SetValue("CodeBase", Path.Combine(context.ComponentPath, this._debugEngine.Module.Name));
            clsidGuidKey.SetValue("ThreadingModel", "Free");

            using (var exceptionAssistantKey = context.CreateKey("ExceptionAssistant\\KnownEngines\\" + this._id))
            {
                exceptionAssistantKey.SetValue("", this._name);
            }
        }

        public override void Unregister(RegistrationContext context)
        {
        }
    }
}
