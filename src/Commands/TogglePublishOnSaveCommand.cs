using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;

namespace HubitatVS
{
    [Command(PackageIds.TogglePublishOnSaveCommand)]
    internal sealed class TogglePublishOnSaveCommand : BaseCommand<TogglePublishOnSaveCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Command.Visible = HubitatGroovyFileSelection.GetSelectedGroovyFilePath() != null;
            Command.Checked = HubitatHubSettings.Instance.PublishOnSaveEnabled;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var settings = await HubitatHubSettings.GetLiveInstanceAsync();
            settings.PublishOnSaveEnabled = !settings.PublishOnSaveEnabled;
            await settings.SaveAsync();

            await VS.StatusBar.ShowMessageAsync(settings.PublishOnSaveEnabled
                ? "Publish on Save enabled"
                : "Publish on Save disabled");
        }
    }
}
