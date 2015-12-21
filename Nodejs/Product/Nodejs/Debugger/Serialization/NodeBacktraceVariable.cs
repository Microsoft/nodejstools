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
using Microsoft.VisualStudioTools.Project;
using Newtonsoft.Json.Linq;

namespace Microsoft.NodejsTools.Debugger.Serialization {
    sealed class NodeBacktraceVariable : INodeVariable {
        public NodeBacktraceVariable(NodeStackFrame stackFrame, JToken parameter) {
            Utilities.ArgumentNotNull("stackFrame", stackFrame);
            Utilities.ArgumentNotNull("parameter", parameter);

            JToken value = parameter["value"];
            Id = (int)value["ref"];
            Parent = null;
            StackFrame = stackFrame;
            Name = (string)parameter["name"] ?? NodeVariableType.AnonymousVariable;
            TypeName = (string)value["type"];
            Value = GetValue((JValue)value["value"]);
            Class = (string)value["className"];
            try {
                Text = (string)value["text"];
            } catch (ArgumentException) {
                Text = String.Empty;
            }
            Attributes = NodePropertyAttributes.None;
            Type = NodePropertyType.Normal;
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

        /// <summary>
        /// Converts the JValue to string.
        /// </summary>
        /// <param name="value">Value which has to be converted to string representation.</param>
        /// <returns>String which represents the value.</returns>
        private static string GetValue(JValue value) {
            if (value == null) {
                return null;
            }

            if (value.Type == JTokenType.Date) {
                var parentValue = value.Parent.ToString();
                return parentValue.Replace("\"value\": \"", string.Empty)
                    .Replace("\"", string.Empty);
                // var dateTimeValue = (DateTime)value.Value;
                // return dateTimeValue.ToUniversalTime().ToString("s") + "Z";
            }

            return (string)value;
        }
    }
}