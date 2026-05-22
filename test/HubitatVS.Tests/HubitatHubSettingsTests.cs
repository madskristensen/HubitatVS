using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatHubSettingsTests
    {
        [TestMethod]
        public void GetHubs_InvalidJson_ReturnsEmptyList()
        {
            var settings = new HubitatHubSettings { HubsJson = "{not-json" };

            var hubs = settings.GetHubs();

            Assert.AreEqual(0, hubs.Count);
        }

        [TestMethod]
        public void SetHubs_ThenGetHubs_RoundTripsValues()
        {
            var settings = new HubitatHubSettings();
            var source = new List<HubitatHubConfig>
            {
                new HubitatHubConfig
                {
                    Name = "Main",
                    Host = "hub.local",
                    Username = "user",
                    Password = "pwd"
                }
            };

            settings.SetHubs(source);
            var hubs = settings.GetHubs();

            Assert.AreEqual(1, hubs.Count);
            Assert.AreEqual("Main", hubs[0].Name);
            Assert.AreEqual("hub.local", hubs[0].Host);
            Assert.AreEqual("user", hubs[0].Username);
            Assert.AreEqual("pwd", hubs[0].Password);
        }

        [TestMethod]
        public void GetHubs_NullJson_ReturnsEmptyList()
        {
            var settings = new HubitatHubSettings { HubsJson = null! };

            var hubs = settings.GetHubs();

            Assert.AreEqual(0, hubs.Count);
        }
    }
}
