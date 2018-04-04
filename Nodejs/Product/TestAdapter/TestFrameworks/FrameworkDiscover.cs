﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.NodejsTools.TestFrameworks;

namespace Microsoft.NodejsTools.TestAdapter.TestFrameworks
{
    public sealed class FrameworkDiscover
    {
        private readonly Dictionary<string, TestFramework> frameworks = new Dictionary<string, TestFramework>(StringComparer.OrdinalIgnoreCase);

        public static FrameworkDiscover Intance = new FrameworkDiscover();

        private FrameworkDiscover()
        {
            var directoryLoader = new TestFrameworkDirectories();
            var testFrameworkDirectories = directoryLoader.GetFrameworkDirectories();

            foreach (var directory in testFrameworkDirectories)
            {
                var fx = new TestFramework(directory);
                this.frameworks.Add(fx.Name, fx);
            }
        }

        public TestFramework Get(string frameworkName)
        {
            this.frameworks.TryGetValue(frameworkName, out var testFX);
            return testFX;
        }
    }
}
