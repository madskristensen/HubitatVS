using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HubitatVS
{
    [Command(PackageIds.ReplaceWithHubVersionCommand)]
    internal sealed class ReplaceWithHubVersionCommand : BaseCommand<ReplaceWithHubVersionCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Command.Visible = HubitatGroovyFileSelection.GetSelectedGroovyFilePath() != null;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var filePath = HubitatGroovyFileSelection.GetSelectedGroovyFilePath();
            if (filePath == null) return;

            try
            {
                await ExecuteReplaceAsync(filePath);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                await VS.MessageBox.ShowErrorAsync("Replace with hub version failed", ex.Message);
            }
        }

        public static async Task ExecuteReplaceAsync(string localFilePath)
        {
            var pane = await HubitatOutput.GetPaneAsync();

            var localSource = await Task.Run(() => File.ReadAllText(localFilePath));

            var candidate = HubitatGroovyAnalyzer.AnalyzeSource(localSource, localFilePath);
            if (candidate.Kind == HubitatCodeKind.Unknown)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Replace with hub version",
                    "Could not determine whether this Groovy file is a Hubitat driver, app, or library.");
                return;
            }

            if (string.IsNullOrWhiteSpace(candidate.DisplayName))
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Replace with hub version",
                    "Could not parse the name from the Groovy definition() block.");
                return;
            }

            var hubs = await HubitatHubSettings.GetHubsCachedAsync();
            if (hubs.Count == 0)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Replace with hub version",
                    "No Hubitat hub connection configured. Please set up a connection in Tools > Hubitat Connections.");
                return;
            }

            var hub = await HubitatHubSelectionService.SelectHubAsync(hubs);
            if (hub == null)
                return;

            var fileName = Path.GetFileName(localFilePath);
            var kindNoun = candidate.Kind.ToString().ToLowerInvariant();
            var confirmed = await VS.MessageBox.ShowConfirmAsync(
                "Replace with hub version",
                $"Replace the contents of '{fileName}' with the {kindNoun} '{candidate.DisplayName}' from hub '{hub.Name}'?\r\n\r\n" +
                "The change is applied as a single edit in the editor, so you can press Ctrl+Z to undo it.");

            if (!confirmed)
                return;

            await WriteToPaneAsync(pane, $"Testing connection to hub {hub.Name}...\r\n");

            var connected = await HubitatHubSelectionService.TestConnectionAsync(hub);
            if (!connected)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Replace with hub version",
                    $"Could not connect to hub at {hub.Host}. Please verify your connection settings.");
                return;
            }

            await WriteToPaneAsync(pane, $"Searching for {kindNoun} '{candidate.DisplayName}'...\r\n");

            using IHubitatHubClient client = new HubitatHubClient(hub);
            var workflowResult = await HubitatCompareWorkflow.ResolveAsync(client, candidate);

            if (workflowResult.Failure == HubitatCompareFailure.NotFound)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Replace with hub version",
                    $"Could not find {kindNoun} '{candidate.DisplayName}' on the hub.\r\n\r\n" +
                    "Make sure it has been published at least once.");
                return;
            }

            if (workflowResult.Failure == HubitatCompareFailure.DownloadFailed)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Replace with hub version",
                    $"Failed to download {kindNoun} source from hub.");
                return;
            }

            var match = workflowResult.Match!;
            var hubSource = workflowResult.HubSource!;
            await WriteToPaneAsync(pane, $"Downloaded hub version (ID {match.Id}, {hubSource.Length} chars)...\r\n");

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Open the document (or activate it if already open) so the replacement is undoable in the editor.
            var docView = await VS.Documents.OpenAsync(localFilePath);
            if (docView?.TextBuffer == null)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Replace with hub version",
                    "Could not open the document to replace its contents.");
                return;
            }

            var textBuffer = docView.TextBuffer;
            var snapshot = textBuffer.CurrentSnapshot;
            if (string.Equals(snapshot.GetText(), hubSource, StringComparison.Ordinal))
            {
                await WriteToPaneAsync(pane, "Local file already matches hub version. No changes made.\r\n");
                await VS.StatusBar.ShowMessageAsync("Already in sync with hub");
                return;
            }

            using (var edit = textBuffer.CreateEdit())
            {
                edit.Replace(new Span(0, snapshot.Length), hubSource);
                edit.Apply();
            }

            await WriteToPaneAsync(pane, $"Replaced '{fileName}' with hub version. Press Ctrl+Z to undo.\r\n");
            await VS.StatusBar.ShowMessageAsync($"Replaced with hub version from {hub.Name}");
        }

        private static Task WriteToPaneAsync(IVsOutputWindowPane? pane, string message)
        {
            pane?.OutputStringThreadSafe(message);
            return Task.CompletedTask;
        }
    }
}
