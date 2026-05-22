using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatConnectionTrackerTests
    {
        [TestInitialize]
        public void Initialize()
        {
            HubitatConnectionTracker.NotifyConfigChanged();
        }

        [TestMethod]
        public void SetConnected_RaisesEventsOnlyWhenStateChanges()
        {
            var hubsChangedCount = 0;
            var hubEvents = new List<string>();

            EventHandler hubsChanged = (_, __) => hubsChangedCount++;
            EventHandler<HubitatHubStateChangedEventArgs> hubStateChanged = (_, e) => hubEvents.Add(e.HubName ?? "<global>");

            HubitatConnectionTracker.HubsChanged += hubsChanged;
            HubitatConnectionTracker.HubStateChanged += hubStateChanged;

            try
            {
                HubitatConnectionTracker.SetConnected("Main", true);
                HubitatConnectionTracker.SetConnected("Main", true);
                HubitatConnectionTracker.SetConnected("Main", false);

                Assert.AreEqual(2, hubsChangedCount);
                CollectionAssert.AreEqual(new[] { "Main", "Main" }, hubEvents);
            }
            finally
            {
                HubitatConnectionTracker.HubsChanged -= hubsChanged;
                HubitatConnectionTracker.HubStateChanged -= hubStateChanged;
            }
        }

        [TestMethod]
        public void RemoveHub_RemovesStateAndRaisesEvents()
        {
            HubitatConnectionTracker.SetConnected("Main", true);

            var hubsChangedCount = 0;
            string? changedHub = null;
            EventHandler hubsChanged = (_, __) => hubsChangedCount++;
            EventHandler<HubitatHubStateChangedEventArgs> hubStateChanged = (_, e) => changedHub = e.HubName;

            HubitatConnectionTracker.HubsChanged += hubsChanged;
            HubitatConnectionTracker.HubStateChanged += hubStateChanged;

            try
            {
                HubitatConnectionTracker.RemoveHub("Main");

                Assert.AreEqual(1, hubsChangedCount);
                Assert.AreEqual("Main", changedHub);
                Assert.IsNull(HubitatConnectionTracker.GetConnected("Main"));
            }
            finally
            {
                HubitatConnectionTracker.HubsChanged -= hubsChanged;
                HubitatConnectionTracker.HubStateChanged -= hubStateChanged;
            }
        }

        [TestMethod]
        public async Task EnsureConnectionsTestedAsync_WithEmptyInput_DoesNothing()
        {
            await HubitatConnectionTracker.EnsureConnectionsTestedAsync(Array.Empty<HubitatHubConfig>());
            Assert.IsNull(HubitatConnectionTracker.GetConnected("Any"));
        }
    }
}
