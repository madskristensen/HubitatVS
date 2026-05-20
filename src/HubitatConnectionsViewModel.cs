using Microsoft.VisualStudio.Shell;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace HubitatVS
{
    internal sealed class HubitatConnectionsViewModel
    {
        public ObservableCollection<HubitatHubConfig> Hubs { get; } = new ObservableCollection<HubitatHubConfig>();

        public async Task ReloadHubsAsync()
        {
            var settings = await HubitatHubSettings.GetLiveInstanceAsync();
            var hubs = settings.GetHubs();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Hubs.Clear();
            foreach (var hub in hubs)
                Hubs.Add(hub);
        }
    }
}
