using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatAdornmentFormattingTests
    {
        private static readonly MethodInfo FormatEntryMethod = typeof(HubitatAdornment)
            .GetMethod("FormatEntry", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo FormatRelativeDateMethod = typeof(HubitatAdornment)
            .GetMethod("FormatRelativeDate", BindingFlags.NonPublic | BindingFlags.Static)!;

        [TestMethod]
        public void FormatEntry_ConnectionErrorAndNotPublished_AreRenderedCorrectly()
        {
            var unavailable = new HubitatHubInfoEntry
            {
                HubName = "MainHub",
                ConnectionError = true,
                Kind = HubitatCodeKind.Driver
            };

            var notPublished = new HubitatHubInfoEntry
            {
                HubName = "MainHub",
                Found = false,
                Kind = HubitatCodeKind.Driver
            };

            Assert.AreEqual("MainHub · unavailable", FormatEntry(unavailable, showHubName: true));
            Assert.AreEqual("not published", FormatEntry(notPublished, showHubName: false));
        }

        [TestMethod]
        public void FormatEntry_IncludesPluralizedInstalledCountAndSyncState()
        {
            var app = new HubitatHubInfoEntry
            {
                HubName = "MainHub",
                Found = true,
                Kind = HubitatCodeKind.App,
                Version = 7,
                InstalledCount = 2,
                IsInSync = true
            };

            var driver = new HubitatHubInfoEntry
            {
                HubName = "MainHub",
                Found = true,
                Kind = HubitatCodeKind.Driver,
                Version = 3,
                InstalledCount = 1,
                IsInSync = false
            };

            var appText = FormatEntry(app, showHubName: true);
            var driverText = FormatEntry(driver, showHubName: false);

            StringAssert.Contains(appText, "v7");
            StringAssert.Contains(appText, "2 instances");
            StringAssert.Contains(appText, "in sync");

            StringAssert.Contains(driverText, "v3");
            StringAssert.Contains(driverText, "1 device");
            StringAssert.Contains(driverText, "differs");
        }

        [TestMethod]
        public void FormatRelativeDate_UsesExpectedBuckets()
        {
            Assert.AreEqual("today", FormatRelativeDate(DateTimeOffset.UtcNow.AddHours(-5)));
            Assert.AreEqual("yesterday", FormatRelativeDate(DateTimeOffset.UtcNow.AddHours(-30)));
            Assert.AreEqual("3d ago", FormatRelativeDate(DateTimeOffset.UtcNow.AddDays(-3.2)));
        }

        private static string FormatEntry(HubitatHubInfoEntry entry, bool showHubName)
            => (string)FormatEntryMethod.Invoke(null, new object[] { entry, showHubName })!;

        private static string FormatRelativeDate(DateTimeOffset date)
            => (string)FormatRelativeDateMethod.Invoke(null, new object[] { date })!;
    }
}
