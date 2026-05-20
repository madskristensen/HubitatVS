using Microsoft.VisualStudio.Imaging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HubitatVS
{
    public class HubitatConnectionsToolWindow : BaseToolWindow<HubitatConnectionsToolWindow>
    {
        public override string GetTitle(int toolWindowId) => "Hubitat Connections";

        public override Type PaneType => typeof(Pane);

        public override Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
            => Task.FromResult<FrameworkElement>(new HubitatConnectionsToolWindowControl());

        [Guid("E67E18FD-1E9A-4D2B-9AD4-B9F1F613D0E2")]
        internal sealed class Pane : ToolWindowPane
        {
            public Pane()
            {
                BitmapImageMoniker = KnownMonikers.StatusInformation;
            }
        }
    }
}
