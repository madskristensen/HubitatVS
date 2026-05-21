using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;

namespace HubitatVS
{
    [Command(PackageIds.PublishToHubitatCommand)]
    internal sealed class PublishToHubitatCommand : BaseCommand<PublishToHubitatCommand>
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
            await HubitatPublishService.PublishFileAsync(filePath);
        }


    }
}
