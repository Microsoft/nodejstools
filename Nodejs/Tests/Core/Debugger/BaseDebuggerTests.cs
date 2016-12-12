﻿//*********************************************************//
//    Copyright (c) Microsoft. All rights reserved.
//    
//    Apache 2.0 License
//    
//    You may obtain a copy of the License at
//    http://www.apache.org/licenses/LICENSE-2.0
//    
//    Unless required by applicable law or agreed to in writing, software 
//    distributed under the License is distributed on an "AS IS" BASIS, 
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or 
//    implied. See the License for the specific language governing 
//    permissions and limitations under the License.
//
//*********************************************************//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.NodejsTools;
using Microsoft.NodejsTools.Debugger;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudioTools;
using TestUtilities;

namespace NodejsTests.Debugger {
    internal enum NodeVersion {
        NodeVersion_Unknown,
    };

    internal static class Extensions {
        internal static async Task Remove(this NodeBreakpoint breakpoint) {
            foreach (var binding in breakpoint.GetBindings()) {
                await binding.Remove().ConfigureAwait(false);
            }
        }
    }

    public class BaseDebuggerTests {
        internal virtual string DebuggerTestPath {
            get {
                return TestUtilities.TestData.GetPath(@"TestData\DebuggerProject\");
            }
        }

        internal static NodeBreakpoint AddBreakPoint(
            NodeDebugger newproc,
            string fileName,
            int line,
            int column,
            bool enabled = true,
            BreakOn breakOn = new BreakOn(),
            string condition = ""
        ) {
            NodeBreakpoint breakPoint = newproc.AddBreakpoint(fileName, line, column, enabled, breakOn, condition);
            breakPoint.BindAsync().WaitAndUnwrapExceptions();
            return breakPoint;
        }

        internal NodeDebugger DebugProcess(
            string filename,
            Action<NodeDebugger> onProcessCreated = null,
            Action<NodeDebugger, NodeThread> onLoadComplete = null,
            string interpreterOptions = null,
            NodeDebugOptions debugOptions = NodeDebugOptions.None,
            string cwd = null,
            string arguments = "",
            bool resumeOnProcessLoad = true
        ) {
            if (!Path.IsPathRooted(filename)) {
                filename = DebuggerTestPath + filename;
            }
            string fullPath = Path.GetFullPath(filename);
            string dir = cwd ?? Path.GetFullPath(Path.GetDirectoryName(filename));
            if (!String.IsNullOrEmpty(arguments)) {
                arguments = "\"" + fullPath + "\" " + arguments;
            } else {
                arguments = "\"" + fullPath + "\"";
            }

            // Load process
            AutoResetEvent processLoaded = new AutoResetEvent(false);
            Assert.IsNotNull(Nodejs.NodeExePath, "Node isn't installed");
            NodeDebugger process =
                new NodeDebugger(
                    Nodejs.NodeExePath,
                    arguments,
                    dir,
                    null,
                    interpreterOptions,
                    debugOptions,
                    null,
                    createNodeWindow: false);
            if (onProcessCreated != null) {
                onProcessCreated(process);
            }
            process.ProcessLoaded += (sender, args) => {
                // Invoke onLoadComplete delegate, if requested
                if (onLoadComplete != null) {
                    onLoadComplete(process, args.Thread);
                }
                processLoaded.Set();
            };
            process.Start();
            AssertWaited(processLoaded);

            // Resume, if requested
            if (resumeOnProcessLoad) {
                process.Resume();
            }

            return process;
        }

        internal Process StartNodeProcess(
            string filename,
            string interpreterOptions = null,
            string cwd = null,
            string arguments = "",
            bool startBrokenAtEntryPoint = false
        ) {
            if (!Path.IsPathRooted(filename)) {
                filename = DebuggerTestPath + filename;
            }
            string fullPath = Path.GetFullPath(filename);
            string dir = cwd ?? Path.GetFullPath(Path.GetDirectoryName(filename));
            arguments =
                (startBrokenAtEntryPoint ? "--debug-brk" : "--debug") +
                " \"" + fullPath + "\"" +
                (String.IsNullOrEmpty(arguments) ? "" : " " + arguments);

            Assert.IsNotNull(Nodejs.NodeExePath, "Node isn't installed");
            var psi = new ProcessStartInfo(Nodejs.NodeExePath, arguments);
            psi.WorkingDirectory = dir;
            var process = new Process();
            process.StartInfo = psi;
            process.EnableRaisingEvents = true;
            process.Start();

            return process;
        }

        internal NodeDebugger AttachToNodeProcess(
            Action<NodeDebugger> onProcessCreated = null,
            Action<NodeDebugger, NodeThread> onLoadComplete = null,
            string hostName = "localhost",
            ushort portNumber = 5858,
            int id = 0,
            bool resumeOnProcessLoad = false) {
            // Load process
            AutoResetEvent processLoaded = new AutoResetEvent(false);
            var process = new NodeDebugger(new UriBuilder { Scheme = "tcp", Host = hostName, Port = portNumber }.Uri, id);
            if (onProcessCreated != null) {
                onProcessCreated(process);
            }
            process.ProcessLoaded += (sender, args) => {
                // Invoke onLoadComplete delegate, if requested
                if (onLoadComplete != null) {
                    onLoadComplete(process, args.Thread);
                }
                processLoaded.Set();
            };
            process.StartListening();
            AssertWaited(processLoaded);

            // Resume, if requested
            if (resumeOnProcessLoad) {
                process.Resume();
            }

            return process;
        }

        internal virtual NodeVersion Version {
            get {
                return NodeVersion.NodeVersion_Unknown;
            }
        }

        internal void LocalsTest(
            string filename,
            int breakpoint,
            int frameIndex = 0,
            string[] expectedParams = null,
            string[] expectedLocals = null,
            string[] expectedValues = null,
            string[] expectedHexValues = null
        ) {
            TestDebuggerSteps(
                filename,
                new[] {
                    new TestStep(action: TestAction.AddBreakpoint, targetBreakpoint: breakpoint),
                    new TestStep(action: TestAction.ResumeProcess, expectedBreakpointHit: breakpoint),
                    new TestStep(validation: (process, thread) => {
                        var frame = thread.Frames[frameIndex];
                        AssertUtil.ContainsExactly(
                            new HashSet<string>(expectedParams ?? new string[] { }),
                            frame.Parameters.Select(x => x.Expression)
                        );
                        AssertUtil.ContainsExactly(
                            new HashSet<string>(expectedLocals ?? new string[] { }),
                            frame.Locals.Select(x => x.Expression)
                        );

                        if (expectedValues != null || expectedHexValues != null) {
                            foreach (var evaluationResult in frame.Parameters.Concat(frame.Locals)) {
                                int i = 0;
                                var match = -1;
                                if (expectedParams != null) {
                                    foreach (var expectedParam in expectedParams) {
                                        if (evaluationResult.Expression == expectedParam) {
                                            match = i;
                                            break;
                                        }
                                        ++i;
                                    }
                                }
                                if (match == -1 && expectedLocals != null) {
                                    foreach (var expectedLocal in expectedLocals) {
                                        if (evaluationResult.Expression == expectedLocal) {
                                            match = i;
                                            break;
                                        }
                                        ++i;
                                    }
                                }
                                Assert.IsTrue(match > -1);
                                if (expectedValues != null) {
                                    Assert.AreEqual(expectedValues[match], evaluationResult.StringValue);
                                }
                                if (expectedHexValues != null) {
                                    Assert.AreEqual(expectedHexValues[match], evaluationResult.HexValue);
                                }
                            }
                        }
                    }),
                    new TestStep(action: TestAction.ResumeProcess, expectedExitCode: 0),
                }
            );
        }

        internal void ExecTest(
            NodeThread thread,
            int frameIndex = 0,
            string expression = null,
            string expectedType = null,
            string expectedValue = null,
            string expectedException = null,
            string expectedFrame = null
        ) {
            var frame = thread.Frames[frameIndex];
            NodeEvaluationResult evaluationResult = null;
            Exception exception = null;

            try {
                evaluationResult = frame.ExecuteTextAsync(expression).WaitAndUnwrapExceptions();
            } catch (Exception ex) {
                exception = ex;
            }

            if (expectedType != null) {
                Assert.IsNotNull(evaluationResult);
                Assert.AreEqual(expectedType, evaluationResult.TypeName);
            }
            if (expectedValue != null) {
                Assert.IsNotNull(evaluationResult);
                Assert.AreEqual(expectedValue, evaluationResult.StringValue);
            }
            if (expectedException != null) {
                Assert.IsNotNull(exception);
                Assert.AreEqual(expectedException, exception.Message);
            }
            if (expectedFrame != null) {
                Assert.AreEqual(expectedFrame, frame.FunctionName);
            }
        }

        internal class ExceptionHandlerInfo {
            public readonly int FirstLine;
            public readonly int LastLine;
            public readonly HashSet<string> Expressions;

            public ExceptionHandlerInfo(int firstLine, int lastLine, params string[] expressions) {
                FirstLine = firstLine;
                LastLine = lastLine;
                Expressions = new HashSet<string>(expressions);
            }
        }

        const ExceptionHitTreatment _breakNever = ExceptionHitTreatment.BreakNever;
        const ExceptionHitTreatment _breakAlways = ExceptionHitTreatment.BreakAlways;
        const ExceptionHitTreatment _breakOnUnhandled = ExceptionHitTreatment.BreakOnUnhandled;

        internal class ExceptionInfo {
            public readonly string TypeName;
            public readonly string Description;
            public readonly int? LineNo;

            public ExceptionInfo(string typeName, string description, int? lineNo = null) {
                TypeName = typeName;
                Description = description;
                LineNo = lineNo;
            }
        }

        internal ICollection<KeyValuePair<string, ExceptionHitTreatment>> CollectExceptionTreatments(ExceptionHitTreatment exceptionTreatment = ExceptionHitTreatment.BreakAlways, params string[] exceptionNames) {
            return exceptionNames.Select(
                name => new KeyValuePair<string, ExceptionHitTreatment>(name, exceptionTreatment)
            ).ToArray();
        }

        internal void TestExceptions(
            string filename,
            ExceptionHitTreatment? defaultExceptionTreatment,
            ICollection<KeyValuePair<string, ExceptionHitTreatment>> exceptionTreatments,
            int expectedExitCode,
            params ExceptionInfo[] exceptions
        ) {
            var steps = new List<TestStep>();
            foreach (var exception in exceptions) {
                steps.Add(new TestStep(action: TestAction.ResumeProcess, expectedExceptionRaised: exception));
            }
            steps.Add(new TestStep(action: TestAction.ResumeProcess, expectedExitCode: expectedExitCode));
            TestDebuggerSteps(
                filename,
                steps,
                defaultExceptionTreatment: defaultExceptionTreatment,
                exceptionTreatments: exceptionTreatments
                );
        }

        internal enum TestAction {
            None = 0,
            Wait,
            ResumeThread,
            ResumeProcess,
            StepOver,
            StepInto,
            StepOut,
            AddBreakpoint,
            RemoveBreakpoint,
            UpdateBreakpoint,
            KillProcess,
            Detach,
        }

        internal struct TestStep {
            public TestAction _action;
            public int? _expectedEntryPointHit;
            public int? _expectedBreakpointHit;
            public int? _expectedStepComplete;
            public string _expectedBreakFile, _expectedBreakFunction;
            public ExceptionInfo _expectedExceptionRaised;
            public string _targetBreakpointFile;
            public int? _targetBreakpoint;
            public int? _targetBreakpointColumn;
            public uint? _expectedHitCount;
            public uint? _hitCount;
            public bool? _enabled;
            public BreakOn? _breakOn;
            public string _condition;
            public bool _builtin;
            public bool _expectFailure;
            public bool _expectReBind;
            public Action<NodeDebugger, NodeThread> _validation;
            public int? _expectedExitCode;

            internal TestStep(
                TestAction action = TestAction.None,
                int? expectedEntryPointHit = null,
                int? expectedBreakpointHit = null,
                int? expectedStepComplete = null,
                string expectedBreakFile = null,
                ExceptionInfo expectedExceptionRaised = null,
                int? targetBreakpoint = null,
                int? targetBreakpointColumn = null,
                string targetBreakpointFile = null,
                uint? expectedHitCount = null,
                uint? hitCount = null,
                bool? enabled = null,
                BreakOn? breakOn = null,
                string condition = null,
                bool builtin = false,
                bool expectFailure = false,
                bool expectReBind = false,
                Action<NodeDebugger, NodeThread> validation = null,
                int? expectedExitCode = null,
                string expectedBreakFunction = null
            ) {
                _action = action;
                _expectedEntryPointHit = expectedEntryPointHit;
                _expectedBreakpointHit = expectedBreakpointHit;
                _expectedStepComplete = expectedStepComplete;
                _expectedBreakFile = expectedBreakFile;
                _expectedExceptionRaised = expectedExceptionRaised;
                _targetBreakpointFile = targetBreakpointFile;
                _targetBreakpoint = targetBreakpoint;
                _targetBreakpointColumn = targetBreakpointColumn;
                _expectedHitCount = expectedHitCount;
                _hitCount = hitCount;
                _enabled = enabled;
                _breakOn = breakOn;
                _condition = condition;
                _builtin = builtin;
                _expectFailure = expectFailure;
                _expectReBind = expectReBind;
                _validation = validation;
                _expectedExitCode = expectedExitCode;
                _expectedBreakFunction = expectedBreakFunction;
            }
        }

        internal void TestDebuggerSteps(
            string filename,
            IEnumerable<TestStep> steps,
            string interpreterOptions = null,
            Action<NodeDebugger> onProcessCreated = null,
            ExceptionHitTreatment? defaultExceptionTreatment = null,
            ICollection<KeyValuePair<string, ExceptionHitTreatment>> exceptionTreatments = null,
            string scriptArguments = null
        ) {
            if (!Path.IsPathRooted(filename)) {
                filename = DebuggerTestPath + filename;
            }

            NodeThread thread = null;
            using (var process = DebugProcess(
                filename,
                onProcessCreated: onProcessCreated,
                onLoadComplete: (newproc, newthread) => {
                    thread = newthread;
                },
                interpreterOptions: interpreterOptions,
                arguments: scriptArguments,
                resumeOnProcessLoad: false
            )) {
                TestDebuggerSteps(
                    process,
                    thread,
                    filename,
                    steps,
                    defaultExceptionTreatment,
                    exceptionTreatments,
                    waitForExit: true);
            }
        }

        internal struct Breakpoint {
            internal Breakpoint(string fileName, int line, int column) {
                _fileName = fileName;
                _line = line;
                _column = column;
            }
            public string _fileName;
            public int _line;
            public int _column;
        }

        internal void TestDebuggerSteps(
            NodeDebugger process,
            NodeThread thread,
            string filename,
            IEnumerable<TestStep> steps,
            ExceptionHitTreatment? defaultExceptionTreatment = null,
            ICollection<KeyValuePair<string, ExceptionHitTreatment>> exceptionTreatments = null,
            bool waitForExit = false
        ) {
            if (!Path.IsPathRooted(filename)) {
                filename = DebuggerTestPath + filename;
            }

            // Since Alpha does not support break on unhandled, and the commonly used Express module has handled SyntaxError exceptions,
            // for alpha we have SyntaxErrors set to BreakNever by default.  Here we set it to BreakAlways so unit tests can run
            // assuming BreakAlways is the default.            
            // TODO: Remove once exception treatment is updated for just my code support when it is added after Alpha
            process.SetExceptionTreatment(null, CollectExceptionTreatments(ExceptionHitTreatment.BreakAlways, "SyntaxError"));

            if (defaultExceptionTreatment != null || exceptionTreatments != null) {
                process.SetExceptionTreatment(defaultExceptionTreatment, exceptionTreatments);
            }

            Dictionary<Breakpoint, NodeBreakpoint> breakpoints = new Dictionary<Breakpoint, NodeBreakpoint>();

            AutoResetEvent entryPointHit = new AutoResetEvent(false);
            process.EntryPointHit += (sender, e) => {
                Console.WriteLine("EntryPointHit");
                Assert.AreEqual(e.Thread, thread);
                entryPointHit.Set();
            };

            AutoResetEvent breakpointBound = new AutoResetEvent(false);
            process.BreakpointBound += (sender, e) => {
                Console.WriteLine("BreakpointBound {0} {1}", e.BreakpointBinding.Position.FileName, e.BreakpointBinding.Position.Line);
                breakpointBound.Set();
            };

            AutoResetEvent breakpointUnbound = new AutoResetEvent(false);
            process.BreakpointUnbound += (sender, e) => {
                Console.WriteLine("BreakpointUnbound");
                breakpointUnbound.Set();
            };

            AutoResetEvent breakpointBindFailure = new AutoResetEvent(false);
            process.BreakpointBindFailure += (sender, e) => {
                Console.WriteLine("BreakpointBindFailure");
                breakpointBindFailure.Set();
            };

            AutoResetEvent breakpointHit = new AutoResetEvent(false);
            process.BreakpointHit += (sender, e) => {
                Console.WriteLine("BreakpointHit {0}", e.BreakpointBinding.Target.Line);
                Assert.AreEqual(e.Thread, thread);
                Assert.AreEqual(e.BreakpointBinding.Target.Line, thread.Frames.First().Line);
                breakpointHit.Set();
            };

            AutoResetEvent stepComplete = new AutoResetEvent(false);
            process.StepComplete += (sender, e) => {
                Console.WriteLine("StepComplete");
                Assert.AreEqual(e.Thread, thread);
                stepComplete.Set();
            };

            AutoResetEvent exceptionRaised = new AutoResetEvent(false);
            NodeException exception = null;
            process.ExceptionRaised += (sender, e) => {
                Console.WriteLine("ExceptionRaised");
                Assert.AreEqual(e.Thread, thread);
                exception = e.Exception;
                exceptionRaised.Set();
            };

            AutoResetEvent processExited = new AutoResetEvent(false);
            int exitCode = 0;
            process.ProcessExited += (sender, e) => {
                Console.WriteLine("ProcessExited {0}", e.ExitCode);
                exitCode = e.ExitCode;
                processExited.Set();
            };

            Console.WriteLine("-----------------------------------------");
            Console.WriteLine("Begin debugger step test");
            foreach (var step in steps) {
                Console.WriteLine("Step: {0}", step._action);
                Assert.IsFalse(
                    ((step._expectedEntryPointHit != null ? 1 : 0) +
                     (step._expectedBreakpointHit != null ? 1 : 0) +
                     (step._expectedStepComplete != null ? 1 : 0) +
                     (step._expectedExceptionRaised != null ? 1 : 0)) > 1);
                bool wait = false;
                NodeBreakpoint nodeBreakpoint;
                switch (step._action) {
                    case TestAction.None:
                        break;
                    case TestAction.Wait:
                        wait = true;
                        break;
                    case TestAction.ResumeThread:
                        thread.Resume();
                        wait = true;
                        break;
                    case TestAction.ResumeProcess:
                        process.Resume();
                        wait = true;
                        break;
                    case TestAction.StepOver:
                        thread.StepOver();
                        wait = true;
                        break;
                    case TestAction.StepInto:
                        thread.StepInto();
                        wait = true;
                        break;
                    case TestAction.StepOut:
                        thread.StepOut();
                        wait = true;
                        break;
                    case TestAction.AddBreakpoint:
                        string breakpointFileName = step._targetBreakpointFile;
                        if (breakpointFileName != null) {
                            if (!step._builtin && !Path.IsPathRooted(breakpointFileName)) {
                                breakpointFileName = DebuggerTestPath + breakpointFileName;
                            }
                        } else {
                            breakpointFileName = filename;
                        }
                        int breakpointLine = step._targetBreakpoint.Value;
                        int breakpointColumn = step._targetBreakpointColumn.HasValue ? step._targetBreakpointColumn.Value : 0;
                        Breakpoint breakpoint = new Breakpoint(breakpointFileName, breakpointLine, breakpointColumn);
                        Assert.IsFalse(breakpoints.TryGetValue(breakpoint, out nodeBreakpoint));
                        breakpoints[breakpoint] =
                            AddBreakPoint(
                                process,
                                breakpointFileName,
                                breakpointLine,
                                breakpointColumn,
                                step._enabled ?? true,
                                step._breakOn ?? new BreakOn(),
                                step._condition
                            );
                        if (step._expectFailure) {
                            AssertWaited(breakpointBindFailure);
                            AssertNotSet(breakpointBound);
                            breakpointBindFailure.Reset();
                        } else {
                            AssertWaited(breakpointBound);
                            AssertNotSet(breakpointBindFailure);
                            breakpointBound.Reset();
                        }
                        break;
                    case TestAction.RemoveBreakpoint:
                        breakpointFileName = step._targetBreakpointFile ?? filename;
                        breakpointLine = step._targetBreakpoint.Value;
                        breakpointColumn = step._targetBreakpointColumn.HasValue ? step._targetBreakpointColumn.Value : 0;
                        breakpoint = new Breakpoint(breakpointFileName, breakpointLine, breakpointColumn);
                        breakpoints[breakpoint].Remove().WaitAndUnwrapExceptions();
                        breakpoints.Remove(breakpoint);
                        AssertWaited(breakpointUnbound);
                        breakpointUnbound.Reset();
                        break;
                    case TestAction.UpdateBreakpoint:
                        breakpointFileName = step._targetBreakpointFile ?? filename;
                        breakpointLine = step._targetBreakpoint.Value;
                        breakpointColumn = step._targetBreakpointColumn.HasValue ? step._targetBreakpointColumn.Value : 0;
                        breakpoint = new Breakpoint(breakpointFileName, breakpointLine, breakpointColumn);
                        nodeBreakpoint = breakpoints[breakpoint];
                        foreach (var breakpointBinding in nodeBreakpoint.GetBindings()) {
                            if (step._hitCount != null) {
                                Assert.IsTrue(breakpointBinding.SetHitCountAsync(step._hitCount.Value).WaitAndUnwrapExceptions());
                            }
                            if (step._enabled != null) {
                                Assert.IsTrue(breakpointBinding.SetEnabledAsync(step._enabled.Value).WaitAndUnwrapExceptions());
                            }
                            if (step._breakOn != null) {
                                Assert.IsTrue(breakpointBinding.SetBreakOnAsync(step._breakOn.Value).WaitAndUnwrapExceptions());
                            }
                            if (step._condition != null) {
                                Assert.IsTrue(breakpointBinding.SetConditionAsync(step._condition).WaitAndUnwrapExceptions());
                            }
                        }
                        break;
                    case TestAction.KillProcess:
                        process.Terminate();
                        break;
                    case TestAction.Detach:
                        process.Detach();
                        break;
                }

                if (wait) {
                    if (step._expectedEntryPointHit != null) {
                        AssertWaited(entryPointHit);
                        AssertNotSet(breakpointHit);
                        AssertNotSet(stepComplete);
                        AssertNotSet(exceptionRaised);
                        Assert.IsNull(exception);
                        entryPointHit.Reset();
                    } else if (step._expectedBreakpointHit != null) {
                        if (step._expectReBind) {
                            AssertWaited(breakpointUnbound);
                            AssertWaited(breakpointBound);
                            breakpointUnbound.Reset();
                            breakpointBound.Reset();
                        }
                        AssertWaited(breakpointHit);
                        AssertNotSet(entryPointHit);
                        AssertNotSet(stepComplete);
                        AssertNotSet(exceptionRaised);
                        Assert.IsNull(exception);
                        breakpointHit.Reset();
                    } else if (step._expectedStepComplete != null) {
                        AssertWaited(stepComplete);
                        AssertNotSet(entryPointHit);
                        AssertNotSet(breakpointHit);
                        AssertNotSet(exceptionRaised);
                        Assert.IsNull(exception);
                        stepComplete.Reset();
                    } else if (step._expectedExceptionRaised != null) {
                        AssertWaited(exceptionRaised);
                        AssertNotSet(entryPointHit);
                        AssertNotSet(breakpointHit);
                        AssertNotSet(stepComplete);
                        exceptionRaised.Reset();
                    } else {
                        AssertNotSet(entryPointHit);
                        AssertNotSet(breakpointHit);
                        AssertNotSet(stepComplete);
                        AssertNotSet(exceptionRaised);
                        Assert.IsNull(exception);
                    }
                }

                if (step._expectedEntryPointHit != null) {
                    Assert.AreEqual(step._expectedEntryPointHit.Value, thread.Frames.First().Line);
                } else if (step._expectedBreakpointHit != null) {
                    Assert.AreEqual(step._expectedBreakpointHit.Value, thread.Frames.First().Line);
                } else if (step._expectedStepComplete != null) {
                    Assert.AreEqual(step._expectedStepComplete.Value, thread.Frames.First().Line);
                } else if (step._expectedExceptionRaised != null) {
                    Assert.AreEqual(step._expectedExceptionRaised.TypeName, exception.TypeName);
                    Assert.AreEqual(step._expectedExceptionRaised.Description, exception.Description);
                    if (step._expectedExceptionRaised.LineNo != null) {
                        Assert.AreEqual(step._expectedExceptionRaised.LineNo.Value, thread.Frames[0].Line);
                    }
                    exception = null;
                }
                var expectedBreakFile = step._expectedBreakFile;
                if (expectedBreakFile != null) {
                    if (!step._builtin && !Path.IsPathRooted(expectedBreakFile)) {
                        expectedBreakFile = DebuggerTestPath + expectedBreakFile;
                    }
                    Assert.AreEqual(expectedBreakFile, thread.Frames.First().FileName);
                }
                var expectedBreakFunction = step._expectedBreakFunction;
                if (expectedBreakFunction != null) {
                    Assert.AreEqual(expectedBreakFunction, thread.Frames.First().FunctionName);
                }

                if (step._expectedHitCount != null) {
                    string breakpointFileName = step._targetBreakpointFile ?? filename;
                    if (!step._builtin && !Path.IsPathRooted(breakpointFileName)) {
                        breakpointFileName = DebuggerTestPath + breakpointFileName;
                    }
                    int breakpointLine = step._targetBreakpoint.Value;
                    int breakpointColumn = step._targetBreakpointColumn.HasValue ? step._targetBreakpointColumn.Value : 0;
                    var breakpoint = new Breakpoint(breakpointFileName, breakpointLine, breakpointColumn);
                    nodeBreakpoint = breakpoints[breakpoint];
                    foreach (var breakpointBinding in nodeBreakpoint.GetBindings()) {
                        Assert.AreEqual(step._expectedHitCount.Value, breakpointBinding.GetHitCount());
                    }
                }

                if (step._validation != null) {
                    step._validation(process, thread);
                }

                if (step._expectedExitCode != null) {
                    AssertWaited(processExited);
                    Assert.AreEqual(step._expectedExitCode.Value, exitCode);
                }
            }

            if (waitForExit) {
                process.WaitForExit(10000);
            }

            AssertNotSet(entryPointHit);
            AssertNotSet(breakpointHit);
            AssertNotSet(stepComplete);
            AssertNotSet(exceptionRaised);
            Assert.IsNull(exception);
        }

        internal static void AssertWaited(EventWaitHandle eventObj) {
            if (!eventObj.WaitOne(10000)) {
                Assert.Fail("Failed to wait on event");
            }
        }

        internal static void AssertNotSet(EventWaitHandle eventObj) {
            if (eventObj.WaitOne(10)) {
                Assert.Fail("Unexpected set EventWaitHandle");
            }
        }
    }
}
