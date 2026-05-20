using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HubitatVS
{
    internal static class HubitatPublishService
    {
        /// <summary>Selects a hub (prompting if multiple) then publishes a single file.</summary>
        public static async Task PublishFileAsync(string filePath)
        {
            var pane = await HubitatOutput.GetPaneAsync();
            pane?.Activate();

            await VS.StatusBar.ShowMessageAsync("Publishing to Hubitat…");

            var settings = await HubitatHubSettings.GetLiveInstanceAsync();
            var hubs = settings.GetHubs();

            if (hubs.Count == 0)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Hubitat",
                    "No Hubitat connections configured. Open Hubitat Connections from the Tools menu to add one.");
                return;
            }

            HubitatHubConfig hub;
            if (hubs.Count == 1)
            {
                hub = hubs[0];
            }
            else if (HubitatHubPickerDialog.SessionHub != null)
            {
                // Use the hub the user pinned for this session, but verify it's still in the list.
                hub = hubs.Find(h => string.Equals(h.Name, HubitatHubPickerDialog.SessionHub.Name, StringComparison.Ordinal))
                      ?? HubitatHubPickerDialog.SessionHub;
            }
            else
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dialog = new HubitatHubPickerDialog(hubs);
                if (dialog.ShowModal() != true || dialog.SelectedHub == null)
                    return;
                hub = dialog.SelectedHub;
            }

            await PublishFilesAsync(new[] { filePath }, hub);
        }

        public static async Task PublishFilesAsync(IList<string> filePaths, HubitatHubConfig hub)
        {
            var pane = await HubitatOutput.GetPaneAsync();
            pane?.Activate();

            await VS.StatusBar.ShowMessageAsync($"Publishing to Hubitat ({hub.Name})\u2026");

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            pane?.OutputStringThreadSafe(
                $"[{timestamp}] Publishing {filePaths.Count} file(s) to {hub.Name} ({hub.Host})\r\n");

            var ct = CancellationToken.None;
            int successCount = 0;
            int failCount = 0;

            using var client = new HubitatHubClient(hub);

            for (int i = 0; i < filePaths.Count; i++)
            {
                var filePath = filePaths[i];
                var fileName = Path.GetFileName(filePath);

                await VS.StatusBar.ShowProgressAsync($"Publishing {fileName}\u2026", i + 1, filePaths.Count);

                string source;
                try
                {
                    source = await System.Threading.Tasks.Task.Run(() => File.ReadAllText(filePath), ct);
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                    pane?.OutputStringThreadSafe($"  \u2717 {fileName}: Could not read file \u2014 {ex.Message}\r\n");
                    failCount++;
                    continue;
                }

                HubitatPublishResult result;
                try
                {
                    result = await client.PublishDriverAsync(source, ct);
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                    result = new HubitatPublishResult { Success = false, Message = $"[{ex.GetType().Name}] {ex.Message}" };
                }

                var icon = result.Success ? "\u2713" : "\u2717";
                pane?.OutputStringThreadSafe($"  {icon} {fileName}: {result.Message}\r\n");

                if (result.Success) successCount++; else failCount++;
            }

            pane?.OutputStringThreadSafe($"Done: {successCount} succeeded, {failCount} failed.\r\n\r\n");

            var summary = filePaths.Count == 1
                ? (successCount == 1 ? $"\u2713 Published to {hub.Name}" : $"\u2717 Publish to {hub.Name} failed")
                : $"Published to {hub.Name}: {successCount}/{filePaths.Count} succeeded";

            await VS.StatusBar.ShowMessageAsync(summary);
        }
    }
}
