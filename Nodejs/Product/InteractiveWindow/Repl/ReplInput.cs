// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.VisualStudio.Text;

namespace Microsoft.NodejsTools.Repl
{
    internal sealed class ReplSpan
    {
        private readonly object _span; // ITrackingSpan or string
        public readonly ReplSpanKind Kind;

        public ReplSpan(ITrackingSpan span, ReplSpanKind kind)
        {
            Debug.Assert(!kind.IsPrompt());
            _span = span;
            Kind = kind;
        }

        public ReplSpan(string litaral, ReplSpanKind kind)
        {
            _span = litaral;
            Kind = kind;
        }

        public object Span
        {
            get { return _span; }
        }

        public string Prompt
        {
            get { return (string)_span; }
        }

        public ITrackingSpan TrackingSpan
        {
            get { return (ITrackingSpan)_span; }
        }

        public int Length
        {
            get
            {
                return _span is string ? Prompt.Length : TrackingSpan.GetSpan(TrackingSpan.TextBuffer.CurrentSnapshot).Length;
            }
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Kind, _span);
        }
    }

    internal enum ReplSpanKind
    {
        None,
        /// <summary>
        /// The span represents output from the program (standard output)
        /// </summary>
        Output,
        /// <summary>
        /// The span represents a prompt for input of code.
        /// </summary>
        Prompt,
        /// <summary>
        /// The span represents a 2ndary prompt for more code.
        /// </summary>
        SecondaryPrompt,
        /// <summary>
        /// The span represents code inputted after a prompt or secondary prompt.
        /// </summary>
        Language,
        /// <summary>
        /// The span represents the prompt for input for standard input (non code input)
        /// </summary>
        StandardInputPrompt,
        /// <summary>
        /// The span represents the input for a standard input (non code input)
        /// </summary>
        StandardInput,
    }

    internal static class ReplSpanKindExtensions
    {
        internal static bool IsPrompt(this ReplSpanKind kind)
        {
            switch (kind)
            {
                case ReplSpanKind.Prompt:
                case ReplSpanKind.SecondaryPrompt:
                case ReplSpanKind.StandardInputPrompt:
                    return true;
                default:
                    return false;
            }
        }
    }
}
