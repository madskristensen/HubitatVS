using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace HubitatVS
{
    internal sealed class HubitatConnectionsViewModel
    {
        public ObservableCollection<HubitatHubViewModel> Hubs { get; } = new ObservableCollection<HubitatHubViewModel>();

        public async Task ReloadHubsAsync()
        {
            var settings = await HubitatHubSettings.GetLiveInstanceAsync();
            var hubs = settings.GetHubs();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Hubs.Clear();
            foreach (var hub in hubs)
                Hubs.Add(new HubitatHubViewModel(hub));

            await ApplyTrackerStatusesAsync();
        }

        public async Task TestAllConnectionsAsync()
        {
            if (Hubs.Count == 0)
                return;

            await HubitatConnectionTracker.EnsureConnectionsTestedAsync(Hubs.Select(h => h.Hub).ToArray());
            await ApplyTrackerStatusesAsync();
        }

        public async Task<bool> TestConnectionAsync(HubitatHubViewModel hubVm)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            hubVm.Status = ConnectionStatus.Testing;
            hubVm.StatusMessage = "Testing…";

            var pane = await HubitatOutput.GetPaneAsync();
            await WriteToPaneAsync(pane, $"Testing connection to {hubVm.Hub.Name}…\r\n");

            try
            {
                bool connected = await HubitatConnectionTracker.TestConnectionAsync(hubVm.Hub);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                hubVm.Status = connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
                hubVm.StatusMessage = connected ? "Connected" : "Failed";

                if (connected)
                {
                    await WriteToPaneAsync(pane, $"✅ Connected to {hubVm.Hub.Name}\r\n");
                }
                else
                {
                    await WriteToPaneAsync(pane, $"❌ Failed to connect to {hubVm.Hub.Name}\r\n");
                }

                return connected;
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                hubVm.Status = ConnectionStatus.Disconnected;
                hubVm.StatusMessage = "Not connected";
                await WriteToPaneAsync(pane, $"❌ Failed to connect to {hubVm.Hub.Name}: {ex.GetBaseException().Message}\r\n");
                return false;
            }
        }

        public async Task ApplyTrackerStatusesAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (var hubVm in Hubs)
            {
                if (HubitatConnectionTracker.IsTesting(hubVm.Hub.Name))
                {
                    hubVm.Status = ConnectionStatus.Testing;
                    hubVm.StatusMessage = "Testing…";
                    continue;
                }

                var connected = HubitatConnectionTracker.GetConnected(hubVm.Hub.Name);
                if (connected == true)
                {
                    hubVm.Status = ConnectionStatus.Connected;
                    hubVm.StatusMessage = "Connected";
                }
                else if (connected == false)
                {
                    hubVm.Status = ConnectionStatus.Disconnected;
                    hubVm.StatusMessage = "Failed";
                }
                else
                {
                    hubVm.Status = ConnectionStatus.Unknown;
                    hubVm.StatusMessage = "Not tested";
                }
            }
        }

        private static Task WriteToPaneAsync(IVsOutputWindowPane? pane, string message)
        {
            pane?.OutputStringThreadSafe(message);
            return Task.CompletedTask;
        }
    }
}
