using System;
using System.Collections.Generic;

namespace SimHub.Plugin.SonoffSwitch
{
    [Serializable]
    public class SonoffPluginSettings
    {
        public List<SonoffDeviceConfig> Devices { get; set; } = new List<SonoffDeviceConfig>();

        /// <summary>How often, in milliseconds, each device is polled for status and sensor data.</summary>
        public int PollIntervalMs { get; set; } = 5000;

        /// <summary>HTTP request timeout, in milliseconds, for each poll/command call.</summary>
        public int RequestTimeoutMs { get; set; } = 3000;
    }
}
