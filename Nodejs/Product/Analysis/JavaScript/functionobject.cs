// functionobject.cs
//
// Copyright 2010 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;

namespace Microsoft.NodejsTools.Parsing {
    [Serializable]
    internal sealed class FunctionObject : Statement, INameDeclaration {
        private Block m_body;
        private ParameterDeclaration[] m_parameters;
        private int _generatorIndex = -1;

        public FunctionObject(EncodedSpan functionSpan)
            : base(functionSpan) {
        }

        public Block Body {
            get { return m_body; }
            set {
                m_body.ClearParent(this);
                m_body = value;
                m_body.AssignParent(this);
            }
        }

        public ParameterDeclaration[] ParameterDeclarations {
            get { return m_parameters; }
            set {
                if (value != null) {
                    foreach (var node in value) {
                        node.ClearParent(this);
                    }
                }
                m_parameters = value;
                if (value != null) {
                    foreach (var node in value) {
                        node.AssignParent(this);
                    }
                }
            }
        }

        public FunctionType FunctionType { get; set; }

        public Expression Initializer { get { return null; } }

        public string Name { get; set; }

        public string NameGuess { get; set; }

        public string Doclet { get; set; }

        // TODO: NameIndex?
        public IndexSpan NameSpan {
            get;
            set;
        }

        public IndexSpan GetNameSpan(LocationResolver resolver) {
            return NameSpan;
        }

        public IndexSpan ParametersSpan { get; set; }
        public bool IsExpression { get; set; }
        public bool IsGenerator { get { return _generatorIndex != -1; } }

        public int GeneratorIndex {
            get { return _generatorIndex; }
            set { _generatorIndex = value; }
        }

        public int ParameterStart {
            get {
                return ParametersSpan.Start;
            }
        }

        public int ParameterEnd {
            get {
                return ParametersSpan.End;
            }
        }

        public JSVariableField VariableField { get; set; }
        
        public override void Walk(AstVisitor walker) {
            if (walker.Walk(this)) {
                if (m_parameters != null) {
                    foreach (var param in m_parameters) {
                        if (param != null) {
                            param.Walk(walker);
                        }
                    }
                }

                if (Body != null) {
                    Body.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }

        public override IEnumerable<Node> Children {
            get {
                if (ParameterDeclarations != null) {
                    foreach (var decl in ParameterDeclarations) {
                        if (decl != null) {
                            yield return decl;
                        }
                    }
                }
                if (Body != null) {
                    yield return Body;
                }
            }
        }
    }
}