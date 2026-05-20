using Microsoft.VisualStudio.PlatformUI;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace HubitatVS
{
    public partial class HubitatHubPickerDialog : DialogWindow
    {
        /// <summary>Hub remembered for the lifetime of this VS session when "Don't ask me again" is checked.</summary>
        public static HubitatHubConfig SessionHub { get; private set; }

        public HubitatHubConfig SelectedHub { get; private set; }

        public HubitatHubPickerDialog(IList<HubitatHubConfig> hubs)
        {
            InitializeComponent();
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
    }
}
