using System.Windows;

namespace SimHub.Plugin.SonoffSwitch
{
    public partial class PasswordDialog : Window
    {
        /// <summary>The plaintext password entered, valid once ShowDialog() returns true.</summary>
        public string Password { get; private set; } = "";

        public PasswordDialog(string deviceName)
        {
            InitializeComponent();
            PromptText.Text = $"Enter the web admin password for \"{deviceName}\":";
            Loaded += (s, e) => PasswordInput.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Password = PasswordInput.Password;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
