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
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.NodejsTools.Classifier;

namespace Microsoft.NodejsTools.Intellisense {
    /// <summary>
    /// Parses an expression in reverse to get the experssion we need to
    /// analyze for completion, quick info, or signature help.
    /// </summary>
    class ReverseExpressionParser : IEnumerable<ClassificationSpan> {
        private readonly ITextSnapshot _snapshot;
        private readonly ITextBuffer _buffer;
        private readonly ITrackingSpan _span;
        private IList<ClassificationSpan> _tokens;
        private ITextSnapshotLine _curLine;
        private NodejsClassifier _classifier;
        private static readonly string[] _assignOperators = new[] {
            "=" ,  "+=" ,  "-=" ,  "/=" ,  "%=" ,  "^=" ,  "*=" ,  "//=" ,  "&=" ,  "|=" ,  ">>=" ,  "<<=" ,  "**="
        };
        private static HashSet<string> _stmtKeywords = new HashSet<string>() { 
            "debugger", "var", "if", "for", "do", "while", "continue", "break",
            "return", "with", "switch", "throw", "try", "else",
        };


        public ReverseExpressionParser(ITextSnapshot snapshot, ITextBuffer buffer, ITrackingSpan span) {
            _snapshot = snapshot;
            _buffer = buffer;
            _span = span;

            var loc = span.GetSpan(snapshot);
            var line = _curLine = snapshot.GetLineFromPosition(loc.Start);

            var targetSpan = new Span(line.Start.Position, span.GetEndPoint(snapshot).Position - line.Start.Position);
            _tokens = Classifier.GetClassificationSpans(new SnapshotSpan(snapshot, targetSpan));
        }

        public SnapshotSpan? GetExpressionRange(bool forCompletion = true, int nesting = 0) {
            int dummy;
            SnapshotPoint? dummyPoint;
            string lastKeywordArg;
            bool isParameterName;
            return GetExpressionRange(nesting, out dummy, out dummyPoint, out lastKeywordArg, out isParameterName, forCompletion);
        }

        internal static IEnumerator<ClassificationSpan> ReverseClassificationSpanEnumerator(NodejsClassifier classifier, SnapshotPoint startPoint) {
            var startLine = startPoint.GetContainingLine();
            int curLine = startLine.LineNumber;
            var tokens = classifier.GetClassificationSpans(new SnapshotSpan(startLine.Start, startPoint));

            for (; ; ) {
                for (int i = tokens.Count - 1; i >= 0; i--) {
                    yield return tokens[i];
                }

                // indicate the line break
                yield return null;

                curLine--;
                if (curLine >= 0) {
                    var prevLine = startPoint.Snapshot.GetLineFromLineNumber(curLine);
                    tokens = classifier.GetClassificationSpans(prevLine.Extent);
                } else {
                    break;
                }
            }
        }

        /// <summary>
        /// Walks backwards to figure out if we're a parameter name which comes after a (     
        /// </summary>
        private bool IsParameterNameOpenParen(IEnumerator<ClassificationSpan> enumerator) {
            if (MoveNextSkipExplicitNewLines(enumerator)) {
                if (enumerator.Current.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Identifier)) {
                    if (MoveNextSkipExplicitNewLines(enumerator) &&
                        enumerator.Current.ClassificationType == Classifier.Provider.Keyword &&
                        enumerator.Current.Span.GetText() == "function") {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Walks backwards to figure out if we're a parameter name which comes after a comma.
        /// </summary>
        private bool IsParameterNameComma(IEnumerator<ClassificationSpan> enumerator) {
            int groupingLevel = 1;

            while (MoveNextSkipExplicitNewLines(enumerator)) {
                if (enumerator.Current.ClassificationType == _classifier.Provider.Keyword) {
                    if (enumerator.Current.Span.GetText() == "function" && groupingLevel == 0) {
                        return true;
                    }

                    if (_stmtKeywords.Contains(enumerator.Current.Span.GetText())) {
                        return false;
                    }
                }
                if (enumerator.Current.IsOpenGrouping()) {
                    groupingLevel--;
                    if (groupingLevel == 0) {
                        return IsParameterNameOpenParen(enumerator);
                    }
                } else if (enumerator.Current.IsCloseGrouping()) {
                    groupingLevel++;
                }
            }

            return false;
        }


        private bool MoveNextSkipExplicitNewLines(IEnumerator<ClassificationSpan> enumerator) {
            while (enumerator.MoveNext()) {
                if (enumerator.Current == null) {
                    while (enumerator.Current == null) {
                        if (!enumerator.MoveNext()) {
                            return false;
                        }
                    }
                    if (!IsExplicitLineJoin(enumerator.Current)) {
                        return true;
                    }
                } else {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the range of the expression to the left of our starting span.  
        /// </summary>
        /// <param name="nesting">1 if we have an opening parenthesis for sig completion</param>
        /// <param name="paramIndex">The current parameter index.</param>
        /// <returns></returns>
        public SnapshotSpan? GetExpressionRange(int nesting, out int paramIndex, out SnapshotPoint? sigStart, out string lastKeywordArg, out bool isParameterName, bool forCompletion = true, bool forSignatureHelp = false) {
            SnapshotSpan? start = null;
            paramIndex = 0;
            sigStart = null;
            bool nestingChanged = false, lastTokenWasCommaOrOperator = true, lastTokenWasKeywordArgAssignment = false;
            int otherNesting = 0;
            bool isSigHelp = nesting != 0;
            isParameterName = false;
            lastKeywordArg = null;

            ClassificationSpan lastToken = null;
            // Walks backwards over all the lines
            var enumerator = ReverseClassificationSpanEnumerator(_classifier, _span.GetSpan(_snapshot).End);
            if (enumerator.MoveNext()) {
                if (enumerator.Current != null && enumerator.Current.ClassificationType == this._classifier.Provider.StringLiteral) {
                    return enumerator.Current.Span;
                }

                lastToken = enumerator.Current;
                while (ShouldSkipAsLastToken(lastToken, forCompletion) && enumerator.MoveNext()) {
                    // skip trailing new line if the user is hovering at the end of the line
                    if (lastToken == null && (nesting + otherNesting == 0)) {
                        // new line out of a grouping...
                        return _span.GetSpan(_snapshot);
                    }
                    lastToken = enumerator.Current;
                }

                bool lastNewLine = false;
                // Walk backwards over the tokens in the current line
                do {
                    var token = enumerator.Current;

                    if (token == null) {
                        // new line
                        lastNewLine = true;
                        continue;
                    } else if (lastNewLine && !lastTokenWasCommaOrOperator && otherNesting == 0) {
                        break;
                    }

                    lastNewLine = false;

                    var text = token.Span.GetText();
                    if (text == "(") {
                        if (nesting != 0) {
                            nesting--;
                            nestingChanged = true;
                            if (nesting == 0) {
                                if (sigStart == null) {
                                    sigStart = token.Span.Start;
                                }
                            }
                        } else {
                            if (start == null && !forCompletion) {
                                // hovering directly over an open paren, don't provide a tooltip
                                return null;
                            }

                            // figure out if we're a parameter definition
                            isParameterName = IsParameterNameOpenParen(enumerator);
                            break;
                        }
                        lastTokenWasCommaOrOperator = true;
                        lastTokenWasKeywordArgAssignment = false;
                    } else if (token.IsOpenGrouping()) {
                        if (otherNesting != 0) {
                            otherNesting--;
                        } else {
                            if (nesting == 0) {
                                if (start == null) {
                                    return null;
                                }
                                break;
                            }
                            paramIndex = 0;
                        }
                        nestingChanged = true;
                        lastTokenWasCommaOrOperator = true;
                        lastTokenWasKeywordArgAssignment = false;
                    } else if (text == ")") {
                        nesting++;
                        nestingChanged = true;
                        lastTokenWasCommaOrOperator = true;
                        lastTokenWasKeywordArgAssignment = false;
                    } else if (token.IsCloseGrouping()) {
                        otherNesting++;
                        nestingChanged = true;
                        lastTokenWasCommaOrOperator = true;
                        lastTokenWasKeywordArgAssignment = false;
                    } else if ((token.ClassificationType == Classifier.Provider.Keyword &&
                                text != "this" && text != "get" && text != "set" && text != "delete") ||
                               token.ClassificationType == Classifier.Provider.Operator) {
                        if (forCompletion && text == "new") {
                            if (!forSignatureHelp) {
                                start = token.Span;
                            }
                            break;
                        }

                        lastTokenWasKeywordArgAssignment = false;

                        if (nesting == 0 && otherNesting == 0) {
                            if (start == null) {
                                // http://pytools.codeplex.com/workitem/560
                                // yield_value = 42
                                // function *f() {
                                //     yield<ctrl-space>
                                //     yield <ctrl-space>
                                // }
                                // 
                                // If we're next to the keyword, just return the keyword.
                                // If we're after the keyword, return the span of the text proceeding
                                //  the keyword so we can complete after it.
                                // 
                                // Also repros with "return <ctrl-space>" or "print <ctrl-space>" both
                                // of which we weren't reporting completions for before
                                if (forCompletion) {
                                    if (token.Span.IntersectsWith(_span.GetSpan(_snapshot))) {
                                        return token.Span;
                                    } else {
                                        return _span.GetSpan(_snapshot);
                                    }
                                }

                                // hovering directly over a keyword, don't provide a tooltip
                                return null;
                            } else if ((nestingChanged || forCompletion) && token.ClassificationType == Classifier.Provider.Keyword && text == "function") {
                                return null;
                            }
                            break;
                        } else if ((token.ClassificationType == Classifier.Provider.Keyword &&
                            _stmtKeywords.Contains(text)) ||
                            (token.ClassificationType == Classifier.Provider.Operator && IsAssignmentOperator(text))) {
                            if (start == null || (nestingChanged && nesting != 0 || otherNesting != 0)) {
                                return null;
                            } else {
                                break;
                            }
                        }
                        lastTokenWasCommaOrOperator = true;
                    } else if (token.ClassificationType == Classifier.Provider.DotClassification) {
                        lastTokenWasCommaOrOperator = true;
                        lastTokenWasKeywordArgAssignment = false;
                    } else if (token.ClassificationType == Classifier.Provider.CommaClassification) {
                        lastTokenWasCommaOrOperator = true;
                        lastTokenWasKeywordArgAssignment = false;
                        if (nesting == 0 && otherNesting == 0) {
                            if (start == null && !forCompletion) {
                                return null;
                            }
                            isParameterName = IsParameterNameComma(enumerator);
                            break;
                        } else if (nesting == 1 && otherNesting == 0 && sigStart == null) {
                            paramIndex++;
                        }
                    } else if (token.ClassificationType == Classifier.Provider.Comment) {
                        // Do not update start - if we bail out on the next token we see, we don't want to
                        // count the comment as part of the expression, either.
                        continue;
                    } else if (!lastTokenWasCommaOrOperator) {
                        break;
                    } else {
                        if (lastTokenWasKeywordArgAssignment &&
                            token.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Identifier) &&
                            lastKeywordArg == null) {
                            if (paramIndex == 0) {
                                lastKeywordArg = text;
                            } else {
                                lastKeywordArg = String.Empty;
                            }
                        }
                        lastTokenWasCommaOrOperator = false;
                    }

                    start = token.Span;
                } while (enumerator.MoveNext());
            }

            if (start.HasValue && lastToken != null && (lastToken.Span.End.Position - start.Value.Start.Position) >= 0) {
                return new SnapshotSpan(
                    Snapshot,
                    new Span(
                        start.Value.Start.Position,
                        lastToken.Span.End.Position - start.Value.Start.Position
                    )
                );
            }

            return null;
        }

        private static bool IsAssignmentOperator(string text) {
            return ((IList<string>)_assignOperators).Contains(text);
        }

        internal static bool IsExplicitLineJoin(ClassificationSpan cur) {
            if (cur != null && cur.ClassificationType.IsOfType(NodejsPredefinedClassificationTypeNames.Operator)) {
                var text = cur.Span.GetText();
                return text == "\\\r\n" || text == "\\\r" || text == "\n";
            }
            return false;
        }

        /// <summary>
        /// Returns true if we should skip this token when it's the last token that the user hovers over.  Currently true
        /// for new lines and dot classifications.  
        /// </summary>
        private bool ShouldSkipAsLastToken(ClassificationSpan lastToken, bool forCompletion) {
            return lastToken == null || (
                (lastToken.ClassificationType.Classification == PredefinedClassificationTypeNames.WhiteSpace &&
                    (lastToken.Span.GetText() == "\r\n" || lastToken.Span.GetText() == "\n" || lastToken.Span.GetText() == "\r")) ||
                    (lastToken.ClassificationType == Classifier.Provider.DotClassification && !forCompletion));
        }

        public NodejsClassifier Classifier {
            get { return _classifier ?? (_classifier = (NodejsClassifier)_buffer.Properties.GetProperty(typeof(NodejsClassifier))); }
        }

        public ITextSnapshot Snapshot {
            get { return _snapshot; }
        }

        public ITextBuffer Buffer {
            get { return _buffer; }
        }

        public ITrackingSpan Span {
            get { return _span; }
        }

        /// <summary>
        /// Tokens for the current line
        /// </summary>
        public IList<ClassificationSpan> Tokens {
            get { return _tokens; }
            set { _tokens = value; }
        }

        public ITextSnapshotLine CurrentLine {
            get { return _curLine; }
            set { _curLine = value; }
        }

        public IEnumerator<ClassificationSpan> GetEnumerator() {
            return ReverseClassificationSpanEnumerator(_classifier, _span.GetSpan(_snapshot).End);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return ReverseClassificationSpanEnumerator(_classifier, _span.GetSpan(_snapshot).End);
        }

        internal bool IsInGrouping() {
            // We assume that groupings are correctly matched and keep a simple
            // nesting count.
            int nesting = 0;
            bool maybeFunction = false, maybeParams = false;
            foreach (var token in this) {
                if (token == null) {
                    continue;
                }

                if (token.IsCloseGrouping()) {
                    if (++nesting == 0 && maybeFunction) {
                        if (token.Span.GetText() == ")") {
                            maybeParams = true;
                        } else {
                            maybeFunction = false;
                        }
                    }
                } else if (token.IsOpenGrouping()) {
                    if (nesting-- == 0) {
                        if (token.Span.GetText() != "{") {
                            if (maybeParams && token.Span.GetText() == "(") {
                                maybeFunction = maybeParams = false;
                            } else {
                                // might be an object literal, might be a function...
                                return true;
                            }
                        } else {
                            maybeFunction = true;
                        }
                    }
                } else if (token.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Keyword) &&
                    _stmtKeywords.Contains(token.Span.GetText()) || token.Span.GetText() == "function") {
                    return false;
                }
            }
            return false;
        }
    }
}
