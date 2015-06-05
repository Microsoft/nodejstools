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
using Microsoft.NodejsTools.Analysis.Values;
using Microsoft.NodejsTools.Parsing;

namespace Microsoft.NodejsTools.Analysis.Analyzer {
    internal class DDG : AstVisitor {
        private readonly JsAnalyzer _analyzer;
        internal AnalysisUnit _unit;
        internal ExpressionEvaluator _eval;
        private Block _curSuite;

        public DDG(JsAnalyzer analyzer) {
            _analyzer = analyzer;
        }

        public int Analyze(Deque<AnalysisUnit> queue, CancellationToken cancel) {
            if (cancel.IsCancellationRequested) {
                return 0;
            }
            // Including a marker at the end of the queue allows us to see in
            // the log how frequently the queue empties.
            var endOfQueueMarker = new AnalysisUnit(null, null);
            int queueCountAtStart = queue.Count;

            if (queueCountAtStart > 0 && queue[queue.Count - 1].Environment != null) {
                queue.Append(endOfQueueMarker);
            }

            HashSet<Node> analyzedItems = new HashSet<Node>();
            while (queue.Count > 0 && !cancel.IsCancellationRequested) {
                _unit = queue.PopLeft();

                if (_unit.Environment == null) {    // endOfQueueMarker
                    _analyzer.Log.EndOfQueue(queueCountAtStart, queue.Count);

                    queueCountAtStart = queue.Count;
                    if (queueCountAtStart > 0) {
                        queue.Append(endOfQueueMarker);
                    }
                    continue;
                }

                if (_unit.Ast != null) {
                    analyzedItems.Add(_unit.Ast);
                }

                _analyzer.Log.Dequeue(queue, _unit);

                _unit.IsInQueue = false;
                SetCurrentUnit(_unit);
                _unit.Analyze(this, cancel);
            }

            if (cancel.IsCancellationRequested) {
                _analyzer.Log.Cancelled(queue);
            }
            return analyzedItems.Count;
        }

        public void SetCurrentUnit(AnalysisUnit unit) {
            _eval = new ExpressionEvaluator(unit);
            _unit = unit;
        }

        public EnvironmentRecord Scope {
            get {
                return _eval.Scope;
            }
            set {
                _eval.Scope = value;
            }
        }

        public JsAnalyzer ProjectState {
            get { return _unit.Analyzer; }
        }

        public override bool Walk(JsAst node) {
#if FALSE
            ModuleReference existingRef;
#endif
            //Debug.Assert(node == _unit.Ast);
#if FALSE
            if (!ProjectState.Modules.TryGetValue(_unit.DeclaringModule.Name, out existingRef)) {
                // publish our module ref now so that we don't collect dependencies as we'll be fully processed
                ProjectState.Modules[_unit.DeclaringModule.Name] = new ModuleReference(_unit.DeclaringModule);
            }
#endif

            return base.Walk(node);
        }

        /// <summary>
        /// Gets the function which we are processing code for currently or
        /// null if we are not inside of a function body.
        /// </summary>
        public FunctionEnvironmentRecord CurrentFunction {
            get { return CurrentContainer<FunctionEnvironmentRecord>(); }
        }

#if FALSE
        public ClassScope CurrentClass {
            get { return CurrentContainer<ClassScope>(); }
        }
#endif
        private T CurrentContainer<T>() where T : EnvironmentRecord {
            return Scope.EnumerateTowardsGlobal.OfType<T>().FirstOrDefault();
        }

        public override bool Walk(ExpressionStatement node) {
            BinaryOperator binOp = node.Expression as BinaryOperator;
            if (binOp != null && binOp.OperatorToken == JSToken.Assign) {
                EnvironmentRecord newEnv;
                if (Scope.GlobalEnvironment.TryGetNodeEnvironment(node.Expression, out newEnv)) {
                    var res = _eval.Evaluate(binOp.Operand2);                    
                    Scope = newEnv;
                    _eval.AssignTo(binOp, binOp.Operand1, res);
                } else {
                    _eval.Evaluate(node.Expression);
                }
            } else {
                _eval.Evaluate(node.Expression);
            }
            return false;
        }

        public override bool Walk(VariableDeclaration node) {
            if (node.Initializer != null) {
                _eval.Scope.AssignVariable(
                    node.Name,
                    node,
                    _unit,
                    _eval.Evaluate(node.Initializer)
                );
            }
            return false;
        }

        public override bool Walk(Var node) {
            return base.Walk(node);
        }

        public override bool Walk(ForNode node) {
            if (node.Initializer != null) {
                node.Initializer.Walk(this);
            }
            if (node.Body != null) {
                node.Body.Walk(this);
            }
            if (node.Incrementer != null) {
                _eval.Evaluate(node.Incrementer);
            }
            if (node.Condition != null) {
                _eval.Evaluate(node.Condition);
            }
            return false;
        }

        public override bool Walk(ForIn node) {
            var coll = _eval.Evaluate(node.Collection);
            var variable = node.Variable as Var;
            var lookupVar = node.Variable as ExpressionStatement;
            if (variable != null) {
                string varName = variable.First().Name;
                Debug.Assert(_eval.Scope.ContainsVariable(varName));
                var prevVar = _eval.Scope.GetVariable(node.Variable, _unit, varName);
                bool walkedBody = false;
                if (coll.Count == 1) {
                    foreach (var value in coll) {
                        var values = value.GetEnumerationValues(node, _unit);
                        if (values.Count < 50) {
                            walkedBody = true;
                            Debug.WriteLine(String.Format("Enumerating: {1} {0}", value, values.Count));

                            foreach (var individualValue in values) {
                                var newVar = new LocalNonEnqueingVariableDef();
                                _eval.Scope.ReplaceVariable(variable.First().Name, newVar);

                                _eval.Scope.AssignVariable(
                                    varName,
                                    node,
                                    _unit,
                                    individualValue
                                );
                                node.Body.Walk(this);
                                prevVar.AddTypes(
                                    _unit,
                                    newVar.GetTypesNoCopy(
                                        _unit.ProjectEntry,
                                        _unit.ProjectEntry
                                    )
                                );
                            }
                        }
                    }
                }

                if (!walkedBody) {
                    foreach (var value in coll) {
                        var values = value.GetEnumerationValues(node, _unit);
                        prevVar.AddTypes(
                            _unit,
                            values
                        );
                    }

                    // we replace the variable here so that we analyze w/ empty
                    // types, but we do the assignment so users get completions against
                    // anything in the for body.
                    var newVar = new LocalNonEnqueingVariableDef();
                    _eval.Scope.ReplaceVariable(variable.First().Name, newVar);

                    node.Body.Walk(this);
                }

                _eval.Scope.ReplaceVariable(varName, prevVar);
            } else if (lookupVar != null) {
                // TODO: _eval.AssignTo(node, lookupVar.Expression, values);
                node.Body.Walk(this);
            }

            return false;
        }

#if FALSE
        private bool TryImportModule(string modName, bool forceAbsolute, out ModuleReference moduleRef) {
            if (ProjectState.Limits.CrossModule != null &&
                ProjectState.ModulesByFilename.Count > ProjectState.Limits.CrossModule) {
                // too many modules loaded, disable cross module analysis by blocking
                // scripts from seeing other modules.
                moduleRef = null;
                return false;
            }

            foreach (var name in PythonAnalyzer.ResolvePotentialModuleNames(_unit.ProjectEntry, modName, forceAbsolute)) {
                if (ProjectState.Modules.TryGetValue(name, out moduleRef)) {
                    return true;
                }
            }

            _unit.DeclaringModule.AddUnresolvedModule(modName, forceAbsolute);

            moduleRef = null;
            return false;
        }

        internal List<AnalysisValue> LookupBaseMethods(string name, IEnumerable<IAnalysisSet> bases, Node node, AnalysisUnit unit) {
            var result = new List<AnalysisValue>();
            foreach (var b in bases) {
                foreach (var curType in b) {
                    BuiltinClassInfo klass = curType as BuiltinClassInfo;
                    if (klass != null) {
                        var value = klass.GetMember(node, unit, name);
                        if (value != null) {
                            result.AddRange(value);
                        }
                    }
                }
            }
            return result;
        }
#endif

        public override bool Walk(FunctionObject node) {
            return false;
        }

        internal void WalkBody(Node node, AnalysisUnit unit) {
            var oldUnit = _unit;
            var eval = _eval;
            _unit = unit;
            _eval = new ExpressionEvaluator(unit);
            try {
                node.Walk(this);
            } finally {
                _unit = oldUnit;
                _eval = eval;
            }
        }

        public override bool Walk(IfNode node) {
            _eval.Evaluate(node.Condition);
            //TryPushIsInstanceScope(test, test.Test);
            if (node.TrueBlock != null) {
                node.TrueBlock.Walk(this);
            }

            if (node.FalseBlock != null) {
                node.FalseBlock.Walk(this);
            }
            return false;
        }

#if FALSE
        public override bool Walk(ImportStatement node) {
            int len = Math.Min(node.Names.Count, node.AsNames.Count);
            for (int i = 0; i < len; i++) {
                var curName = node.Names[i];
                var asName = node.AsNames[i];

                string importing, saveName;
                Node nameNode;
                if (curName.Names.Count == 0) {
                    continue;
                } else if (curName.Names.Count > 1) {
                    // import fob.oar
                    if (asName != null) {
                        // import fob.oar as baz, baz becomes the value of the oar module
                        importing = curName.MakeString();
                        saveName = asName.Name;
                        nameNode = asName;
                    } else {
                        // plain import fob.oar, we bring in fob into the scope
                        saveName = importing = curName.Names[0].Name;
                        nameNode = curName.Names[0];
                    }
                } else {
                    // import fob
                    importing = curName.Names[0].Name;
                    if (asName != null) {
                        saveName = asName.Name;
                        nameNode = asName;
                    } else {
                        saveName = importing;
                        nameNode = curName.Names[0];
                    }
                }

                ModuleReference modRef;

                var def = Scope.CreateVariable(nameNode, _unit, saveName);
                if (TryImportModule(importing, node.ForceAbsolute, out modRef)) {
                    modRef.AddReference(_unit.DeclaringModule);

                    Debug.Assert(modRef.Module != null);
                    if (modRef.Module != null) {
                        modRef.Module.Imported(_unit);

                        if (modRef.AnalysisModule != null) {
                            def.AddTypes(_unit, modRef.AnalysisModule);
                        }
                        def.AddAssignment(nameNode, _unit);
                    }
                }
            }
            return true;
        }
#endif
        public override bool Walk(ReturnNode node) {
            var fnScope = CurrentFunction;
            if (node.Operand != null && fnScope != null) {
                var lookupRes = _eval.Evaluate(node.Operand);
                fnScope.AddReturnTypes(node, _unit, lookupRes);
            }
            return false;
        }

        public override bool Walk(Block node) {
            var prevSuite = _curSuite;
            var prevScope = Scope;

            _curSuite = node;
            try {
                for (int i = 0; i < node.Count; i++) {
                    if (IsGwtCode(node, i)) {
                        return false;
                    }

                    node[i].Walk(this);
                }
            } finally {
                Scope = prevScope;
                _curSuite = prevSuite;
            }
            return false;
        }

        internal static bool IsGwtCode(Block node, int i) {
            if (i == 0) {
                var varDecl = node[i] as Declaration;
                if (varDecl != null && varDecl.Count == 1) {
                    if (varDecl[i].Name == "$gwt_version") {
                        // this code is ugly, generated, and not analyzable...
                        return true;
                    }
                }
            }
            return false;
        }
#if FALSE
        public override bool Walk(DelStatement node) {
            foreach (var expr in node.Expressions) {
                DeleteExpression(expr);
            }
            return false;
        }

        private void DeleteExpression(Expression expr) {
            Lookup name = expr as Lookup;
            if (name != null) {
                var var = Scope.CreateVariable(name, _unit, name.Name);

                return;
            }

            IndexExpression index = expr as IndexExpression;
            if (index != null) {
                var values = _eval.Evaluate(index.Target);
                var indexValues = _eval.Evaluate(index.Index);
                foreach (var value in values) {
                    value.DeleteIndex(index, _unit, indexValues);
                }
                return;
            }

            MemberExpression member = expr as MemberExpression;
            if (member != null) {
                var values = _eval.Evaluate(member.Target);
                foreach (var value in values) {
                    value.DeleteMember(member, _unit, member.Name);
                }
                return;
            }

            ParenthesisExpression paren = expr as ParenthesisExpression;
            if (paren != null) {
                DeleteExpression(paren.Expression);
                return;
            }

            SequenceExpression seq = expr as SequenceExpression;
            if (seq != null) {
                foreach (var item in seq.Items) {
                    DeleteExpression(item);
                }
                return;
            }
        }
#endif
        public override bool Walk(ThrowNode node) {
            _eval.EvaluateMaybeNull(node.Operand);
            return false;
        }

        public override bool Walk(WhileNode node) {
            _eval.Evaluate(node.Condition);
            if (node.Body != null) {
                node.Body.Walk(this);
            }
            return false;
        }

        public override bool Walk(TryNode node) {
            node.TryBlock.Walk(this);
            if (node.CatchBlock != null) {
                node.CatchBlock.Walk(this);
            }
#if FALSE
            if (node.Handlers != null) {
                foreach (var handler in node.Handlers) {
                    var test = AnalysisSet.Empty;
                    if (handler.Test != null) {
                        var testTypes = _eval.Evaluate(handler.Test);

                        if (handler.Target != null) {
                            foreach (var type in testTypes) {
                                ClassInfo klass = type as ClassInfo;
                                if (klass != null) {
                                    test = test.Union(klass.Instance.SelfSet);
                                }

                                BuiltinClassInfo builtinClass = type as BuiltinClassInfo;
                                if (builtinClass != null) {
                                    test = test.Union(builtinClass.Instance.SelfSet);
                                }
                            }

                            _eval.AssignTo(handler, handler.Target, test);
                        }
                    }

                    handler.Body.Walk(this);
                }
            }
#endif

            if (node.FinallyBlock != null) {
                node.FinallyBlock.Walk(this);
            }
            return false;
        }
    }
}
