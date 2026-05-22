using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatConnectionsViewModelStatusTests
    {
        private static readonly MethodInfo ApplyTrackerStatusMethod = typeof(HubitatConnectionsViewModel)
            .GetMethod("ApplyTrackerStatus", BindingFlags.NonPublic | BindingFlags.Static)!;

        [TestInitialize]
        public void Initialize()
        {
            HubitatConnectionTracker.NotifyConfigChanged();
            ResetTestingSet();
        }

        [TestMethod]
        public void ApplyTrackerStatus_SetsTesting_WhenHubIsBeingTested()
        {
            var vm = CreateVm("Main");
            AddTestingHub("Main");

            ApplyTrackerStatusMethod.Invoke(null, new object[] { vm });

            Assert.AreEqual(ConnectionStatus.Testing, vm.Status);
            Assert.AreEqual("Testing…", vm.StatusMessage);
        }

        [TestMethod]
        public void ApplyTrackerStatus_SetsConnected_WhenTrackerReportsTrue()
        {
            var vm = CreateVm("Main");
            HubitatConnectionTracker.SetConnected("Main", true);

            ApplyTrackerStatusMethod.Invoke(null, new object[] { vm });

            Assert.AreEqual(ConnectionStatus.Connected, vm.Status);
            Assert.AreEqual("Connected", vm.StatusMessage);
        }

        [TestMethod]
        public void ApplyTrackerStatus_SetsDisconnected_WhenTrackerReportsFalse()
        {
            var vm = CreateVm("Main");
            HubitatConnectionTracker.SetConnected("Main", false);

            ApplyTrackerStatusMethod.Invoke(null, new object[] { vm });

            Assert.AreEqual(ConnectionStatus.Disconnected, vm.Status);
            Assert.AreEqual("Failed", vm.StatusMessage);
        }

        [TestMethod]
        public void ApplyTrackerStatus_SetsUnknown_WhenNoTrackerDataExists()
        {
            var vm = CreateVm("Main");

            ApplyTrackerStatusMethod.Invoke(null, new object[] { vm });

            Assert.AreEqual(ConnectionStatus.Unknown, vm.Status);
            Assert.AreEqual("Not tested", vm.StatusMessage);
        }

        private static HubitatHubViewModel CreateVm(string name)
            => new HubitatHubViewModel(new HubitatHubConfig { Name = name, Host = "hub.local" });

        private static void AddTestingHub(string hubName)
        {
            var field = typeof(HubitatConnectionTracker).GetField("_testing", BindingFlags.NonPublic | BindingFlags.Static)!;
            var set = (System.Collections.Generic.HashSet<string>)field.GetValue(null)!;
            set.Add(hubName);
        }

        private static void ResetTestingSet()
        {
            var field = typeof(HubitatConnectionTracker).GetField("_testing", BindingFlags.NonPublic | BindingFlags.Static)!;
            var set = (System.Collections.Generic.HashSet<string>)field.GetValue(null)!;
            set.Clear();
        }
    }
}
