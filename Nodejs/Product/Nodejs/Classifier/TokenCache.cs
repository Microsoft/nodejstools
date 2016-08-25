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


using Microsoft.VisualStudio.Text.Tagging;
using System.Diagnostics;
using System;
using System.Text;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.NodejsTools.Classifier {
    [DebuggerDisplay("{GetDebugView(),nq}")]
    internal struct LineTokenization : ITag {
        public readonly TokenInfo[] Tokens;
        public readonly object State;

        public LineTokenization(TokenInfo[] tokens, object state) {
            Tokens = tokens;
            State = state;
        }

        internal string GetDebugView() {
            StringBuilder sb = new StringBuilder();
            if (State != null) {
                sb.Append(State != null ? "S " : "  ");
            }
            if (Tokens != null) {
                for (int i = 0; i < Tokens.Length; i++) {
                    sb.Append('[');
                    sb.Append(Tokens[i].Category);
                    sb.Append(']');
                }
            }
            return sb.ToString();
        }
    }

    internal class TokenCache {
        private LineTokenization[] _map;

        internal TokenCache() {
            _map = null;
        }

        /// <summary>
        /// Looks for the first cached tokenization preceding the given line.
        /// Returns the line we have a tokenization for or minLine - 1 if there is none.
        /// </summary>
        internal int IndexOfPreviousTokenization(int line, int minLine, out LineTokenization tokenization) {
            if (line < 0) {
                throw new ArgumentOutOfRangeException("line", "Must be 0 or greater");
            }
            Utilities.CheckNotNull(_map);

            line--;
            while (line >= minLine) {
                if (_map[line].Tokens != null) {
                    tokenization = _map[line];
                    return line;
                }
                line--;
            }
            tokenization = default(LineTokenization);
            return minLine - 1;
        }

        internal bool TryGetTokenization(int line, out LineTokenization tokenization) {
            if (line < 0) {
                throw new ArgumentOutOfRangeException("line", "Must be 0 or greater");
            }
            Utilities.CheckNotNull(_map);

            if (_map[line].Tokens != null) {
                tokenization = _map[line];
                return true;
            } else {
                tokenization = default(LineTokenization);
                return false;
            }
        }

        internal LineTokenization this[int line] {
            get {
                return _map[line];
            }
            set {
                _map[line] = value;
            }
        }

        internal void Clear() {
            _map = null;
        }

        internal void EnsureCapacity(int capacity) {
            if (_map == null) {
                _map = new LineTokenization[capacity];
            } else if (_map.Length < capacity) {
                Array.Resize(ref _map, Math.Max(capacity, (_map.Length + 1) * 2));
            }
        }

        internal void DeleteLines(int index, int count) {
            if (index > _map.Length - count) {
                throw new ArgumentOutOfRangeException("line", "Must be 'count' less than the size of the cache");
            }
            Utilities.CheckNotNull(_map);
            
            // Move the lines following our deletion up to 'remove' the deleted lines.
            Array.Copy(_map, index + count, _map, index, _map.Length - index - count);
           
            // From the end of the map remove 'count' lines to remove the now bogus data at the end of the map.
            for (int i = 0; i < count; i++) {
                _map[_map.Length - i - 1] = default(LineTokenization);
            }
        }

        internal void InsertLines(int index, int count) {
            Utilities.CheckNotNull(_map);

            // Copy all rows below to make room for the inserted lines.
            Array.Copy(_map, index, _map, index + count, _map.Length - index - count);

            // Clear all of the cached lines where we inserted since they have the old content.
            // Also, clear the line before the insert as it could have been split and is now invalid.
            for (int i = 0; i < count; i++) {
                _map[index + i] = default(LineTokenization);
            }

            if (index + count < _map.Length) {
                // Some cases (splitting lines for instance) result in an extra line afterwards
                // that should be cleared as well.
                _map[index + count] = default(LineTokenization);
            }
        }
    }
}
