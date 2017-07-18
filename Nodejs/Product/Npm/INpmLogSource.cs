// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;

namespace Microsoft.NodejsTools.Npm
{
    public interface INpmLogSource
    {
        event EventHandler CommandStarted;
        event EventHandler<NpmLogEventArgs> OutputLogged;
        event EventHandler<NpmLogEventArgs> ErrorLogged;
        event EventHandler<NpmExceptionEventArgs> ExceptionLogged;
        event EventHandler<NpmCommandCompletedEventArgs> CommandCompleted;
    }
}
