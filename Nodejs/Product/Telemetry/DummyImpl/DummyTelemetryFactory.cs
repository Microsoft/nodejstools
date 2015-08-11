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

namespace Microsoft.NodejsTools.Telemetry {
    /// <summary>
    /// Default, dummy <see cref="ITelemetryFactory"/> implementation.
    /// It's intended to be used when no other real implementation of ITelemetryFactory is found
    /// </summary>
    internal class DummyTelemetryFactory : ITelemetryFactory {
        private readonly static ITelemetryLogger _defaultLogger = new DummyTelemetryLogger();

        public ITelemetryLogger GetLogger() {
            return _defaultLogger;
        }
    }
}
