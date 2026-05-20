using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;

namespace HubitatVS
{
    [Command(PackageIds.PublishToHubitatCommand)]
    internal sealed class PublishToHubitatCommand : BaseCommand<PublishToHubitatCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Command.Visible = GetSelectedGroovyFilePath() != null;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var filePath = GetSelectedGroovyFilePath();
            if (filePath == null) return;
            await HubitatPublishService.PublishFileAsync(filePath);
        }

        private static string GetSelectedGroovyFilePath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var monitorSelection = Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (monitorSelection == null) return null;

            monitorSelection.GetCurrentSelection(
                out IntPtr hierPtr, out uint itemId,
                out IVsMultiItemSelect _, out IntPtr containerPtr);

            if (containerPtr != IntPtr.Zero)
                Marshal.Release(containerPtr);

            if (hierPtr == IntPtr.Zero) return null;

            IVsHierarchy hierarchy;
            try { hierarchy = (IVsHierarchy)Marshal.GetObjectForIUnknown(hierPtr); }
            finally { Marshal.Release(hierPtr); }

            hierarchy.GetCanonicalName(itemId, out string path);

            return path != null && path.EndsWith(".groovy", StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
        }
    }
}
