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
using System.Threading;
using Microsoft.NodejsTools.Analysis;
using Microsoft.VisualStudio.Text;

namespace Microsoft.NodejsTools.Intellisense {
    sealed partial class VsProjectAnalyzer {

        class BufferParser : IDisposable {
            internal VsProjectAnalyzer _parser;
            private readonly Timer _timer;
            private IList<ITextBuffer> _buffers;
            private bool _parsing, _requeue, _textChange;
            internal IProjectEntry _currentProjEntry;
            private ITextDocument _document;
            public int AttachedViews;

            private const int ReparseDelay = 1000;      // delay in MS before we re-parse a buffer w/ non-line changes.

            public BufferParser(IProjectEntry initialProjectEntry, VsProjectAnalyzer parser, ITextBuffer buffer) {
                _parser = parser;
                _timer = new Timer(ReparseTimer, null, Timeout.Infinite, Timeout.Infinite);
                _buffers = new[] { buffer };
                _currentProjEntry = initialProjectEntry;
                AttachedViews = 1;

                InitBuffer(buffer);
            }

            public ITextBuffer[] Buffers {
                get {
                    return _buffers.ToArray();
#if FALSE
                return _buffers.Where(
                    x => !x.Properties.ContainsProperty(NodejsReplEvaluator.InputBeforeReset)
                ).ToArray();
#endif
                }
            }

            internal void AddBuffer(ITextBuffer textBuffer) {
                lock (this) {
                    EnsureMutableBuffers();

                    _buffers.Add(textBuffer);

                    InitBuffer(textBuffer);

                    _parser.ConnectErrorList(_currentProjEntry, textBuffer);
                }
            }

            internal void RemoveBuffer(ITextBuffer subjectBuffer) {
                lock (this) {
                    EnsureMutableBuffers();

                    UninitBuffer(subjectBuffer);

                    _buffers.Remove(subjectBuffer);

                    _parser.DisconnectErrorList(_currentProjEntry, subjectBuffer);
                }
            }

            private void UninitBuffer(ITextBuffer subjectBuffer) {
                if (_document != null) {
                    _document.EncodingChanged -= EncodingChanged;
                    _document = null;
                }
                subjectBuffer.Properties.RemoveProperty(typeof(IProjectEntry));
                subjectBuffer.Properties.RemoveProperty(typeof(BufferParser));
                subjectBuffer.ChangedLowPriority -= BufferChangedLowPriority;
            }

            private void InitBuffer(ITextBuffer buffer) {
                buffer.Properties.AddProperty(typeof(BufferParser), this);
                buffer.ChangedLowPriority += BufferChangedLowPriority;
                buffer.Properties.AddProperty(typeof(IProjectEntry), _currentProjEntry);

                if (_document != null) {
                    _document.EncodingChanged -= EncodingChanged;
                    _document = null;
                }
                if (buffer.Properties.TryGetProperty<ITextDocument>(typeof(ITextDocument), out _document) && _document != null) {
                    _document.EncodingChanged += EncodingChanged;
                }
            }

            private void EnsureMutableBuffers() {
                if (_buffers.IsReadOnly) {
                    _buffers = new List<ITextBuffer>(_buffers);
                }
            }

            internal void ReparseTimer(object unused) {
                RequeueWorker();
            }

            internal void ReparseWorker(object unused) {
                ITextSnapshot[] snapshots;
                lock (this) {
                    if (_parsing) {
                        NotReparsing();
                        Interlocked.Decrement(ref _parser._analysisPending);
                        return;
                    }

                    _parsing = true;
                    var buffers = Buffers;
                    snapshots = new ITextSnapshot[buffers.Length];
                    for (int i = 0; i < buffers.Length; i++) {
                        snapshots[i] = buffers[i].CurrentSnapshot;
                    }
                }

                _parser.ParseBuffers(this, snapshots);
                Interlocked.Decrement(ref _parser._analysisPending);

                lock (this) {
                    _parsing = false;
                    if (_requeue) {
                        RequeueWorker();
                    }
                    _requeue = false;
                }
            }

            /// <summary>
            /// Called when we decide we need to re-parse a buffer but before we start the buffer.
            /// </summary>
            internal void EnqueingEntry() {
                lock (this) {
                    IJsProjectEntry pyEntry = _currentProjEntry as IJsProjectEntry;
                    if (pyEntry != null) {
                        pyEntry.BeginParsingTree();
                    }
                }
            }

            /// <summary>
            /// Called when we race and are not actually re-parsing a buffer, balances the calls
            /// of BeginParsingTree when we aren't parsing.
            /// </summary>
            private void NotReparsing() {
                lock (this) {
                    IJsProjectEntry pyEntry = _currentProjEntry as IJsProjectEntry;
                    if (pyEntry != null) {
                        pyEntry.UpdateTree(null, null);
                    }
                }
            }

            internal void EncodingChanged(object sender, EncodingChangedEventArgs e) {
                lock (this) {
                    if (_parsing) {
                        // we are currently parsing, just reque when we complete
                        _requeue = true;
                        _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    } else {
                        Requeue();
                    }
                }
            }

            internal void BufferChangedLowPriority(object sender, TextContentChangedEventArgs e) {
                lock (this) {
                    // only immediately re-parse on line changes after we've seen a text change.

                    if (_parsing) {
                        // we are currently parsing, just reque when we complete
                        _requeue = true;
                        _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    } else if (LineAndTextChanges(e)) {
                        // user pressed enter, we should reque immediately
                        Requeue();
                    } else {
                        // parse if the user doesn't do anything for a while.
                        _textChange = IncludesTextChanges(e);
                        _timer.Change(ReparseDelay, Timeout.Infinite);
                    }
                }
            }

            internal void Requeue() {
                RequeueWorker();
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            private void RequeueWorker() {
                Interlocked.Increment(ref _parser._analysisPending);
                EnqueingEntry();
                ThreadPool.QueueUserWorkItem(ReparseWorker);
            }

            /// <summary>
            /// Used to track if we have line + text changes, just text changes, or just line changes.
            /// 
            /// If we have text changes followed by a line change we want to immediately reparse.
            /// If we have just text changes we want to reparse in ReparseDelay ms from the last change.
            /// If we have just repeated line changes (e.g. someone's holding down enter) we don't want to
            ///     repeatedly reparse, instead we want to wait ReparseDelay ms.
            /// </summary>
            private bool LineAndTextChanges(TextContentChangedEventArgs e) {
                if (_textChange) {
                    _textChange = false;
                    return e.Changes.IncludesLineChanges;
                }

                bool mixedChanges = false;
                if (e.Changes.IncludesLineChanges) {
                    mixedChanges = IncludesTextChanges(e);
                }

                return mixedChanges;
            }

            /// <summary>
            /// Returns true if the change incldues text changes (not just line changes).
            /// </summary>
            private static bool IncludesTextChanges(TextContentChangedEventArgs e) {
                bool mixedChanges = false;
                foreach (var change in e.Changes) {
                    if (change.OldText != String.Empty || change.NewText != Environment.NewLine) {
                        mixedChanges = true;
                        break;
                    }
                }
                return mixedChanges;
            }


            internal ITextDocument Document {
                get {
                    return _document;
                }
            }

            #region IDisposable
            private bool disposedValue = false;

            protected virtual void Dispose(bool disposing) {
                if (!disposedValue) {
                    if (disposing) {
                        _timer.Dispose();
                    }

                    disposedValue = true;
                }
            }

            public void Dispose() {
                Dispose(true);
            }
            #endregion
        }
    }
}
