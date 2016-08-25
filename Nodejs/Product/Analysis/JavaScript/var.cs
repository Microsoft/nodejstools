// var.cs
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

namespace Microsoft.NodejsTools.Parsing {
    /// <summary>
    /// Summary description for variablestatement.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "AST statement")]
    [Serializable]
    internal sealed class Var : Declaration
    {
        public Var(EncodedSpan span)
            : base(span)
        {
        }

        public override void Walk(AstVisitor walker) {
            if (walker.Walk(this)) {
                foreach (var decl in this) {
                    decl.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }
    }
}
