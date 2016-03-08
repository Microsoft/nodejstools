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
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.TestWindow.Extensibility;

namespace Microsoft.NodejsTools.TestAdapter {
    [Export(typeof(IRunFromContextFileExtensions))]
    class RunFromContextFileExtensions : IRunFromContextFileExtensions {
        #region IRunFromContextFileExtensions Members

        public IEnumerable<string> FileTypes {
            get { 
                return new[] { NodejsFileTypeHelpers.JavaScriptExtension };
            }
        }

        #endregion
    }
}
