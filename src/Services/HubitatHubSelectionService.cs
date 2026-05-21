using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Shell;

namespace HubitatVS
{
    internal static class HubitatHubSelectionService
    {
        public static async System.Threading.Tasks.Task<HubitatHubConfig?> SelectHubAsync(IReadOnlyList<HubitatHubConfig> hubs)
        {
            if (hubs == null || hubs.Count == 0)
                return null;

            if (hubs.Count == 1)
                return hubs[0];

            if (HubitatHubPickerDialog.SessionHub != null)
            {
                var existing = hubs.FirstOrDefault(h =>
                    string.Equals(h.Name, HubitatHubPickerDialog.SessionHub.Name, StringComparison.Ordinal));
                return existing ?? HubitatHubPickerDialog.SessionHub;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dialog = new HubitatHubPickerDialog(hubs.ToList());
            return dialog.ShowModal() == true ? dialog.SelectedHub : null;
        }

        public static System.Threading.Tasks.Task<bool> TestConnectionAsync(HubitatHubConfig hub, CancellationToken ct = default)
            => HubitatConnectionTracker.TestConnectionAsync(hub, ct);
    }
}
