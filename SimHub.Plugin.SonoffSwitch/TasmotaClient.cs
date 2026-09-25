using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SimHub.Plugin.SonoffSwitch
{
    public class TasmotaStatus
    {
        public bool PowerOn { get; set; }
        public double? Temperature { get; set; }
        public double? Humidity { get; set; }

        /// <summary>Raw JSON last received for the sensor query, kept for diagnostics when
        /// Temperature/Humidity come back empty even though the request succeeded.</summary>
        public string RawSensorJson { get; set; }
    }

    /// <summary>
    /// Talks to a Sonoff TH10/TH16 running Tasmota firmware over its local HTTP API
    /// (the "cm?cmnd=" console endpoint). This is the supported control path for this
    /// plugin: stock eWeLink firmware only exposes a cloud API and is not handled here.
    ///
    /// Power state and sensor readings are fetched with two separate, narrowly-scoped
    /// commands ("Power" and "Status 8") rather than the combined "Status 0" blob: the
    /// shape of "Status 0" has changed across Tasmota firmware versions (older releases,
    /// e.g. 6.x, do not reliably nest StatusSTS/StatusSNS the same way newer ones do),
    /// while "Power" and "Status 8" have been stable since early Tasmota releases.
    /// </summary>
    public class TasmotaClient
    {
        private readonly HttpClient _http;

        public TasmotaClient(int timeoutMs)
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(Math.Max(500, timeoutMs))
            };
        }

        private static string BuildBaseUrl(SonoffDeviceConfig device)
        {
            var host = device.IpAddress?.Trim();
            if (string.IsNullOrEmpty(host))
                throw new ArgumentException("Device IP address is not set.");
            return $"http://{host}";
        }

        private HttpRequestMessage BuildRequest(SonoffDeviceConfig device, string command)
        {
            var url = $"{BuildBaseUrl(device)}/cm?cmnd={Uri.EscapeDataString(command)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (device.UseAuth)
            {
                // Tasmota's web login is always "admin"; its UI only asks for a password,
                // so a blank username here should still authenticate rather than silently
                // sending no credentials.
                var username = string.IsNullOrWhiteSpace(device.Username) ? "admin" : device.Username.Trim();
                var plainPassword = PasswordProtector.Decrypt(device.Password);
                var raw = $"{username}:{plainPassword}";
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
            }

            return request;
        }

        private async Task<string> SendCommandAsync(SonoffDeviceConfig device, string command)
        {
            using (var request = BuildRequest(device, command))
            using (var response = await _http.SendAsync(request).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Retrieves power state ("Power") and sensor readings ("Status 8") from the device.
        /// </summary>
        public async Task<TasmotaStatus> GetStatusAsync(SonoffDeviceConfig device)
        {
            var result = new TasmotaStatus();

            var powerBody = await SendCommandAsync(device, "Power").ConfigureAwait(false);
            result.PowerOn = ParsePower(powerBody);

            var sensorBody = await SendCommandAsync(device, "Status 8").ConfigureAwait(false);
            result.RawSensorJson = sensorBody;
            ParseSensors(sensorBody, result);

            return result;
        }

        /// <summary>
        /// Sends a Power On / Off / Toggle command. <paramref name="on"/> is true for On,
        /// false for Off, and null for Toggle.
        /// </summary>
        public async Task<bool> SetPowerAsync(SonoffDeviceConfig device, bool? on)
        {
            var arg = on == null ? "Toggle" : (on.Value ? "On" : "Off");
            var body = await SendCommandAsync(device, $"Power {arg}").ConfigureAwait(false);
            return ParsePower(body);
        }

        /// <summary>
        /// Parses the JSON returned by Tasmota's "Power" command. Single-relay devices
        /// (TH10/TH16) return {"POWER":"ON"}; multi-relay devices use POWER1, POWER2, etc,
        /// so POWER1 is checked as a fallback for completeness.
        /// </summary>
        internal static bool ParsePower(string json)
        {
            var root = JObject.Parse(json);
            var token = root["POWER"] ?? root["POWER1"];
            return token != null && string.Equals((string)token, "ON", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses the JSON returned by Tasmota's "Status 8" (StatusSNS) command. Sensor
        /// readings are usually nested under StatusSNS, inside a child object whose name
        /// depends on the sensor model wired to the device (AM2301, SI7021, DS18B20,
        /// SHT3X, etc.), so we scan for the first child object exposing Temperature/
        /// Humidity fields rather than hardcoding a sensor name. Some firmware/sensor
        /// combinations report Temperature/Humidity directly on StatusSNS with no nested
        /// object, so that flat shape is checked too.
        /// </summary>
        internal static void ParseSensors(string json, TasmotaStatus result)
        {
            var root = JObject.Parse(json);
            var sns = (root["StatusSNS"] as JObject) ?? root;

            var flatTemp = sns["Temperature"];
            var flatHum = sns["Humidity"];
            if (flatTemp != null)
                result.Temperature = flatTemp.Value<double>();
            if (flatHum != null)
                result.Humidity = flatHum.Value<double>();

            if (result.Temperature != null && result.Humidity != null)
                return;

            foreach (var property in sns.Properties())
            {
                if (!(property.Value is JObject sensorObj))
                    continue;

                var temp = sensorObj["Temperature"];
                var hum = sensorObj["Humidity"];

                if (temp != null && result.Temperature == null)
                    result.Temperature = temp.Value<double>();

                if (hum != null && result.Humidity == null)
                    result.Humidity = hum.Value<double>();
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
