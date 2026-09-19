using System;

namespace SimHub.Plugin.SonoffSwitch
{
    /// <summary>
    /// Latest known state for a device, refreshed by the polling loop and read by the
    /// SimHub property delegates. Not persisted to disk.
    /// </summary>
    public class DeviceRuntimeState
    {
        public bool Online { get; set; }
        public bool PowerOn { get; set; }
        public double? Temperature { get; set; }
        public double? Humidity { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public string LastError { get; set; }

        /// <summary>Raw JSON from the last "Status 8" sensor query, kept so the settings UI
        /// can show it when Temperature/Humidity come back empty despite a successful poll.</summary>
        public string LastRawSensorJson { get; set; }
    }
}
