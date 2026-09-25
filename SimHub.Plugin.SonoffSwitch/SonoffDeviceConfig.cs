using System;

namespace SimHub.Plugin.SonoffSwitch
{
    /// <summary>
    /// Persisted configuration for a single Sonoff TH10/TH16 switch, flashed with
    /// Tasmota firmware and reachable over the local network.
    /// </summary>
    [Serializable]
    public class SonoffDeviceConfig
    {
        /// <summary>Friendly name used to build the SimHub property names for this device.</summary>
        public string Name { get; set; } = "Switch1";

        /// <summary>IP address or hostname of the device on the local network.</summary>
        public string IpAddress { get; set; } = "192.168.1.100";

        /// <summary>Whether the device's Tasmota web UI is protected with a username/password.</summary>
        public bool UseAuth { get; set; } = false;

        public string Username { get; set; } = "";

        /// <summary>
        /// Stored, encrypted (DPAPI) form of the device's web admin password. Never holds
        /// plaintext at rest. Use <see cref="PasswordProtector"/> to encrypt/decrypt when
        /// reading or writing this value; the settings UI does this through
        /// <see cref="DeviceRow.PlainPassword"/> rather than touching this property directly.
        /// </summary>
        public string Password { get; set; } = "";

        /// <summary>Unique id, used internally to keep runtime state keyed independently of the display name.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Shallow copy, so the settings UI can edit a device without mutating the live
        /// instance the plugin is polling with until the user actually saves.
        /// </summary>
        public SonoffDeviceConfig Clone()
        {
            return (SonoffDeviceConfig)MemberwiseClone();
        }

        public override string ToString()
        {
            return $"{Name} ({IpAddress})";
        }
    }
}
