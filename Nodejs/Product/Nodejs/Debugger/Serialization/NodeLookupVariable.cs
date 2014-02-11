﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System.Collections.Generic;
using Microsoft.VisualStudioTools.Project;
using Newtonsoft.Json.Linq;

namespace Microsoft.NodejsTools.Debugger.Serialization {
    sealed class NodeLookupVariable : INodeVariable {
        public NodeLookupVariable(NodeEvaluationResult parent, JToken property, Dictionary<int, JToken> references) {
            Utilities.ArgumentNotNull("property", property);
            Utilities.ArgumentNotNull("references", references);

            Id = (int)property["ref"];
            JToken reference = references[Id];
            Parent = parent;
            StackFrame = parent != null ? parent.Frame : null;
            Name = (string)property["name"];
            TypeName = (string)reference["type"];
            Value = (string)reference["value"];
            Class = (string)reference["className"];
            Text = (string)reference["text"];
            Attributes = (NodePropertyAttributes)property.Value<int>("attributes");
            Type = (NodePropertyType)property.Value<int>("propertyType");
        }

        public int Id { get; private set; }
        public NodeEvaluationResult Parent { get; private set; }
        public string Name { get; private set; }
        public string TypeName { get; private set; }
        public string Value { get; private set; }
        public string Class { get; private set; }
        public string Text { get; private set; }
        public NodePropertyAttributes Attributes { get; private set; }
        public NodePropertyType Type { get; private set; }
        public NodeStackFrame StackFrame { get; private set; }
    }
}