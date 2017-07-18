// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.NodejsTools.Debugger.DebugEngine;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System;

namespace Microsoft.NodejsTools.Debugger.Remote
{
    internal class NodeRemoteDebugProgram : IDebugProgram2
    {
        private readonly NodeRemoteDebugProcess _process;
        private readonly Guid _guid = Guid.NewGuid();

        public NodeRemoteDebugProgram(NodeRemoteDebugProcess process)
        {
            this._process = process;
        }

        public NodeRemoteDebugProcess DebugProcess => this._process;
        public int Attach(IDebugEventCallback2 pCallback)
        {
            throw new NotImplementedException();
        }

        public int CanDetach()
        {
            throw new NotImplementedException();
        }

        public int CauseBreak()
        {
            throw new NotImplementedException();
        }

        public int Continue(IDebugThread2 pThread)
        {
            throw new NotImplementedException();
        }

        public int Detach()
        {
            throw new NotImplementedException();
        }

        public int EnumCodeContexts(IDebugDocumentPosition2 pDocPos, out IEnumDebugCodeContexts2 ppEnum)
        {
            throw new NotImplementedException();
        }

        public int EnumCodePaths(string pszHint, IDebugCodeContext2 pStart, IDebugStackFrame2 pFrame, int fSource, out IEnumCodePaths2 ppEnum, out IDebugCodeContext2 ppSafety)
        {
            throw new NotImplementedException();
        }

        public int EnumModules(out IEnumDebugModules2 ppEnum)
        {
            throw new NotImplementedException();
        }

        public int EnumThreads(out IEnumDebugThreads2 ppEnum)
        {
            throw new NotImplementedException();
        }

        public int Execute()
        {
            throw new NotImplementedException();
        }

        public int GetDebugProperty(out IDebugProperty2 ppProperty)
        {
            throw new NotImplementedException();
        }

        public int GetDisassemblyStream(enum_DISASSEMBLY_STREAM_SCOPE dwScope, IDebugCodeContext2 pCodeContext, out IDebugDisassemblyStream2 ppDisassemblyStream)
        {
            throw new NotImplementedException();
        }

        public int GetENCUpdate(out object ppUpdate)
        {
            throw new NotImplementedException();
        }

        public int GetEngineInfo(out string pbstrEngine, out Guid pguidEngine)
        {
            pguidEngine = AD7Engine.DebugEngineGuid;
            pbstrEngine = null;
            return VSConstants.S_OK;
        }

        public int GetMemoryBytes(out IDebugMemoryBytes2 ppMemoryBytes)
        {
            throw new NotImplementedException();
        }

        public int GetName(out string pbstrName)
        {
            pbstrName = null;
            return VSConstants.S_OK;
        }

        public int GetProcess(out IDebugProcess2 ppProcess)
        {
            ppProcess = this._process;
            return VSConstants.S_OK;
        }

        public int GetProgramId(out Guid pguidProgramId)
        {
            pguidProgramId = this._guid;
            return VSConstants.S_OK;
        }

        public int Step(IDebugThread2 pThread, enum_STEPKIND sk, enum_STEPUNIT Step)
        {
            throw new NotImplementedException();
        }

        public int Terminate()
        {
            throw new NotImplementedException();
        }

        public int WriteDump(enum_DUMPTYPE DUMPTYPE, string pszDumpUrl)
        {
            throw new NotImplementedException();
        }
    }
}
