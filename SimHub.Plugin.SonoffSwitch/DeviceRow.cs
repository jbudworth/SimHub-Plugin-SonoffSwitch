using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimHub.Plugin.SonoffSwitch
{
    /// <summary>
    /// Binds a <see cref="SonoffDeviceConfig"/> to the settings DataGrid and adds a
    /// read-only StatusText column fed from the plugin's live runtime state.
    /// </summary>
    public class DeviceRow : INotifyPropertyChanged
    {
        public SonoffDeviceConfig Config { get; }

        public DeviceRow(SonoffDeviceConfig config)
        {
            Config = config;
        }

        public string Name
        {
            get => Config.Name;
            set { Config.Name = value; OnPropertyChanged(); }
        }

        public string IpAddress
        {
            get => Config.IpAddress;
            set { Config.IpAddress = value; OnPropertyChanged(); }
        }

        public bool UseAuth
        {
            get => Config.UseAuth;
            set { Config.UseAuth = value; OnPropertyChanged(); }
        }

        public string Username
        {
            get => Config.Username;
            set { Config.Username = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Encrypted ciphertext as stored in Config.Password. Exposed only so the grid's
        /// non-editing display template can check "is a password set?" (via the mask
        /// converter) without ever decrypting anything. Do not bind an editor to this.
        /// </summary>
        public string Password => Config.Password;

        /// <summary>
        /// Plaintext view of the password, used only by the PasswordBox editing template.
        /// Reading decrypts Config.Password on demand; writing re-encrypts it immediately,
        /// so Config.Password never holds plaintext even while the row is being edited.
        /// </summary>
        public string PlainPassword
        {
            get => PasswordProtector.Decrypt(Config.Password);
            set { Config.Password = PasswordProtector.Encrypt(value); OnPropertyChanged(); OnPropertyChanged(nameof(Password)); }
        }

        private string _statusText = "Not checked yet";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
