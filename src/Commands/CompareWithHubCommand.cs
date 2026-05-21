using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HubitatVS
{
    [Command(PackageIds.CompareWithHubCommand)]
    internal sealed class CompareWithHubCommand : BaseCommand<CompareWithHubCommand>
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
                await ExecuteCompareAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Compare Failed", ex.Message);
            }
        }

        public static async Task ExecuteCompareAsync(string localFilePath)
        {
            var pane = await HubitatOutput.GetPaneAsync();

            var localSource = await Task.Run(() => File.ReadAllText(localFilePath));

            // Analyze the local file
            var candidate = HubitatGroovyAnalyzer.AnalyzeSource(localSource, localFilePath);
            if (candidate.Kind == HubitatCodeKind.Unknown)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Compare with Hub",
                    "Could not determine if this is a Hubitat driver or app.");
                return;
            }

            if (string.IsNullOrWhiteSpace(candidate.DisplayName))
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Compare with Hub",
                    "Could not parse the name from the Groovy definition() block.");
                return;
            }

            // Get hub settings
            var hubs = await HubitatHubSettings.GetHubsCachedAsync();

            if (hubs.Count == 0)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Compare with Hub",
                    "No Hubitat hub connection configured. Please set up a connection in Tools > Hubitat Connections.");
                return;
            }

            var hub = await HubitatHubSelectionService.SelectHubAsync(hubs);
            if (hub == null)
                return;

            await WriteToPaneAsync(pane, $"Testing connection to hub {hub.Name}...\r\n");

            var connected = await HubitatHubSelectionService.TestConnectionAsync(hub);
            if (!connected)
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Compare with Hub",
                    $"Could not connect to hub at {hub.Host}. Please verify your connection settings.");
                return;
            }

            await WriteToPaneAsync(pane, $"Searching for {candidate.Kind.ToString().ToLower()} '{candidate.DisplayName}'...\r\n");

            using var client = new HubitatHubClient(hub);

            // Find the code on the hub
            var matches = await client.GetNamespaceMatchesAsync(candidate.Kind, candidate.NamespaceName);
            var match = matches.FirstOrDefault(m =>
                string.Equals(m.Name, candidate.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "Compare with Hub",
                    $"Could not find {candidate.Kind.ToString().ToLower()} '{candidate.DisplayName}' on the hub.\n\n" +
                    $"Make sure it has been published at least once.");
                return;
            }

            await WriteToPaneAsync(pane, $"Downloading hub version (ID {match.Id})...\r\n");

            // Download the hub version
            var hubSource = await client.DownloadCodeSourceAsync(candidate.Kind, match.Id);
            if (string.IsNullOrEmpty(hubSource))
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Compare with Hub",
                    $"Failed to download {candidate.Kind.ToString().ToLower()} source from hub.");
                return;
            }

            // Write temp file
            var tempDir = Path.Combine(Path.GetTempPath(), "HubitatVS");
            var fileName = Path.GetFileName(localFilePath);
            var tempFile = Path.Combine(
                tempDir,
                $"{Path.GetFileNameWithoutExtension(fileName)}.{Guid.NewGuid():N}.hub{Path.GetExtension(fileName)}");
            Directory.CreateDirectory(tempDir);
            CleanupOldCompareTempFiles(tempDir);
            await Task.Run(() => File.WriteAllText(tempFile, hubSource));

            await WriteToPaneAsync(pane, $"Comparing local file with hub version...\r\n");

            // DTE.ExecuteCommand requires the UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await VS.GetServiceAsync<EnvDTE.DTE, EnvDTE.DTE>();
            dte.ExecuteCommand("Tools.DiffFiles", $"\"{localFilePath}\" \"{tempFile}\"");

            await VS.StatusBar.ShowMessageAsync("Compare with Hub completed");
        }

        private static Task WriteToPaneAsync(IVsOutputWindowPane pane, string message)
        {
            pane?.OutputStringThreadSafe(message);
            return Task.CompletedTask;
        }

        private static void CleanupOldCompareTempFiles(string tempDir)
        {
            try
            {
                var threshold = DateTime.UtcNow.AddDays(-1);
                foreach (var file in Directory.EnumerateFiles(tempDir, "*.hub*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(file);
                        if (lastWrite < threshold)
                            File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }
}
