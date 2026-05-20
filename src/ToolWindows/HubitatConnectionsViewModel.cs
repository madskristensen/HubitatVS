using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
        }

        public async Task TestAllConnectionsAsync()
        {
            if (Hubs.Count == 0)
                return;

            // Test all connections in parallel
            var tasks = Hubs.Select(hubVm => TestConnectionAsync(hubVm)).ToArray();
            await Task.WhenAll(tasks);
        }

        private async Task TestConnectionAsync(HubitatHubViewModel hubVm)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            hubVm.Status = ConnectionStatus.Testing;
            hubVm.StatusMessage = "Testing…";

            try
            {
                using var client = new HubitatHubClient(hubVm.Hub);
                bool connected = await client.TestConnectionAsync(CancellationToken.None);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                hubVm.Status = connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
                hubVm.StatusMessage = connected ? "Connected" : "Failed";
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                hubVm.Status = ConnectionStatus.Disconnected;
                hubVm.StatusMessage = $"Error: {ex.Message}";
            }
        }
    }
}
