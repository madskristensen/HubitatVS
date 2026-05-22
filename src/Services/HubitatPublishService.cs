using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Shell.Interop;

namespace HubitatVS
{
    internal static class HubitatPublishService
    {
        private static RatingPrompt _ratingPrompt = new("MadsKristensen.HubitatVS", Vsix.Name, HubitatHubSettings.Instance, 2);

        /// <summary>Selects a hub (prompting if multiple) then publishes a single file.</summary>
        public static async Task PublishFileAsync(string filePath)
        {
            IVsOutputWindowPane pane = await HubitatOutput.GetPaneAsync();
            await ActivatePaneAsync(pane);

            await VS.StatusBar.ShowMessageAsync("Publishing to Hubitat…");

            var hubs = await HubitatHubSettings.GetHubsCachedAsync();

            if (hubs.Count == 0)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Hubitat",
                    "No Hubitat connections configured. Open Hubitat Connections from the Tools menu to add one.");
                await HubitatConnectionsToolWindow.ShowAsync();
                return;
            }

            var hub = await HubitatHubSelectionService.SelectHubAsync(hubs);
            if (hub == null)
                return;

            await PublishFilesAsync(new[] { filePath }, hub);
        }

        public static async Task PublishFilesAsync(IList<string> filePaths, HubitatHubConfig hub)
        {
            var pane = await HubitatOutput.GetPaneAsync();
            await ActivatePaneAsync(pane);

            await VS.StatusBar.ShowMessageAsync($"Publishing to Hubitat ({hub.Name})…");

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            await WriteToPaneAsync(
                pane,
                $"[{timestamp}] Publishing {filePaths.Count} file(s) to {hub.Name} ({hub.Host})\r\n");

            var ct = CancellationToken.None;
            int successCount = 0;
            int failCount = 0;

            using var client = new HubitatHubClient(hub);

            for (int i = 0; i < filePaths.Count; i++)
            {
                var filePath = filePaths[i];
                var fileName = Path.GetFileName(filePath);

                await VS.StatusBar.ShowProgressAsync($"Publishing {fileName}…", i + 1, filePaths.Count);

                string source;
                try
                {
                    source = await Task.Run(() => File.ReadAllText(filePath), ct);
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                    await WriteToPaneAsync(pane, $"  ✗ {fileName}: Could not read file — {ex.Message}\r\n");
                    failCount++;
                    continue;
                }

                var candidate = HubitatGroovyAnalyzer.AnalyzeSource(source, filePath);

                // Check if we need to prompt the user for target selection
                int? targetId = null;
                if (!string.IsNullOrWhiteSpace(candidate.NamespaceName))
                {
                    var namespaceMatches = await client.GetNamespaceMatchesAsync(candidate.Kind, candidate.NamespaceName, ct);

                    // Check if there's an exact match (name + namespace)
                    var exactMatch = namespaceMatches.FirstOrDefault(entry =>
                        string.Equals(entry.Name, candidate.DisplayName, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                    {
                        targetId = exactMatch.Id;
                    }
                    else if (namespaceMatches.Count > 0)
                    {
                        // No exact match, but namespace matches exist - prompt user
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        var dialog = new HubitatCodePickerDialog(candidate.DisplayName, candidate.NamespaceName, namespaceMatches);
                        if (dialog.ShowModal() != true)
                        {
                            await WriteToPaneAsync(pane, $"  ⊘ {fileName}: Cancelled by user\r\n");
                            continue;
                        }

                        targetId = dialog.IsCreateNew ? (int?)null : dialog.SelectedEntry.Id;
                    }
                }

                HubitatPublishTracker.NotifyPublishStarted(filePath);
                HubitatPublishResult result;
                try
                {
                    result = targetId.HasValue || !string.IsNullOrWhiteSpace(candidate.NamespaceName)
                        ? await client.PublishToTargetAsync(candidate, source, targetId, ct)
                        : await client.PublishAsync(candidate, source, ct);
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                    result = new HubitatPublishResult
                    {
                        Success = false,
                        Message = $"[{ex.GetType().Name}] {ex.Message}",
                        Details = BuildExceptionDetails(ex)
                    };
                }

                HubitatPublishTracker.NotifyPublishCompleted(filePath, result.Success);
                var icon = result.Success ? "✓" : "✗";
                var versionSuffix = result.Success && result.PublishedVersion.HasValue
                    ? $" (version {result.PublishedVersion.Value})"
                    : string.Empty;
                await WriteToPaneAsync(pane, $"  {icon} {fileName}: {result.Message}{versionSuffix}\r\n");
                if (!result.Success && !string.IsNullOrWhiteSpace(result.Details))
                {
                    await WriteToPaneAsync(pane, $"    {result.Details.Replace("\r\n", "\r\n    ")}\r\n");
                }

                if (result.Success) successCount++; else failCount++;
            }

            await WriteToPaneAsync(pane, $"Done: {successCount} succeeded, {failCount} failed.\r\n\r\n");

            var summary = filePaths.Count == 1
                ? (successCount == 1 ? $"✓ Published to {hub.Name}" : $"✗ Publish to {hub.Name} failed")
                : $"Published to {hub.Name}: {successCount}/{filePaths.Count} succeeded";

            await VS.StatusBar.ShowMessageAsync(summary);

            if (successCount > 0)
            {
                HubitatConnectionTracker.NotifyHubDataChanged();
            }

            if (failCount == 0)
            {
                _ratingPrompt.RegisterSuccessfulUsage();
            }
        }

        private static async Task ActivatePaneAsync(IVsOutputWindowPane? pane)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            pane?.Activate();
        }

        private static Task WriteToPaneAsync(IVsOutputWindowPane? pane, string message)
        {
            pane?.OutputStringThreadSafe(message);
            return Task.CompletedTask;
        }

        private static string BuildExceptionDetails(Exception ex)
        {
            var parts = new List<string>();
            var current = ex.InnerException;
            while (current != null)
            {
                parts.Add($"Inner: [{current.GetType().Name}] {current.Message}");
                current = current.InnerException;
            }

            return parts.Count == 0 ? string.Empty : string.Join("\r\n", parts);
        }
    }
}
