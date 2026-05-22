using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatHubSelectionServiceTests
    {
        [TestInitialize]
        public void Initialize()
        {
            SetSessionHub(null);
        }

        [TestCleanup]
        public void Cleanup()
        {
            SetSessionHub(null);
        }

        [TestMethod]
        public async Task SelectHubAsync_NoHubs_ReturnsNull()
        {
            var selected = await HubitatHubSelectionService.SelectHubAsync(new List<HubitatHubConfig>());
            Assert.IsNull(selected);
        }

        [TestMethod]
        public async Task SelectHubAsync_SingleHub_ReturnsThatHub()
        {
            var hub = new HubitatHubConfig { Name = "Main", Host = "hub.local" };
            var selected = await HubitatHubSelectionService.SelectHubAsync(new List<HubitatHubConfig> { hub });

            Assert.AreSame(hub, selected);
        }

        [TestMethod]
        public async Task SelectHubAsync_WithSessionHubNameMatch_ReturnsListInstance()
        {
            var sessionHub = new HubitatHubConfig { Name = "Main", Host = "stale.local" };
            SetSessionHub(sessionHub);

            var listHub = new HubitatHubConfig { Name = "Main", Host = "live.local" };
            var selected = await HubitatHubSelectionService.SelectHubAsync(new List<HubitatHubConfig>
            {
                listHub,
                new HubitatHubConfig { Name = "Backup", Host = "backup.local" }
            });

            Assert.AreSame(listHub, selected);
        }

        private static void SetSessionHub(HubitatHubConfig? hub)
        {
            var field = typeof(HubitatHubPickerDialog).GetField("<SessionHub>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)!;
            field.SetValue(null, hub);
        }
    }
}
