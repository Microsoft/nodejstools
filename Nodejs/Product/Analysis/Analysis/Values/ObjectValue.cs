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
using System.Linq;
using System.Text;
using Microsoft.NodejsTools.Analysis.Analyzer;
using Microsoft.NodejsTools.Parsing;

namespace Microsoft.NodejsTools.Analysis.Values {
    /// <summary>
    /// Represents a JavaScript object (constructed via a literal or
    /// as the result of a new someFunction call).
    /// </summary>
    [Serializable]
    internal class ObjectValue : ExpandoValue {
#if DEBUG
        private readonly string _description;
#endif

        public ObjectValue(ProjectEntry projectEntry, AnalysisValue prototype = null, string description = null)
            : base(projectEntry) {
            if (prototype != null) {
                Add("__proto__", prototype.SelfSet);
            }
#if DEBUG
            _description = description;
#endif
        }

        internal override Dictionary<string, IAnalysisSet> GetAllMembers(ProjectEntry accessor) {
            var res = base.GetAllMembers(accessor);
            var prototypes = GetPrototype(accessor);
            if (prototypes != null) {
                try {
                    foreach (var value in prototypes) {
                        if (PushProtoLookup(value.Value)) {
                            foreach (var kvp in value.Value.GetAllMembers(accessor)) {
                                MergeTypes(res, kvp.Key, kvp.Value);
                            }
                        }
                    }
                } finally {
                    foreach(var value in prototypes) {
                        PopProtoLookup(value.Value);
                    }
                }
            }

            return res;
        }

        public override IAnalysisSet Get(Node node, AnalysisUnit unit, string name, bool addRef = true) {
            var res = base.Get(node, unit, name, addRef);

            // we won't recurse on prototype because we either have
            // a prototype value and it's correct, or we don't have
            // a prototype.  Recursing on prototype results in
            // prototypes getting merged and the analysis bloating
            if (name != "prototype") {                
                return res.Union(GetRecurse(this, node, unit, name, addRef));
            }

            return res;
        }

        [ThreadStatic]
        private static Dictionary<AnalysisValue, int> _hitCount;

        internal static bool PushProtoLookup(AnalysisValue value){
            if (_hitCount == null) {
                _hitCount = new Dictionary<AnalysisValue, int>();
            }

            int count;
            if (!_hitCount.TryGetValue(value, out count)) {
                _hitCount[value] = 1;
                return true;
            } else {
                _hitCount[value] = count + 1;
            }
            return false;
        }

        internal static void PopProtoLookup(AnalysisValue value) {
            int count = _hitCount[value];
            if (count == 1) {
                _hitCount.Remove(value);
            } else {
                _hitCount[value] = count - 1;
            }
        }

        private IAnalysisSet GetRecurse(AnalysisValue protoStart, Node node, AnalysisUnit unit, string name, bool addRef) {
            var prototypes = protoStart.GetPrototype(unit.ProjectEntry);
            IAnalysisSet protovalue = AnalysisSet.Empty;
            if (prototypes != null) {
                try {
                    foreach (var proto in prototypes) {
                        if (PushProtoLookup(proto.Value)) {
                            var property = proto.Value.GetProperty(node, unit, name);
                            if (property != null) {
                                var value = property.GetValue(
                                    node,
                                    unit,
                                    proto.Value.DeclaringModule,
                                    this.SelfSet,
                                    addRef
                                );
                                protovalue = protovalue.Union(value);
                                if (property.IsEphemeral) {
                                    protovalue = protovalue.Union(GetRecurse(proto.Value, node, unit, name, addRef));
                                }
                            } else {
                                // keep searching the prototype chain...
                                protovalue = protovalue.Union(GetRecurse(proto.Value, node, unit, name, addRef));
                            }
                        }
                    }
                } finally {
                    foreach (var proto in prototypes) {
                        PopProtoLookup(proto.Value);
                    }
                }
            }
           
            return protovalue;
        }

        internal override IAnalysisSet GetPrototype(ProjectEntry accessor) {
            IAnalysisSet protoTypes;
            PropertyDescriptorValue protoDesc;
            if (Descriptors != null &&
                Descriptors.TryGetValue("__proto__", out protoDesc) &&
                protoDesc.Values != null &&
                (protoTypes = protoDesc.Values.GetTypesNoCopy(accessor, ProjectEntry)).Count > 0) {
                // someone has assigned to __proto__, so that's our [[Prototype]]
                // property now.
                return protoTypes;
            } else if (this != ProjectState._objectPrototype) {
                // [[Prototype]] hasn't been assigned any other way, we have
                // object's prototype.
                return ProjectState._objectPrototype.SelfSet;
            }

            return base.GetPrototype(accessor);
        }

        public override void SetMember(Node node, AnalysisUnit unit, string name, IAnalysisSet value) {
            if (name == "__proto__" && value.Count > 0) {
                if (SetMemberWorker(node, unit, name, value)) {
                    // assignment to __proto__ means all of our previous lookups
                    // need to be re-evaluated with the new __proto__ value.
                    foreach (var kvp in Descriptors) {
                        if (kvp.Value.Values != null) {
                            kvp.Value.Values.EnqueueDependents(unit.ProjectEntry, ProjectEntry);
                        }
                    }
                }
            } else {
                base.SetMember(node, unit, name, value);
            }
        }
        public override BuiltinTypeId TypeId {
            get {
                return BuiltinTypeId.Object;
            }
        }

        public override JsMemberType MemberType {
            get {
                return JsMemberType.Object;
            }
        }

        public virtual string ObjectDescription {
            get {
                return "object";
            }
        }

        public override string ShortDescription {
            get {
                return ObjectDescription;
            }
        }

        public override string Description {
            get {
                StringBuilder res = new StringBuilder();
                res.Append(ObjectDescription);

                if (Descriptors != null) {
                    var names = Descriptors
                        .Where(VariableIsDefined)
                        .Where(NotDunder)
                        .Select(x => x.Key).ToArray();

                    if (names.Length > 0) {
                        res.AppendLine();
                        res.Append("Contains: ");
                        int lineLength = "Contains: ".Length;
                        Array.Sort(names);
                        for (int i = 0; i < names.Length; i++) {
                            res.Append(names[i]);
                            lineLength += names[i].Length;
                            if (i != names.Length - 1) {
                                res.Append(", ");
                                lineLength += 3;
                            }
                            if (lineLength > 160) {
                                lineLength = 0;
                                res.AppendLine();
                            }
                        }
                    }
                }
                return res.ToString();
            }
        }

        private static bool VariableIsDefined(KeyValuePair<string, PropertyDescriptorValue> desc) {
            return (desc.Value.Values != null && desc.Value.Values.VariableStillExists) ||
                   desc.Value.Getter != null;
        }

        private static bool NotDunder(KeyValuePair<string, PropertyDescriptorValue> desc) {
            return !(desc.Key.StartsWith("__") && desc.Key.EndsWith("__"));
        }

        internal override bool UnionEquals(AnalysisValue ns, int strength) {
            if (strength >= MergeStrength.ToObject) {
                // II + II => BII(object)
                // II + BII => BII(object)
#if FALSE
                var obj = ProjectState.ClassInfos[BuiltinTypeId.Object];
                return ns is InstanceInfo || 
                    //(ns is BuiltinInstanceInfo && ns.TypeId != BuiltinTypeId.Type && ns.TypeId != BuiltinTypeId.Function) ||
                    ns == obj.Instance;
#endif
#if FALSE
            } else if (strength >= MergeStrength.ToBaseClass) {
                var ii = ns as InstanceInfo;
                if (ii != null) {
                    return ii.ClassInfo.UnionEquals(ClassInfo, strength);
                }
                var bii = ns as BuiltinInstanceInfo;
                if (bii != null) {
                    return bii.ClassInfo.UnionEquals(ClassInfo, strength);
                }
#endif
            }

            return base.UnionEquals(ns, strength);
        }

        internal override int UnionHashCode(int strength) {
            if (strength >= MergeStrength.ToObject) {
                //return ProjectState.ClassInfos[BuiltinTypeId.Object].Instance.UnionHashCode(strength);
#if FALSE
            } else if (strength >= MergeStrength.ToBaseClass) {
                return ClassInfo.UnionHashCode(strength);
#endif
            }

            return base.UnionHashCode(strength);
        }

        internal override AnalysisValue UnionMergeTypes(AnalysisValue ns, int strength) {
            if (strength >= MergeStrength.ToObject) {
                // II + II => BII(object)
                // II + BII => BII(object)
                //return ProjectState.ClassInfos[BuiltinTypeId.Object].Instance;
#if FALSE
            } else if (strength >= MergeStrength.ToBaseClass) {
                var ii = ns as InstanceInfo;
                if (ii != null) {
                    return ii.ClassInfo.UnionMergeTypes(ClassInfo, strength).GetInstanceType().Single();
                }
                var bii = ns as BuiltinInstanceInfo;
                if (bii != null) {
                    return bii.ClassInfo.UnionMergeTypes(ClassInfo, strength).GetInstanceType().Single();
                }
#endif
            }

            return base.UnionMergeTypes(ns, strength);
        }

        public override IEnumerable<IReferenceable> GetDefinitions(string name) {
            foreach (var res in base.GetDefinitions(name)) {
                yield return res;
            }

            var prototypes = GetPrototype(null);
            if (prototypes != null) {
                try {
                    foreach (var value in prototypes) {
                        if (PushProtoLookup(value.Value)) {
                            var protoContainer = value.Value as IReferenceableContainer;
                            if (protoContainer != null) {
                                foreach (var res in protoContainer.GetDefinitions(name)) {
                                    yield return res;
                                }
                            }
                        }
                    }
                } finally {
                    foreach (var value in prototypes) {
                        PopProtoLookup(value.Value);
                    }
                }
            }


            if (Push()) {
                try {
                    if (this != ProjectState._objectPrototype) {
                        foreach (var res in ProjectState._objectPrototype.GetDefinitions(name)) {
                            yield return res;
                        }
                    }
                } finally {
                    Pop();
                }
            }
        }
    }
}
