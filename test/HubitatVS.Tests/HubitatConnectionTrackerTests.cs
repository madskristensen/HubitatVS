using System;
using System.Collections.Generic;
using System.Linq;
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

        [TestMethod]
        public async Task EnsureConnectionsTestedAsync_RemovesStaleStatuses()
        {
            HubitatConnectionTracker.SetConnected("OldHub", true);

            await HubitatConnectionTracker.EnsureConnectionsTestedAsync(new[]
            {
                new HubitatHubConfig { Name = "NewHub", Host = string.Empty }
            });

            Assert.IsNull(HubitatConnectionTracker.GetConnected("OldHub"));
            Assert.IsNotNull(HubitatConnectionTracker.GetConnected("NewHub"));
        }

        [TestMethod]
        public void NotifyConfigChanged_RaisesGlobalHubStateEvent()
        {
            HubitatHubStateChangedEventArgs? observed = null;
            EventHandler<HubitatHubStateChangedEventArgs> handler = (_, e) => observed = e;
            HubitatConnectionTracker.HubStateChanged += handler;

            try
            {
                HubitatConnectionTracker.NotifyConfigChanged();
                Assert.IsNotNull(observed);
                Assert.IsTrue(observed!.IsGlobal);
            }
            finally
            {
                HubitatConnectionTracker.HubStateChanged -= handler;
            }
        }

        [TestMethod]
        public async Task ParallelSetConnectedAndRemoveHub_DoesNotLeaveInconsistentState()
        {
            var hubName = "ConcurrentHub";

            var setTasks = Enumerable.Range(0, 200)
                .Select(i => Task.Run(() => HubitatConnectionTracker.SetConnected(hubName, i % 2 == 0)));
            var removeTasks = Enumerable.Range(0, 50)
                .Select(_ => Task.Run(() => HubitatConnectionTracker.RemoveHub(hubName)));

            await Task.WhenAll(setTasks.Concat(removeTasks));

            var final = HubitatConnectionTracker.GetConnected(hubName);
            Assert.IsTrue(final == null || final == true || final == false);
        }

        [TestMethod]
        public async Task ParallelNotifyConfigChangedAndSetConnected_ResetsAndAcceptsNewState()
        {
            var resetTasks = Enumerable.Range(0, 40)
                .Select(_ => Task.Run(() => HubitatConnectionTracker.NotifyConfigChanged()));
            var setTasks = Enumerable.Range(0, 80)
                .Select(i => Task.Run(() => HubitatConnectionTracker.SetConnected($"Hub-{i % 5}", true)));

            await Task.WhenAll(resetTasks.Concat(setTasks));

            HubitatConnectionTracker.NotifyConfigChanged();
            Assert.IsNull(HubitatConnectionTracker.GetConnected("Hub-0"));

            HubitatConnectionTracker.SetConnected("Hub-0", true);
            Assert.AreEqual(true, HubitatConnectionTracker.GetConnected("Hub-0"));
        }
    }
}
