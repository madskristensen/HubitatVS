using Microsoft.VisualStudio.Shell;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HubitatVS
{
    public partial class HubitatConnectionsToolWindowControl : UserControl
    {
        private static bool _testedThisSession;
        private bool _trackerSubscribed;
        private string _pendingHubPassword = string.Empty;

        public HubitatConnectionsToolWindowControl()
        {
            InitializeComponent();
            DataContext = new HubitatConnectionsViewModel();
            Loaded += async (s, e) =>
            {
                if (!_trackerSubscribed)
                {
                    HubitatConnectionTracker.HubsChanged += OnTrackerChanged;
                    _trackerSubscribed = true;
                }

                await ViewModel.ReloadHubsAsync();
                if (!_testedThisSession)
                {
                    _testedThisSession = true;
                    await ViewModel.TestAllConnectionsAsync();
                }
            };
            Unloaded += (s, e) =>
            {
                if (_trackerSubscribed)
                {
                    HubitatConnectionTracker.HubsChanged -= OnTrackerChanged;
                    _trackerSubscribed = false;
                }
            };
        }

        private HubitatConnectionsViewModel ViewModel => (HubitatConnectionsViewModel)DataContext;

        private void OnTrackerChanged(object sender, EventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ViewModel.ApplyTrackerStatusesAsync();
                }
                catch (Exception ex)
                {
                    ex.Log();
                }
            });
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            var hubVm = HubsGrid.SelectedItem as HubitatHubViewModel;
            if (hubVm == null)
            {
                await VS.StatusBar.ShowMessageAsync("Select a hub first.");
                return;
            }

            await VS.StatusBar.ShowMessageAsync($"Testing connection to {hubVm.Hub.Name}\u2026");
            try
            {
                bool ok = await ViewModel.TestConnectionAsync(hubVm);

                var msg = ok
                    ? $"\u2713 Connected to {hubVm.Hub.Name} ({hubVm.Hub.Host})"
                    : $"\u2717 Could not connect to {hubVm.Hub.Host}";
                await VS.StatusBar.ShowMessageAsync(msg);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                hubVm.Status = ConnectionStatus.Disconnected;
                hubVm.StatusMessage = "Error";
                await VS.StatusBar.ShowMessageAsync($"\u2717 [{ex.GetType().Name}] {ex.Message}");
            }
        }

        private void NewHub_Click(object sender, RoutedEventArgs e)
        {
            HubsGrid.SelectedItem = null;
            ClearFormFields();
            HubNameBox.Focus();
        }

        private async void DeleteHub_Click(object sender, RoutedEventArgs e)
        {
            var hubVm = HubsGrid.SelectedItem as HubitatHubViewModel;
            if (hubVm == null)
            {
                await VS.StatusBar.ShowMessageAsync("Select a hub first.");
                return;
            }

            var hub = hubVm.Hub;
            var settings = await HubitatHubSettings.GetLiveInstanceAsync();
            var hubs = settings.GetHubs();
            hubs.RemoveAll(h =>
                string.Equals(h.Name, hub.Name, StringComparison.Ordinal) &&
                string.Equals(h.Host, hub.Host, StringComparison.Ordinal));
            settings.SetHubs(hubs);
            await settings.SaveAsync();
            await ViewModel.ReloadHubsAsync();
            HubitatConnectionTracker.RemoveHub(hub.Name);
            ClearFormFields();
            await VS.StatusBar.ShowMessageAsync($"Hub '{hub.Name}' deleted.");
        }

        private async void SaveHub_Click(object sender, RoutedEventArgs e)
        {
            var name = HubNameBox.Text.Trim();
            var host = HubHostBox.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(host))
            {
                await VS.StatusBar.ShowMessageAsync("Name and Host are required.");
                return;
            }

            var settings = await HubitatHubSettings.GetLiveInstanceAsync();
            var hubs = settings.GetHubs();
            var existing = hubs.FirstOrDefault(h =>
                string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Host = host;
                existing.Username = HubUsernameBox.Text.Trim();
                existing.Password = _pendingHubPassword;
            }
            else
            {
                hubs.Add(new HubitatHubConfig
                {
                    Name = name,
                    Host = host,
                    Username = HubUsernameBox.Text.Trim(),
                    Password = _pendingHubPassword
                });
            }

            settings.SetHubs(hubs);
            await settings.SaveAsync();
            await ViewModel.ReloadHubsAsync();

            var savedHubVm = ViewModel.Hubs.FirstOrDefault(h =>
                string.Equals(h.Hub.Name, name, StringComparison.OrdinalIgnoreCase));
            if (savedHubVm != null)
            {
                await VS.StatusBar.ShowMessageAsync($"Testing connection to {savedHubVm.Hub.Name}...");
                bool ok = await ViewModel.TestConnectionAsync(savedHubVm);
                await VS.StatusBar.ShowMessageAsync(ok
                    ? $"Connected to {savedHubVm.Hub.Name} ({savedHubVm.Hub.Host})"
                    : $"Could not connect to {savedHubVm.Hub.Host}");
            }
            else
            {
                await VS.StatusBar.ShowMessageAsync($"Hub '{name}' saved.");
            }
        }

        private void ClearForm_Click(object sender, RoutedEventArgs e)
        {
            HubsGrid.SelectedItem = null;
            ClearFormFields();
        }

        private void HubPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
            => _pendingHubPassword = HubPasswordBox.Password;

        private void HubsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var hubVm = HubsGrid.SelectedItem as HubitatHubViewModel;
            if (hubVm == null) return;

            var hub = hubVm.Hub;
            HubNameBox.Text = hub.Name;
            HubHostBox.Text = hub.Host;
            HubUsernameBox.Text = hub.Username;
            HubPasswordBox.Password = hub.Password;
            _pendingHubPassword = hub.Password;
        }

        private void ClearFormFields()
        {
            HubNameBox.Text = string.Empty;
            HubHostBox.Text = string.Empty;
            HubUsernameBox.Text = string.Empty;
            HubPasswordBox.Password = string.Empty;
            _pendingHubPassword = string.Empty;
        }
    }
}
