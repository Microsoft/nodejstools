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

using System.Collections.Generic;

namespace Microsoft.NodejsTools.Telemetry {
    /// <summary>
    /// DataPointCollection - Represents a collection of DataPoints to report for telemetry
    /// </summary>
    public class DataPointCollection : List<DataPoint> {
        public DataPointCollection() : base() {
        }

        public DataPointCollection(int capacity) : base(capacity) {
        }

        public DataPointCollection(params DataPoint[] datapoints) : base(datapoints) {
        }

        public void Add(string name, object value, bool isPersonallyIdentifiable = false) {
            base.Add(new DataPoint(name, value, isPersonallyIdentifiable));
        }

        public void Add(params DataPoint[] datapoints) {
            base.AddRange(datapoints);
        }
    }
}