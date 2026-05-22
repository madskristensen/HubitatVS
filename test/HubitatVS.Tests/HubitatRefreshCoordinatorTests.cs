using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatRefreshCoordinatorTests
    {
        [TestMethod]
        public void Ctor_NullWorker_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new HubitatRefreshCoordinator(null!));
        }

        [TestMethod]
        public void CurrentVersion_DefaultsToZero()
        {
            using var coordinator = new HubitatRefreshCoordinator((_, __) => Task.CompletedTask);
            Assert.AreEqual(0, coordinator.CurrentVersion);
        }

        [TestMethod]
        public void Dispose_IsIdempotent()
        {
            using var coordinator = new HubitatRefreshCoordinator((_, __) => Task.CompletedTask);
            coordinator.Dispose();
            coordinator.Dispose();
        }

        [TestMethod]
        public async Task RunAsync_CallsErrorHandler_ForNonCancellationExceptions()
        {
            var errorCount = 0;
            using var coordinator = new HubitatRefreshCoordinator(
                (_, __) => throw new InvalidOperationException("boom"),
                _ => Interlocked.Increment(ref errorCount));

            await InvokeRunAsync(coordinator, refreshVersion: 1, CancellationToken.None);

            Assert.AreEqual(1, Volatile.Read(ref errorCount));
        }

        [TestMethod]
        public async Task RunAsync_IgnoresOperationCanceledException()
        {
            var errorCount = 0;
            using var coordinator = new HubitatRefreshCoordinator(
                (_, __) => throw new OperationCanceledException(),
                _ => Interlocked.Increment(ref errorCount));

            await InvokeRunAsync(coordinator, refreshVersion: 1, CancellationToken.None);

            Assert.AreEqual(0, Volatile.Read(ref errorCount));
        }

        private static async Task InvokeRunAsync(HubitatRefreshCoordinator coordinator, int refreshVersion, CancellationToken ct)
        {
            var runAsync = typeof(HubitatRefreshCoordinator).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)runAsync.Invoke(coordinator, new object[] { refreshVersion, ct })!;
        }
    }
}
