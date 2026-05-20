using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace HubitatVS
{
    [Command(PackageIds.ShowHubitatConnectionsToolWindowCommand)]
    internal sealed class ShowHubitatConnectionsToolWindowCommand : BaseCommand<ShowHubitatConnectionsToolWindowCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await HubitatConnectionsToolWindow.ShowAsync();
        }
    }
}
