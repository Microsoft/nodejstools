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

namespace Microsoft.NodejsTools.Jade {
    /// <summary>
    /// Represents collection of ITextRange items
    /// </summary>
    interface ITextRangeCollection<T> : ICompositeTextRange, IEnumerable<T> {
        /// <summary>
        /// Number of items in collection
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Retrieves Nth item in the collection
        /// </summary>
        T this[int index] { get; }

        /// <summary>
        /// Adds comment to collection unless it is already there.
        /// </summary>
        /// <param name="commentToken">Comment token to add</param>
        void Add(T item);

        /// <summary>
        /// Add a set of items to the collection
        /// </summary>
        /// <param name="items">Items to add</param>
        void Add(IEnumerable<T> items);

        /// <summary>
        /// Returns index of item that starts at the given position if exists, -1 otherwise.
        /// </summary>
        /// <param name="position">Position in a text buffer</param>
        /// <returns>Item index or -1 if not found</returns>
        int GetItemAtPosition(int position);

        /// <summary>
        /// Returns index of items that contains given position if exists, -1 otherwise.
        /// </summary>
        /// <param name="position">Position in a text buffer</param>
        /// <returns>Item index or -1 if not found</returns>
        int GetItemContaining(int position);

        /// <summary>
        /// Removes all items from collection
        /// </summary>
        void Clear();

        /// <summary>
        /// Sorts ranges in collection by start position.
        /// </summary>
        void Sort();
    }
}
