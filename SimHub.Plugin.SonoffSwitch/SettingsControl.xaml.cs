using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimHub.Plugin.SonoffSwitch
{
    public partial class SettingsControl : UserControl
    {
        private readonly SonoffPlugin _plugin;
        private readonly DispatcherTimer _statusRefreshTimer;

        public ObservableCollection<DeviceRow> Devices { get; } = new ObservableCollection<DeviceRow>();

        public SettingsControl(SonoffPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            DataContext = this;

            foreach (var device in _plugin.Settings.Devices)
                Devices.Add(new DeviceRow(device));

            PollIntervalBox.Text = _plugin.Settings.PollIntervalMs.ToString();

            RefreshStatusText();

            _statusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusRefreshTimer.Tick += (s, e) => RefreshStatusText();
            _statusRefreshTimer.Start();

            Unloaded += (s, e) => _statusRefreshTimer.Stop();
        }

        private void RefreshStatusText()
        {
            foreach (var row in Devices)
            {
                var state = _plugin.GetRuntimeState(row.Config.Id);
                if (state == null)
                {
                    row.StatusText = "Not registered yet - save settings first";
                    continue;
                }

                if (!state.Online)
                {
                    row.StatusText = string.IsNullOrEmpty(state.LastError)
                        ? "Offline"
                        : $"Offline ({state.LastError})";
                    continue;
                }

                var tempPart = state.Temperature.HasValue ? $"{state.Temperature.Value:0.0} C" : "no temp sensor";
                var humPart = state.Humidity.HasValue ? $"{state.Humidity.Value:0.0} %RH" : "no humidity sensor";
                var basePart = $"{(state.PowerOn ? "ON" : "OFF")} | {tempPart} | {humPart} | updated {state.LastUpdateUtc.ToLocalTime():T}";

                if (!state.Temperature.HasValue && !state.Humidity.HasValue && !string.IsNullOrEmpty(state.LastRawSensorJson))
                {
                    var snippet = state.LastRawSensorJson.Length > 120
                        ? state.LastRawSensorJson.Substring(0, 120) + "..."
                        : state.LastRawSensorJson;
                    basePart += $" | raw: {snippet}";
                }

                row.StatusText = basePart;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var config = new SonoffDeviceConfig
            {
                Name = $"Switch{Devices.Count + 1}",
                IpAddress = "192.168.1.100"
            };
            Devices.Add(new DeviceRow(config));
        }

        private void RowRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is DeviceRow row)
            {
                Devices.Remove(row);
                _plugin.UnregisterDevice(row.Config.Id);
            }
        }

        private void RowSetPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is DeviceRow row))
                return;

            var dialog = new PasswordDialog(row.Name) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                row.PlainPassword = dialog.Password;
                MessageBox.Show("Password updated. Click Save to apply it.", "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void RowTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is DeviceRow row))
                return;

            row.StatusText = "Testing...";
            try
            {
                await _plugin.TestDeviceAsync(row.Config);
            }
            catch
            {
                // Errors are captured into runtime state already; ignored here.
            }
            RefreshStatusText();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatusText();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveSettings())
                return;

            SavedLabel.Text = "Saved.";
            SavedLabel.Opacity = 1;
            var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            fadeTimer.Tick += (s, e2) => { SavedLabel.Opacity = 0; fadeTimer.Stop(); };
            fadeTimer.Start();
        }

        /// <summary>
        /// Validates the current grid contents, writes them into the plugin's live
        /// Settings, re-registers devices under any new names, restarts polling at the
        /// (possibly changed) interval, and persists to disk. Shared by the Save button
        /// and by Import, since importing a configuration should take effect immediately
        /// rather than needing a separate Save click afterwards.
        /// </summary>
        private bool TrySaveSettings()
        {
            if (!int.TryParse(PollIntervalBox.Text, out var pollInterval) || pollInterval < 500)
            {
                MessageBox.Show("Poll interval must be a number >= 500 (milliseconds).", "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var names = Devices.Select(d => d.Name?.Trim()).ToList();
            if (names.Any(string.IsNullOrEmpty) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            {
                MessageBox.Show("Every device needs a unique, non-empty name.", "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            _plugin.Settings.Devices = Devices.Select(d => d.Config).ToList();
            _plugin.Settings.PollIntervalMs = pollInterval;

            foreach (var row in Devices)
                _plugin.RegisterDevice(row.Config);

            _plugin.RestartPolling();
            _plugin.SaveSettingsNow();
            return true;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PollIntervalBox.Text, out var pollInterval) || pollInterval < 500)
            {
                MessageBox.Show("Poll interval must be a number >= 500 (milliseconds).", "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var snapshot = new SonoffPluginSettingsExportDto
            {
                Devices = Devices.Select(d => new SonoffDeviceExportDto
                {
                    Id = d.Config.Id,
                    Name = d.Config.Name,
                    IpAddress = d.Config.IpAddress,
                    UseAuth = d.Config.UseAuth,
                    Username = d.Config.Username
                }).ToList(),
                PollIntervalMs = pollInterval,
                RequestTimeoutMs = _plugin.Settings.RequestTimeoutMs
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Sonoff Switch configuration",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "SimHub.Plugin.SonoffSwitch.json"
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(snapshot, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(dialog.FileName, json);
                MessageBox.Show(
                    "Configuration Exported.\n\nPasswords are not included in exports for security.  After import all passwords will need to be re-entered.",
                    "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Sonoff Switch configuration",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true)
                return;

            SonoffPluginSettings imported;
            try
            {
                var json = System.IO.File.ReadAllText(dialog.FileName);
                imported = Newtonsoft.Json.JsonConvert.DeserializeObject<SonoffPluginSettings>(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: could not read that file ({ex.Message})", "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (imported?.Devices == null)
            {
                MessageBox.Show("That file doesn't look like a Sonoff Switch configuration export.", "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Exported files never contain passwords (by design - see Export). Flag any
            // imported device that needs authentication so its password can be re-entered,
            // and make sure a stray plaintext/undecryptable value from a hand-edited file
            // doesn't get used as-is.
            var needsPassword = new System.Collections.Generic.List<string>();
            foreach (var device in imported.Devices)
            {
                if (device.UseAuth && string.IsNullOrEmpty(device.Password))
                    needsPassword.Add(device.Name);
                device.Password = PasswordProtector.EnsureEncrypted(device.Password);
            }

            Devices.Clear();
            foreach (var device in imported.Devices)
                Devices.Add(new DeviceRow(device));

            PollIntervalBox.Text = (imported.PollIntervalMs > 0 ? imported.PollIntervalMs : _plugin.Settings.PollIntervalMs).ToString();

            if (!TrySaveSettings())
                return;

            RefreshStatusText();

            var message = "Configuration imported.";
            if (needsPassword.Count > 0)
                message += "\n\nPasswords are not stored in export files. These devices use authentication and need their password re-entered:\n" + string.Join("\n", needsPassword);
            MessageBox.Show(message, "Sonoff plugin", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
