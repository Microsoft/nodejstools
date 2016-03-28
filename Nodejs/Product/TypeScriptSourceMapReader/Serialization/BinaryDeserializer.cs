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
using System.IO;

namespace Microsoft.NodejsTools.TypeScriptSourceMapReader {
    /// <summary>
    /// Custom deserializer to convert the byte[] into different types
    /// The serialization and deserialization needs to be always in sync
    /// </summary>
    internal class BinaryDeserializer {
        /// <summary>
        /// Memory stream to read data from
        /// </summary>
        private MemoryStream memoryStream;

        /// <summary>
        /// Binary reader to reader basic types from binary format
        /// </summary>
        private BinaryReader binaryReader;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="byteArray"></param>
        internal BinaryDeserializer(byte[] byteArray) {
            this.memoryStream = new MemoryStream(byteArray);
            this.binaryReader = new BinaryReader(memoryStream);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        internal void Close() {
            this.binaryReader.Close();
            this.memoryStream.Close();
        }

        internal bool DeserializeBoolean() {
            return this.binaryReader.ReadBoolean();
        }

        internal int DeserializeInt32() {
            return this.binaryReader.ReadInt32();
        }

        internal ulong DeserializeUInt64() {
            return this.binaryReader.ReadUInt64();
        }

        internal string DeserializeString() {
            return this.binaryReader.ReadString();
        }

        internal string[] DeserializeStringArray() {
            var stringArrayCount = this.DeserializeInt32();
            var stringArray = new string[stringArrayCount];
            for (var i = 0; i < stringArrayCount; i++) {
                stringArray[i] = this.DeserializeString();
            }

            return stringArray;
        }

        internal byte[] DeserializeByteArray() {
            var byteArrayCount = this.DeserializeInt32();
            return this.binaryReader.ReadBytes(byteArrayCount);
        }

        internal Guid DeserializeGuid() {
            return new Guid(this.DeserializeByteArray());
        }
    }
}
