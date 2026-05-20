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
    public partial class HubitatCodePickerDialog : DialogWindow
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        // Sentinel value to indicate "create new"
        private static readonly HubitatCodeEntry CreateNewEntry = new HubitatCodeEntry
        {
            Id = -1,
            Name = "➕ Create as new app/driver",
            Namespace = string.Empty
        };

        public HubitatCodeEntry SelectedEntry { get; private set; }

        public bool IsCreateNew => SelectedEntry?.Id == -1;

        public HubitatCodePickerDialog(
            string appOrDriverName,
            string namespaceName,
            IReadOnlyList<HubitatCodeEntry> existingEntries)
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;

            MessageRun.Text = string.IsNullOrWhiteSpace(namespaceName)
                ? $"No exact match found for \"{appOrDriverName}\"."
                : $"No exact match found for \"{appOrDriverName}\" in namespace \"{namespaceName}\".";

            var items = new List<HubitatCodeEntry> { CreateNewEntry };
            items.AddRange(existingEntries);

            CodeListBox.ItemsSource = items;
            CodeListBox.SelectedIndex = 0;
        }

        private void Confirm()
        {
            SelectedEntry = CodeListBox.SelectedItem as HubitatCodeEntry;
            if (SelectedEntry == null) return;

            DialogResult = true;
        }

        private void OK_Click(object sender, RoutedEventArgs e) => Confirm();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void CodeListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

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
