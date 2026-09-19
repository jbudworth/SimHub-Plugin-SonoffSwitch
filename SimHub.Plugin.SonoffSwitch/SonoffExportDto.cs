using System.Collections.Generic;

namespace SimHub.Plugin.SonoffSwitch
{
    /// <summary>
    /// Export-only shape for a device's configuration. Deliberately has no Password
    /// property at all (rather than a blanked-out one) so credential material can
    /// never end up in an exported file, even by accident.
    /// </summary>
    public class SonoffDeviceExportDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public bool UseAuth { get; set; }
        public string Username { get; set; }
    }

    /// <summary>Export-only shape for the plugin's settings, built from <see cref="SonoffDeviceExportDto"/>.</summary>
    public class SonoffPluginSettingsExportDto
    {
        public List<SonoffDeviceExportDto> Devices { get; set; } = new List<SonoffDeviceExportDto>();
        public int PollIntervalMs { get; set; }
        public int RequestTimeoutMs { get; set; }
    }
}
