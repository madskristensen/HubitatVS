using System;
using System.Threading;
using System.Threading.Tasks;
using HubitatVS.OpenFolder;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatOpenFolderTests
    {
        [TestMethod]
        public async Task FileContextProvider_OnlyReturnsContextForGroovyFiles()
        {
            var provider = new HubitatFileContextProvider();

            var groovy = await provider.GetContextsForFileAsync("C:\\repo\\device.groovy", CancellationToken.None);
            var text = await provider.GetContextsForFileAsync("C:\\repo\\notes.txt", CancellationToken.None);

            Assert.AreEqual(1, groovy.Count);
            Assert.AreEqual(0, text.Count);
        }

        [TestMethod]
        public async Task FileContextActionProvider_ReturnsPublishAndCompareActions()
        {
            var provider = new HubitatFileContextActionProvider();
            var context = new FileContext(
                new Guid(HubitatContextTypes.ProviderTypeGuidString),
                HubitatContextTypes.ContextTypeGuid,
                "C:\\repo\\device.groovy",
                new[] { "C:\\repo\\device.groovy" },
                string.Empty,
                null);

            var actions = await provider.GetActionsAsync("C:\\repo\\device.groovy", context, CancellationToken.None);

            Assert.AreEqual(2, actions.Count);
            Assert.AreEqual("Publish to Hubitat…", actions[0].DisplayName);
            Assert.AreEqual("Compare with Hub Version…", actions[1].DisplayName);
        }
    }
}
