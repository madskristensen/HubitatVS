using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatGroovyAnalyzerTests
    {
        // Based on real-world structure from hubitat-drivers/drivers/fully-kiosk/fully-kiosk.groovy
        private const string FullyKioskDriverSample = @"// version: 0.6.0
metadata {
    definition (name: ""Fully Kiosk Browser"", namespace: ""mads"", author: ""Mads Kristensen"") {
        capability ""Actuator""
        capability ""Refresh""
        attribute ""currentPageUrl"", ""String""
        command ""screenOn""
    }
}
";

        // Based on real-world structure from hubitat-drivers/apps/away-lights/away-lights.groovy
        private const string AwayLightsAppSample = @"definition(
    name: ""Away Lights"",
    namespace: ""mads"",
    author: ""Mads Kristensen"",
    description: ""Turns selected lights on during Away mode.""
)

preferences {
    page(name: ""mainPage"", title: ""Away Lights"", install: true, uninstall: true)
}
";

        [TestMethod]
        public void AnalyzeSource_ClassifiesDriverAndExtractsMetadata_ForDriverStyleGroovy()
        {
            var candidate = HubitatGroovyAnalyzer.AnalyzeSource(FullyKioskDriverSample, @"C:\samples\fully-kiosk.groovy");

            Assert.AreEqual(HubitatCodeKind.Driver, candidate.Kind);
            Assert.AreEqual("Fully Kiosk Browser", candidate.DisplayName);
            Assert.AreEqual("mads", candidate.NamespaceName);
            Assert.AreEqual("Mads Kristensen", candidate.Author);
            Assert.AreEqual("0.6.0", candidate.Version);
            Assert.IsTrue(candidate.Warnings.Count == 0, $"Unexpected warnings: {string.Join(", ", candidate.Warnings)}");
        }

        [TestMethod]
        public void AnalyzeSource_ClassifiesApp_ForDefinitionPlusPreferencesPage()
        {
            var candidate = HubitatGroovyAnalyzer.AnalyzeSource(AwayLightsAppSample, @"C:\samples\away-lights.groovy");

            Assert.AreEqual(HubitatCodeKind.App, candidate.Kind);
            Assert.AreEqual("Away Lights", candidate.DisplayName);
            Assert.AreEqual("mads", candidate.NamespaceName);
            Assert.AreEqual("Mads Kristensen", candidate.Author);
            Assert.IsTrue(candidate.Warnings.Count == 0, $"Unexpected warnings: {string.Join(", ", candidate.Warnings)}");
        }

        [TestMethod]
        public void AnalyzeSource_ClassifiesApp_WhenMetadataDefinitionContainsAppHints()
        {
            const string metadataApp = @"metadata {
    definition(name: ""Occupancy Mode Manager"", namespace: ""mads"", author: ""Mads Kristensen"") {
    }
}

preferences {
    page(name: ""mainPage"", title: ""Occupancy Mode Manager"", install: true, uninstall: true)
}
";

            var candidate = HubitatGroovyAnalyzer.AnalyzeSource(metadataApp, @"C:\samples\occupancy-mode-manager.groovy");

            Assert.AreEqual(HubitatCodeKind.App, candidate.Kind);
        }

        [TestMethod]
        public void AnalyzeSource_ClassifiesDriver_WhenMetadataHasCapabilitiesButNoDefinition()
        {
            const string definitionlessDriver = @"metadata {
    capability ""Switch""
    capability ""Refresh""
}
";

            var candidate = HubitatGroovyAnalyzer.AnalyzeSource(definitionlessDriver, @"C:\samples\definitionless-driver.groovy");

            Assert.AreEqual(HubitatCodeKind.Driver, candidate.Kind);
            CollectionAssert.Contains(candidate.Warnings.ToList(), "No Hubitat definition() block found.");
        }

        [TestMethod]
        public void AnalyzeSource_ClassifiesUnknown_AndReportsMissingMetadata_WhenNoHubitatMarkersExist()
        {
            const string unknownScript = @"def helper() {
    return ""hello""
}
";

            var candidate = HubitatGroovyAnalyzer.AnalyzeSource(unknownScript, @"C:\samples\helper.groovy");

            Assert.AreEqual(HubitatCodeKind.Unknown, candidate.Kind);
            CollectionAssert.Contains(candidate.Warnings.ToList(), "No Hubitat definition() block found.");
            CollectionAssert.Contains(candidate.Warnings.ToList(), "Could not determine whether this file is a Hubitat app or driver.");
            CollectionAssert.Contains(candidate.Warnings.ToList(), "Missing namespace.");
            CollectionAssert.Contains(candidate.Warnings.ToList(), "Missing author.");
        }

        [TestMethod]
        public void ScanFiles_DeduplicatesExistingFiles_AndSortsByFileName()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "HubitatVS.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var appPath = Path.Combine(tempRoot, "a-away-lights.groovy");
                var driverPath = Path.Combine(tempRoot, "b-fully-kiosk.groovy");

                File.WriteAllText(appPath, AwayLightsAppSample);
                File.WriteAllText(driverPath, FullyKioskDriverSample);

                var candidates = HubitatGroovyAnalyzer.ScanFiles(new List<string>
                {
                    driverPath,
                    appPath,
                    appPath.ToUpperInvariant(),
                    Path.Combine(tempRoot, "missing-file.groovy")
                });

                Assert.AreEqual(2, candidates.Count);
                Assert.AreEqual("a-away-lights.groovy", candidates[0].FileName);
                Assert.AreEqual("b-fully-kiosk.groovy", candidates[1].FileName);
                Assert.AreEqual(HubitatCodeKind.App, candidates[0].Kind);
                Assert.AreEqual(HubitatCodeKind.Driver, candidates[1].Kind);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }
    }
}
