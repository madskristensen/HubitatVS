using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HubitatVS
{
    internal sealed class HubitatHubViewModel : INotifyPropertyChanged
    {
        private ConnectionStatus _status = ConnectionStatus.Unknown;
        private string _statusMessage = string.Empty;

        public HubitatHubConfig Hub { get; }

        public ConnectionStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusColor
        {
            get
            {
                return Status switch
                {
                    ConnectionStatus.Connected => "#28A745",    // Green
                    ConnectionStatus.Disconnected => "#DC3545", // Red
                    ConnectionStatus.Testing => "#FFC107",      // Yellow/Amber
                    _ => "#6C757D"                              // Gray
                };
            }
        }

        public HubitatHubViewModel(HubitatHubConfig hub)
        {
            Hub = hub;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal enum ConnectionStatus
    {
        Unknown,
        Testing,
        Connected,
        Disconnected
    }
}
