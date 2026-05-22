global using System;
global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using Task = System.Threading.Tasks.Task;
using System.Runtime.InteropServices;
using System.Threading;

namespace HubitatVS
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(HubitatConnectionsToolWindow.Pane), Style = VsDockStyle.Tabbed, Window = WindowGuids.OutputWindow)]
    [ProvideUIContextRule(
        PackageGuids.GroovyEditorOpenContextString,
        name: "Hubitat .groovy editor open",
        expression: "GroovyContentType | TextMateGroovyContentType",
        termNames: new[] { "GroovyContentType", "TextMateGroovyContentType" },
        termValues: new[]
        {
            "ActiveEditorContentType:groovy",
            "ActiveEditorContentType:code++.groovy"
        })]
    [ProvideAutoLoad(PackageGuids.GroovyEditorOpenContextString, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideProfile(typeof(HubitatHubSettingsDialogPage), "Hubitat", "Hub Connections", 0, 0, true)]
    [Guid(PackageGuids.HubitatVSString)]
    public sealed class HubitatVSPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await RuntimeMonikers.InitializeAsync(this, cancellationToken);
            this.RegisterToolWindows();
            await this.RegisterCommandsAsync();
        }
    }
}