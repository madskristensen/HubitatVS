using Microsoft.VisualStudio.PlatformUI;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HubitatVS
{
    public partial class HubitatHubPickerDialog : DialogWindow
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        /// <summary>Hub remembered for the lifetime of this VS session when "Don't ask me again" is checked.</summary>
        public static HubitatHubConfig SessionHub { get; private set; }

        public HubitatHubConfig SelectedHub { get; private set; }

        public HubitatHubPickerDialog(IList<HubitatHubConfig> hubs)
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            HubListBox.ItemsSource = hubs;
            if (hubs.Count > 0)
                HubListBox.SelectedIndex = 0;
        }

        private void Confirm()
        {
            SelectedHub = HubListBox.SelectedItem as HubitatHubConfig;
            if (SelectedHub == null) return;

            if (DontAskCheckBox.IsChecked == true)
                SessionHub = SelectedHub;

            DialogResult = true;
        }

        private void OK_Click(object sender, RoutedEventArgs e) => Confirm();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void HubListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                ApplyTitleBarTheme();
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private void ApplyTitleBarTheme()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (TryGetResourceColor(EnvironmentColors.ToolWindowBackgroundBrushKey, out var captionColor))
            {
                int captionColorRef = ToColorRef(captionColor);
                _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColorRef, sizeof(int));
            }

            if (TryGetResourceColor(EnvironmentColors.ToolWindowTextBrushKey, out var textColor))
            {
                int textColorRef = ToColorRef(textColor);
                _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColorRef, sizeof(int));
            }

            var darkMode = 1;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        }

        private bool TryGetResourceColor(object key, out Color color)
        {
            if (TryFindResource(key) is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }

            color = default;
            return false;
        }

        private static int ToColorRef(Color color)
            => color.R | (color.G << 8) | (color.B << 16);
    }
}
