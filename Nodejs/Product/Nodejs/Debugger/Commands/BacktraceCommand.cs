// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.NodejsTools.Debugger.Serialization;
using Newtonsoft.Json.Linq;

namespace Microsoft.NodejsTools.Debugger.Commands
{
    internal sealed class BacktraceCommand : DebuggerCommand
    {
        private readonly Dictionary<string, object> _arguments;
        private readonly IEvaluationResultFactory _resultFactory;
        private readonly bool _depthOnly;
        private readonly NodeModule _unknownModule = new NodeModule(-1, NodeVariableType.UnknownModule);

        public BacktraceCommand(int id, IEvaluationResultFactory resultFactory, int fromFrame, int toFrame, bool depthOnly = false)
            : base(id, "backtrace")
        {
            this._resultFactory = resultFactory;
            this._depthOnly = depthOnly;

            this._arguments = new Dictionary<string, object> {
                { "fromFrame", fromFrame },
                { "toFrame", toFrame },
                { "inlineRefs", true }
            };
        }

        protected override IDictionary<string, object> Arguments => this._arguments;
        public int CallstackDepth { get; private set; }
        public List<NodeStackFrame> StackFrames { get; private set; }
        public Dictionary<int, NodeModule> Modules { get; private set; }

        public override void ProcessResponse(JObject response)
        {
            base.ProcessResponse(response);

            var body = response["body"];
            this.CallstackDepth = (int)body["totalFrames"];

            // Collect frames only if required
            if (this._depthOnly)
            {
                this.Modules = new Dictionary<int, NodeModule>();
                this.StackFrames = new List<NodeStackFrame>();
                return;
            }

            // Extract scripts (if not provided)
            this.Modules = GetModules((JArray)response["refs"]);

            // Extract frames
            var frames = (JArray)body["frames"] ?? new JArray();
            this.StackFrames = new List<NodeStackFrame>(frames.Count);

            foreach (var frame in frames)
            {
                // Create stack frame
                var functionName = GetFunctionName(frame);
                var moduleId = (int?)frame["func"]["scriptId"];

                NodeModule module;
                if (!moduleId.HasValue || !this.Modules.TryGetValue(moduleId.Value, out module))
                {
                    module = this._unknownModule;
                }

                var line = (int?)frame["line"] ?? 0;
                var column = (int?)frame["column"] ?? 0;
                var frameId = (int?)frame["index"] ?? 0;

                var stackFrame = new NodeStackFrame(frameId)
                {
                    Column = column,
                    FunctionName = functionName,
                    Line = line,
                    Module = module
                };

                // Locals
                var variables = (JArray)frame["locals"] ?? new JArray();
                stackFrame.Locals = GetVariables(stackFrame, variables);

                // Arguments
                variables = (JArray)frame["arguments"] ?? new JArray();
                stackFrame.Parameters = GetVariables(stackFrame, variables);

                this.StackFrames.Add(stackFrame);
            }
        }

        private List<NodeEvaluationResult> GetVariables(NodeStackFrame stackFrame, IEnumerable<JToken> variables)
        {
            return variables.Select(t => new NodeBacktraceVariable(stackFrame, t))
                .Select(variableProvider => this._resultFactory.Create(variableProvider)).ToList();
        }

        private static string GetFunctionName(JToken frame)
        {
            var func = frame["func"];
            var framename = (string)func["name"];
            if (string.IsNullOrEmpty(framename))
            {
                framename = (string)func["inferredName"];
            }
            if (string.IsNullOrEmpty(framename))
            {
                framename = NodeVariableType.AnonymousFunction;
            }
            return framename;
        }

        private static Dictionary<int, NodeModule> GetModules(JArray references)
        {
            var scripts = new Dictionary<int, NodeModule>(references.Count);
            foreach (var reference in references)
            {
                var scriptId = (int)reference["id"];
                var fileName = (string)reference["name"];

                scripts.Add(scriptId, new NodeModule(scriptId, fileName));
            }
            return scripts;
        }
    }
}
