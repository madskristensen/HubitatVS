global using Community.VisualStudio.Toolkit;

global using Microsoft.VisualStudio.Shell;

global using System;

global using Task = System.Threading.Tasks.Task;

using System.Runtime.InteropServices;
using System.Threading;

namespace HubitatVS
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(HubitatConnectionsToolWindow.Pane), Style = VsDockStyle.Tabbed, Window = WindowGuids.OutputWindow)]
    [ProvideAutoLoad("4646B819-1AE0-4E79-97F4-8A8176FDD664", PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(HubitatHubSettingsDialogPage), "Hubitat", "Hub Connections", 0, 0, true)]
    [ProvideBindingPath()]
    [Guid(PackageGuids.HubitatVSString)]
    public sealed class HubitatVSPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            this.RegisterToolWindows();
            await this.RegisterCommandsAsync();
        }
    }
}