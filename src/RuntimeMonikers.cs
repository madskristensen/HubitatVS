using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace HubitatVS
{
    internal static class RuntimeMonikers
    {
        private static IImageHandle _hubitatLogoHandle;

        internal static ImageMoniker HubitatLogoMoniker { get; private set; } = KnownMonikers.StatusInformation;

        internal static async Task InitializeAsync(AsyncPackage package, CancellationToken cancellationToken)
        {
            if (_hubitatLogoHandle != null)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var imageService = await package.GetServiceAsync(typeof(SVsImageService)) as IVsManagedImageService;
            if (imageService == null)
                throw new InvalidOperationException("Could not acquire SVsImageService.");

            var image = new BitmapImage(new Uri("pack://application:,,,/HubitatVS;component/Resources/Icons/logo.16.16.png", UriKind.Absolute));
            image.Freeze();

            _hubitatLogoHandle = imageService.AddCustomImage(image, canTheme: false);
            HubitatLogoMoniker = _hubitatLogoHandle.Moniker;
        }
    }
}
