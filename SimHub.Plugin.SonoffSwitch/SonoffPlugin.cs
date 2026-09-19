using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameReaderCommon;
using SimHub.Plugins;

namespace SimHub.Plugin.SonoffSwitch
{
    [PluginDescription("Control Sonoff TH10/TH16 wifi smart switches running Tasmota firmware, and expose their temperature/humidity readings as SimHub properties.")]
    [PluginAuthor("Claude.ai")]
    [PluginName("Sonoff TH10/TH16 Control")]
    public class SonoffPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string SettingsKey = "SonoffPluginSettings";

        public PluginManager PluginManager { get; set; }

        public SonoffPluginSettings Settings { get; private set; }

        private readonly TasmotaClient _client = new TasmotaClient(3000);
        private readonly Dictionary<string, DeviceRuntimeState> _states = new Dictionary<string, DeviceRuntimeState>();
        private readonly object _stateLock = new object();
        private Timer _pollTimer;
        private bool _pollInProgress;

        public string LeftMenuTitle => "Sonoff TH10/TH16";

        public ImageSource PictureIcon => BuildIcon();

        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        /// <summary>
        /// Same generated sidebar icon used by the SimHub.Plugin.TapoSwitch plugin: a
        /// wall-outlet/socket face (circular plate, two vertical slots, a round ground
        /// hole) rendered as a plain white silhouette on a transparent background.
        /// SimHub treats sidebar icons as template glyphs - it recolors every pixel with
        /// any opacity to solid white and uses the alpha channel purely as a mask - so
        /// the fill color drawn here is irrelevant, only the shape's coverage matters.
        /// Generated in code rather than shipped as an embedded asset, so there's no
        /// extra file to manage alongside the DLL.
        /// </summary>
        private static ImageSource BuildIcon()
        {
            try
            {
                var plate = new EllipseGeometry(new System.Windows.Point(32, 32), 26, 26);
                var leftSlot = new RectangleGeometry(new System.Windows.Rect(21, 14, 6, 17), 2, 2);
                var rightSlot = new RectangleGeometry(new System.Windows.Rect(37, 14, 6, 17), 2, 2);
                var groundHole = new EllipseGeometry(new System.Windows.Point(32, 41), 5, 5);

                var outlet = new GeometryGroup { FillRule = FillRule.EvenOdd };
                outlet.Children.Add(plate);
                outlet.Children.Add(leftSlot);
                outlet.Children.Add(rightSlot);
                outlet.Children.Add(groundHole);

                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawGeometry(Brushes.White, null, outlet);
                }
                var bmp = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual);
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;

            Settings = this.ReadCommonSettings<SonoffPluginSettings>(SettingsKey, () => new SonoffPluginSettings());

            var migratedAny = false;
            foreach (var device in Settings.Devices)
            {
                var encrypted = PasswordProtector.EnsureEncrypted(device.Password);
                if (encrypted != device.Password)
                {
                    device.Password = encrypted;
                    migratedAny = true;
                }
            }
            if (migratedAny)
                this.SaveCommonSettings(SettingsKey, Settings);

            foreach (var device in Settings.Devices)
                RegisterDevice(device);

            StartPolling();
        }

        public void End(PluginManager pluginManager)
        {
            StopPolling();
            this.SaveCommonSettings(SettingsKey, Settings);
            _client.Dispose();
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Device polling runs on its own timer, independent of game telemetry updates,
            // so there is nothing to do here. Present because IDataPlugin requires it.
        }

        // ----------------------------------------------------------------------------
        // Device registration and property/action wiring
        // ----------------------------------------------------------------------------

        /// <summary>
        /// Creates runtime state for a device and attaches its SimHub properties and
        /// actions. Safe to call again for a device whose Name changed, since delegate
        /// names are re-attached rather than duplicated by SimHub for the same name.
        /// </summary>
        public void RegisterDevice(SonoffDeviceConfig device)
        {
            lock (_stateLock)
            {
                if (!_states.ContainsKey(device.Id))
                    _states[device.Id] = new DeviceRuntimeState();
            }

            var propertyPrefix = SanitizeName(device.Name);

            this.AttachDelegate($"{propertyPrefix}.Online", () => GetState(device.Id)?.Online ?? false);
            this.AttachDelegate($"{propertyPrefix}.PowerOn", () => GetState(device.Id)?.PowerOn ?? false);
            this.AttachDelegate($"{propertyPrefix}.Temperature", () => GetState(device.Id)?.Temperature);
            this.AttachDelegate($"{propertyPrefix}.Humidity", () => GetState(device.Id)?.Humidity);
            this.AttachDelegate($"{propertyPrefix}.LastUpdate", () => GetState(device.Id)?.LastUpdateUtc.ToLocalTime());

            this.AddAction($"{propertyPrefix}.TurnOn", (a, b) => SendPowerCommand(device, true));
            this.AddAction($"{propertyPrefix}.TurnOff", (a, b) => SendPowerCommand(device, false));
            this.AddAction($"{propertyPrefix}.Toggle", (a, b) => SendPowerCommand(device, null));
        }

        /// <summary>
        /// Drops runtime state for a removed device. The SimHub properties/actions already
        /// attached for it stay registered until SimHub restarts; that is a SimHub SDK
        /// limitation (no "detach"), not something this plugin can undo at runtime.
        /// </summary>
        public void UnregisterDevice(string deviceId)
        {
            lock (_stateLock)
            {
                _states.Remove(deviceId);
            }
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Device";
            var chars = name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
            var cleaned = new string(chars);
            return string.IsNullOrEmpty(cleaned) ? "Device" : cleaned;
        }

        private DeviceRuntimeState GetState(string deviceId)
        {
            lock (_stateLock)
            {
                return _states.TryGetValue(deviceId, out var state) ? state : null;
            }
        }

        /// <summary>Exposed for the settings UI so it can display live status per device.</summary>
        public DeviceRuntimeState GetRuntimeState(string deviceId) => GetState(deviceId);

        /// <summary>Exposed for the settings UI's "Test selected" button: runs one poll immediately.</summary>
        public System.Threading.Tasks.Task TestDeviceAsync(SonoffDeviceConfig device) => PollDeviceAsync(device);

        // ----------------------------------------------------------------------------
        // Polling
        // ----------------------------------------------------------------------------

        private void StartPolling()
        {
            StopPolling();

            _pollTimer = new Timer(Math.Max(500, Settings.PollIntervalMs));
            _pollTimer.Elapsed += async (s, e) => await PollAllDevicesAsync().ConfigureAwait(false);
            _pollTimer.AutoReset = true;
            _pollTimer.Start();
        }

        private void StopPolling()
        {
            if (_pollTimer == null)
                return;
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _pollTimer = null;
        }

        /// <summary>Call after changing Settings.PollIntervalMs from the settings UI.</summary>
        public void RestartPolling()
        {
            StartPolling();
        }

        /// <summary>Persists Settings to disk immediately, so device edits survive a crash/kill of SimHub.</summary>
        public void SaveSettingsNow()
        {
            this.SaveCommonSettings(SettingsKey, Settings);
        }

        private async System.Threading.Tasks.Task PollAllDevicesAsync()
        {
            if (_pollInProgress)
                return; // Skip overlapping polls if a previous cycle is still in flight.
            _pollInProgress = true;

            try
            {
                var devices = Settings.Devices.ToList();
                foreach (var device in devices)
                    await PollDeviceAsync(device).ConfigureAwait(false);
            }
            finally
            {
                _pollInProgress = false;
            }
        }

        private async System.Threading.Tasks.Task PollDeviceAsync(SonoffDeviceConfig device)
        {
            var state = GetState(device.Id);
            if (state == null)
                return;

            try
            {
                var status = await _client.GetStatusAsync(device).ConfigureAwait(false);
                lock (_stateLock)
                {
                    state.Online = true;
                    state.PowerOn = status.PowerOn;
                    state.Temperature = status.Temperature;
                    state.Humidity = status.Humidity;
                    state.LastUpdateUtc = DateTime.UtcNow;
                    state.LastError = null;
                    state.LastRawSensorJson = status.RawSensorJson;
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    state.Online = false;
                    state.LastError = ex.Message;
                }
            }
        }

        private async void SendPowerCommand(SonoffDeviceConfig device, bool? on)
        {
            try
            {
                var isOn = await _client.SetPowerAsync(device, on).ConfigureAwait(false);
                var state = GetState(device.Id);
                if (state != null)
                {
                    lock (_stateLock)
                    {
                        state.PowerOn = isOn;
                        state.Online = true;
                        state.LastError = null;
                    }
                }
            }
            catch (Exception ex)
            {
                var state = GetState(device.Id);
                if (state != null)
                {
                    lock (_stateLock)
                    {
                        state.Online = false;
                        state.LastError = ex.Message;
                    }
                }
            }
        }
    }
}
