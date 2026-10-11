// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using Microsoft.NodejsTools.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace NodejsTests
{
    [TestClass]
    public class NodejsGeneralOptionsTests
    {
        private const string PackageGuid = "FE8A8C3D-328A-476D-99F9-2A24B75F8C7F";
        private const string LegacyPageGuid = "EF677A38-0953-39C2-A228-2FBE8F8F082E";

        [TestInitialize]
        public void InitializeThreadHelper()
        {
            var contextField = typeof(ThreadHelper).GetField(
                "_joinableTaskContextCache",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (contextField.GetValue(null) == null)
            {
                _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                typeof(ThreadHelper)
                    .GetMethod("SetUIThread", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, null);
                contextField.SetValue(null, Activator.CreateInstance(contextField.FieldType));
            }
        }

        [TestMethod, Priority(0)]
        public void GeneralOptionsUseLegacyDefaults()
        {
            var page = new TestGeneralOptionsPage();

            page.LoadSettingsFromStorage();

            Assert.IsTrue(page.WaitOnAbnormalExit);
            Assert.IsFalse(page.WaitOnNormalExit);
            Assert.IsTrue(page.EditAndContinue);
        }

        [TestMethod, Priority(0)]
        public void GeneralOptionsRefreshUnmodifiedValues()
        {
            var page = new TestGeneralOptionsPage
            {
                StoredValues =
                {
                    ["WaitOnAbnormalExit"] = false,
                    ["WaitOnNormalExit"] = true,
                    ["EditAndContinue"] = false
                }
            };
            page.LoadSettingsFromStorage();

            page.StoredValues["WaitOnAbnormalExit"] = true;
            page.StoredValues["WaitOnNormalExit"] = false;
            page.StoredValues["EditAndContinue"] = true;
            page.RefreshSettingsFromStorage();

            Assert.IsTrue(page.WaitOnAbnormalExit);
            Assert.IsFalse(page.WaitOnNormalExit);
            Assert.IsTrue(page.EditAndContinue);
        }

        [TestMethod, Priority(0)]
        public void GeneralOptionsRefreshPreservesUnsavedConsumerValues()
        {
            var page = new TestGeneralOptionsPage
            {
                StoredValues =
                {
                    ["WaitOnAbnormalExit"] = true,
                    ["WaitOnNormalExit"] = true,
                    ["EditAndContinue"] = false
                }
            };
            page.LoadSettingsFromStorage();

            page.WaitOnAbnormalExit = false;
            page.StoredValues["WaitOnNormalExit"] = false;
            page.StoredValues["EditAndContinue"] = true;
            page.RefreshSettingsFromStorage();

            Assert.IsFalse(page.WaitOnAbnormalExit);
            Assert.IsFalse(page.WaitOnNormalExit);
            Assert.IsTrue(page.EditAndContinue);
        }

        [TestMethod, Priority(0)]
        public void GeneralOptionsSaveAllLegacyValues()
        {
            var page = new TestGeneralOptionsPage
            {
                WaitOnAbnormalExit = false,
                WaitOnNormalExit = true,
                EditAndContinue = false
            };

            page.SaveSettingsToStorage();

            Assert.AreEqual(false, page.SavedValues["WaitOnAbnormalExit"]);
            Assert.AreEqual(true, page.SavedValues["WaitOnNormalExit"]);
            Assert.AreEqual(false, page.SavedValues["EditAndContinue"]);
        }

        [TestMethod, Priority(0)]
        public void UnifiedSettingsManifestMatchesLegacyContract()
        {
            var manifestPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "UnifiedSettings",
                "NodejsGeneralOptions.registration.json");
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            var properties = (JObject)manifest["properties"];
            var categories = (JObject)manifest["categories"];

            Assert.AreEqual(3, properties.Count);
            AssertSetting(
                properties,
                "debugging.nodejs.general.waitOnAbnormalExit",
                "WaitOnAbnormalExit",
                true,
                "UnifiedSettings_WaitOnAbnormalExit");
            AssertSetting(
                properties,
                "debugging.nodejs.general.waitOnNormalExit",
                "WaitOnNormalExit",
                false,
                "UnifiedSettings_WaitOnNormalExit");
            AssertSetting(
                properties,
                "debugging.nodejs.general.editAndContinue",
                "EditAndContinue",
                true,
                "UnifiedSettings_EditAndContinue");

            Assert.AreEqual(2, categories.Count);
            Assert.AreEqual(
                string.Format("@114;{{{0}}}", PackageGuid),
                (string)categories["debugging.nodejs"]["title"]);
            Assert.AreEqual(
                string.Format("@115;{{{0}}}", PackageGuid),
                (string)categories["debugging.nodejs.general"]["title"]);
            Assert.AreEqual(
                LegacyPageGuid,
                (string)categories["debugging.nodejs.general"]["legacyOptionPageId"]);
        }

        [TestMethod, Priority(0)]
        public void UnifiedSettingsPackageRegistrationPointsToManifest()
        {
            var pkgdef = File.ReadAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnifiedSettings.pkgdef"));

            StringAssert.Contains(pkgdef, @"SettingsManifests\{" + PackageGuid + "}");
            StringAssert.Contains(
                pkgdef,
                @"""ManifestPath""=""$PackageFolder$\UnifiedSettings\NodejsGeneralOptions.registration.json""");
            StringAssert.Contains(pkgdef, @"""CacheTag""=qword:");
        }

        [TestMethod, Priority(0)]
        public void UnifiedSettingsHierarchyExcludesLegacyPlaceholder()
        {
            Assert.AreEqual(new Guid(LegacyPageGuid), typeof(NodejsGeneralOptionsPage).GUID);

            var generatedPkgdef = File.ReadAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Microsoft.NodejsTools.pkgdef"));
            var pageKey = @"[$RootKey$\ToolsOptionsPages\Node.js Tools\General]";
            var pageRegistration = GetPkgdefRegistration(
                generatedPkgdef,
                pageKey).ToUpperInvariant();

            Assert.IsFalse(string.IsNullOrEmpty(pageRegistration));
            StringAssert.Contains(
                pageRegistration,
                string.Format(@"""PAGE""=""{{{0}}}""", LegacyPageGuid));
            StringAssert.Contains(
                pageRegistration,
                @"""ISINUNIFIEDSETTINGS""=DWORD:00000001");
            var isInUnifiedSettings = pageRegistration.Contains(
                @"""ISINUNIFIEDSETTINGS""=DWORD:00000001");

            var manifestPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "UnifiedSettings",
                "NodejsGeneralOptions.registration.json");
            var categories = (JObject)JObject.Parse(
                File.ReadAllText(manifestPath))["categories"];
            var realCategoryCount = 0;
            foreach (var categoryProperty in categories.Properties())
            {
                var category = (JObject)categoryProperty.Value;
                if (string.Equals(
                    LegacyPageGuid,
                    (string)category["legacyOptionPageId"],
                    StringComparison.OrdinalIgnoreCase))
                {
                    ++realCategoryCount;
                }
            }

            // ToolsOptionsHierarchyMerger excludes legacy leaves carrying this registration value.
            var legacyPlaceholderCount = isInUnifiedSettings ? 0 : 1;
            Assert.AreEqual(1, realCategoryCount);
            Assert.AreEqual(0, legacyPlaceholderCount);
        }

        private static string GetPkgdefRegistration(string pkgdef, string key)
        {
            var registration = string.Empty;
            var searchStart = 0;

            while (searchStart < pkgdef.Length)
            {
                var keyStart = pkgdef.IndexOf(key, searchStart, StringComparison.Ordinal);
                if (keyStart < 0)
                {
                    break;
                }

                var keyEnd = pkgdef.IndexOf(
                    "\n[",
                    keyStart + key.Length,
                    StringComparison.Ordinal);
                if (keyEnd < 0)
                {
                    keyEnd = pkgdef.Length;
                }

                registration += pkgdef.Substring(keyStart, keyEnd - keyStart);
                searchStart = keyEnd;
            }

            return registration;
        }

        [TestMethod, Priority(0)]
        public void UnifiedSettingsResourcesResolveWithPackageProviderSyntax()
        {
            var manifestPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "UnifiedSettings",
                "NodejsGeneralOptions.registration.json");
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            var resourceTokens = new List<string>();

            AddDisplayResourceTokens((JObject)manifest["properties"], resourceTokens);
            AddDisplayResourceTokens((JObject)manifest["categories"], resourceTokens);

            Assert.AreEqual(5, resourceTokens.Count);

            var resourceManager = new ResourceManager(
                "VSPackage",
                typeof(NodejsGeneralOptionsPage).Assembly);

            foreach (var token in resourceTokens)
            {
                var fallbackSeparator = token.IndexOf('|');
                var resourceId = fallbackSeparator < 0
                    ? token
                    : token.Substring(0, fallbackSeparator);
                var providerSeparator = resourceId.IndexOf(';');

                Assert.AreEqual('@', resourceId[0], token);
                Assert.IsTrue(providerSeparator > 1, token);

                var resourceName = resourceId.Substring(1, providerSeparator - 1);
                Assert.IsTrue(
                    Guid.TryParse(resourceId.Substring(providerSeparator + 1), out var packageGuid),
                    token);
                Assert.AreEqual(new Guid(PackageGuid), packageGuid, token);

                var resolved = resourceManager.GetString(
                    resourceName,
                    CultureInfo.InvariantCulture);
                Assert.IsFalse(string.IsNullOrEmpty(resolved), token);
                Assert.AreNotEqual(token, resolved, token);
            }
        }

        private static void AddDisplayResourceTokens(
            JObject definitions,
            ICollection<string> resourceTokens)
        {
            foreach (var definitionProperty in definitions.Properties())
            {
                var definition = (JObject)definitionProperty.Value;
                foreach (var fieldName in new[] { "title", "description" })
                {
                    var token = (string)definition[fieldName];
                    if (token != null)
                    {
                        resourceTokens.Add(token);
                    }
                }
            }
        }

        private static void AssertSetting(
            JObject properties,
            string moniker,
            string legacyName,
            bool defaultValue,
            string resourceName)
        {
            var property = (JObject)properties[moniker];
            Assert.IsNotNull(property, moniker);
            Assert.AreEqual("boolean", (string)property["type"], moniker);
            Assert.AreEqual(defaultValue, (bool)property["default"], moniker);
            Assert.AreEqual(
                string.Format("@{0};{{{1}}}", resourceName, PackageGuid),
                (string)property["title"],
                moniker);

            var migration = (JObject)property["migration"]["custom"];
            Assert.AreEqual("full", (string)migration["mode"], moniker);
            Assert.AreEqual(1, ((JArray)migration["inputs"]).Count, moniker);
            Assert.AreEqual(
                "VsUserSettingsRegistry",
                (string)migration["inputs"][0]["store"],
                moniker);
            Assert.AreEqual(
                @"NodejsTools\Options\General\" + legacyName,
                (string)migration["inputs"][0]["path"],
                moniker);

            var map = (JArray)migration["map"];
            Assert.AreEqual(2, map.Count, moniker);
            Assert.AreEqual(true, (bool)map[0]["result"], moniker);
            Assert.AreEqual("True", (string)map[0]["matches"][0], moniker);
            Assert.AreEqual(false, (bool)map[1]["result"], moniker);
            Assert.AreEqual("False", (string)map[1]["matches"][0], moniker);
        }

        private sealed class TestGeneralOptionsPage : NodejsGeneralOptionsPage
        {
            internal IDictionary<string, bool> StoredValues { get; } =
                new Dictionary<string, bool>();

            internal IDictionary<string, bool> SavedValues { get; } =
                new Dictionary<string, bool>();

            internal override bool? LoadBool(string name)
            {
                return this.StoredValues.TryGetValue(name, out var value)
                    ? value
                    : (bool?)null;
            }

            internal override void SaveBool(string name, bool value)
            {
                this.SavedValues[name] = value;
            }
        }
    }
}
