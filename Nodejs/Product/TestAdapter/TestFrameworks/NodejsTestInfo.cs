// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;

namespace Microsoft.NodejsTools.TestAdapter.TestFrameworks
{
    internal class NodejsTestInfo
    {
        public NodejsTestInfo(string fullyQualifiedName, string modulePath)
        {
            var parts = fullyQualifiedName.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length != 3)
            {
                throw new ArgumentException("Invalid fully qualified test name");
            }

            this.ModulePath = modulePath;
            this.ModuleName = parts[0];
            this.TestName = parts[1];
            this.TestFramework = parts[2];
        }

        public NodejsTestInfo(string modulePath, string testName, string testFramework, int line, int column)
        {
            var moduleName = Path.GetFileNameWithoutExtension(modulePath);
            var hash = GetHash(modulePath);

            this.ModulePath = modulePath;
            this.ModuleName = $"{moduleName}[{hash}]";
            this.TestName = testName;
            this.TestFramework = testFramework;
            this.SourceLine = line;
            this.SourceColumn = column;
        }

        private string GetHash(string filePath)
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = Hash.GetFnvHashCode(stream);
                    return Encoding.UTF8.GetString(hash);
                }
            }
            catch (IOException)
            {
                return "FILE_NOT_FOUND";
            }
        }

        public string FullyQualifiedName => string.Join("::", this.ModuleName, this.TestName, this.TestFramework);

        public string ModulePath { get; }

        public string ModuleName { get; }

        public string TestName { get; }

        public string TestFramework { get; }

        public int SourceLine { get; }

        public int SourceColumn { get; }
    }
}
