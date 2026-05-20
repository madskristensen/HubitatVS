using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Threading.Tasks;

namespace HubitatVS
{
    internal static class HubitatOutput
    {
        private static readonly Guid PaneGuid = new Guid("A9F3E271-08B4-4C5A-BD91-23C4F0E7D830");
        private const string PaneName = "Hubitat";

        public static async Task<IVsOutputWindowPane> GetPaneAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var outputWindow = ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
                return null;

            var guid = PaneGuid;
            if (ErrorHandler.Failed(outputWindow.GetPane(ref guid, out var pane)) || pane == null)
            {
                guid = PaneGuid;
                outputWindow.CreatePane(ref guid, PaneName, fInitVisible: 1, fClearWithSolution: 0);
                guid = PaneGuid;
                outputWindow.GetPane(ref guid, out pane);
            }

            return pane;
        }
    }
}
