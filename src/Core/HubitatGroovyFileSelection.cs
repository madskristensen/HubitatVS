using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;

namespace HubitatVS
{
    internal static class HubitatGroovyFileSelection
    {
        internal static string? GetSelectedGroovyFilePath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var monitorSelection = Package.GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (monitorSelection != null)
            {
                int hr = monitorSelection.GetCurrentSelection(
                    out IntPtr hierPtr, out uint itemId,
                    out IVsMultiItemSelect _, out IntPtr containerPtr);

                if (containerPtr != IntPtr.Zero)
                    Marshal.Release(containerPtr);

                if (!ErrorHandler.Failed(hr) && hierPtr != IntPtr.Zero && itemId != VSConstants.VSITEMID_NIL)
                {
                    IVsHierarchy hierarchy;
                    try { hierarchy = (IVsHierarchy)Marshal.GetObjectForIUnknown(hierPtr); }
                    finally { Marshal.Release(hierPtr); }

                    hr = hierarchy.GetCanonicalName(itemId, out string selectedPath);
                    if (!ErrorHandler.Failed(hr) && selectedPath != null && selectedPath.EndsWith(".groovy", StringComparison.OrdinalIgnoreCase))
                        return selectedPath;
                }
            }

            var dte = Package.GetGlobalService(typeof(SDTE)) as EnvDTE.DTE;
            var activePath = dte?.ActiveDocument?.FullName;
            return activePath != null && activePath.EndsWith(".groovy", StringComparison.OrdinalIgnoreCase)
                ? activePath
                : null;
        }
    }
}
