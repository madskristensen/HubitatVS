using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HubitatVS
{
    /// <summary>
    /// Centralized hub connection-state cache and testing coordinator.
    /// </summary>
    internal static class HubitatConnectionTracker
    {
        private static readonly Dictionary<string, bool> _status =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _testing =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();
        private static Task _testTask = Task.CompletedTask;

        /// <summary>Fired when any hub's connection state or the hub list itself changes.</summary>
        public static event EventHandler HubsChanged;

        /// <summary>Records the result of an explicit connection test.</summary>
        public static void SetConnected(string hubName, bool connected)
        {
            bool changed;
            lock (_lock)
            {
                changed = !_status.TryGetValue(hubName, out var existing) || existing != connected;
                _status[hubName] = connected;
            }

            if (changed)
                HubsChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Runs one connection test and updates shared status.</summary>
        public static async Task<bool> TestConnectionAsync(HubitatHubConfig hub, CancellationToken ct = default)
        {
            if (hub == null)
                throw new ArgumentNullException(nameof(hub));

            SetTesting(hub.Name, true);
            try
            {
                bool connected;
                try
                {
                    using var client = new HubitatHubClient(hub);
                    connected = await client.TestConnectionAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ex.Log();
                    connected = false;
                }

                SetConnected(hub.Name, connected);
                return connected;
            }
            finally
            {
                SetTesting(hub.Name, false);
            }
        }

        /// <summary>
        /// Ensures configured hubs have an initial known connection state.
        /// Multiple callers share the same in-flight work.
        /// </summary>
        public static async Task EnsureConnectionsTestedAsync(IEnumerable<HubitatHubConfig> hubs, CancellationToken ct = default)
        {
            if (hubs == null)
                return;

            var configuredHubs = hubs
                .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Name))
                .ToArray();
            if (configuredHubs.Length == 0)
                return;

            Task pending;
            lock (_lock)
            {
                var configuredNames = new HashSet<string>(configuredHubs.Select(h => h.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var stale in _status.Keys.Where(k => !configuredNames.Contains(k)).ToList())
                    _status.Remove(stale);

                var untested = configuredHubs
                    .Where(h => !_status.ContainsKey(h.Name) && !_testing.Contains(h.Name))
                    .ToArray();

                if (untested.Length == 0)
                {
                    pending = _testTask;
                }
                else
                {
                    var newWork = TestConnectionsCoreAsync(untested);
                    _testTask = _testTask.IsCompleted
                        ? newWork
                        : Task.WhenAll(_testTask, newWork);
                    pending = _testTask;
                }
            }

            ct.ThrowIfCancellationRequested();
            await pending;
            ct.ThrowIfCancellationRequested();
        }

        /// <summary>Clears cached statuses after hub configuration changes.</summary>
        public static void NotifyConfigChanged()
        {
            lock (_lock)
            {
                _status.Clear();
                _testing.Clear();
                _testTask = Task.CompletedTask;
            }

            HubsChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Signals that hub-side code data changed and dependents should refresh.</summary>
        public static void NotifyHubDataChanged()
            => HubsChanged?.Invoke(null, EventArgs.Empty);

        /// <summary>
        /// Returns true/false if the hub has been explicitly tested, or null if untested.
        /// </summary>
        public static bool? GetConnected(string hubName)
        {
            lock (_lock)
            {
                return _status.TryGetValue(hubName, out var s) ? s : (bool?)null;
            }
        }

        /// <summary>Returns true while a connection test is currently running for this hub.</summary>
        public static bool IsTesting(string hubName)
        {
            lock (_lock)
            {
                return _testing.Contains(hubName);
            }
        }

        private static async Task TestConnectionsCoreAsync(IEnumerable<HubitatHubConfig> hubs)
        {
            await Task.WhenAll(hubs.Select(h => TestConnectionAsync(h, CancellationToken.None)));
        }

        private static void SetTesting(string hubName, bool testing)
        {
            bool changed;
            lock (_lock)
            {
                changed = testing ? _testing.Add(hubName) : _testing.Remove(hubName);
            }

            if (changed)
                HubsChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
