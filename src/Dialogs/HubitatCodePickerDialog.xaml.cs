using Microsoft.VisualStudio.PlatformUI;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace HubitatVS
{
    public partial class HubitatCodePickerDialog : DialogWindow
    {
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

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                VsDialogThemeHelper.ApplyTitleBarTheme(this);
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }


    }
}
