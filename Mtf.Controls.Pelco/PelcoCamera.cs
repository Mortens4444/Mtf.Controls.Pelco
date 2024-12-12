using Mtf.Network;
using Mtf.Network.EventArg;
using Mtf.Network.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

namespace Mtf.Controls.Pelco
{
    public class PelcoCamera
    {
        public const string PelcoConfigurationUrm = "urn:schemas-pelco-com:service:PelcoConfiguration:1";
        public const string MenuControlUrn = "urn:schemas-pelco-com:service:MenuControl:1";

        public string IpAddress { get; set; } = "192.168.0.20";

        public string Username { get; set; } = "admin";

        public string Password { get; set; } = "admin";

        protected int StreamId { get; set; } = 1;

        public string OnvifUri { get; set; }

        public string JpegUri => $"http://{IpAddress}/jpeg";

        public string H264Uri => $"rtspu://{IpAddress}/stream{StreamId}";

        public Uri SoapUrl => new Uri($"http://{IpAddress}:{Port}/control/PelcoConfiguration-1");

        public ushort Port { get; set; } = 80;

        public override string ToString()
        {
            return IpAddress;
        }

        public string GetModel()
        {
            return SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrm, "GetModelName", "modelName");
        }

        public string GetLocation()
        {
            return SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrm, "GetLocation", "location");
        }

        public string GetConfiguration()
        {
            return SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrm, "GetConfiguration", "pelcoConfig");
        }

        public void Restart()
        {
            _ = SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrm, "Restart", null,
                new SoapParameter
                {
                    Name = "modelName",
                    Value = GetModel()
                });
        }

        public void MenuControlEscape()
        {
            _ = SoapClient.SendRequest(SoapUrl, MenuControlUrn, "Escape", null);
        }

        public void MenuControlEnterMenu()
        {
            _ = SoapClient.SendRequest(SoapUrl, MenuControlUrn, "EnterMenu", null);
        }

        public void MenuControlExitMenu()
        {
            _ = SoapClient.SendRequest(SoapUrl, MenuControlUrn, "ExitMenu", null);
        }

        public static async Task<List<PelcoCamera>> GetPelcoCamerasAsync(string ipAddress = null)
        {
            var discoveredCameras = new List<PelcoCamera>();
            var upnpClient = String.IsNullOrEmpty(ipAddress) ? new UpnpClient() : new UpnpClient(ipAddress);

            var tcs = new TaskCompletionSource<bool>();

            void DeviceDiscoveredHandler(object sender, DeviceDiscoveredEventArgs args)
            {
                if (args.Device.Manufacturer?.Equals("Pelco", StringComparison.OrdinalIgnoreCase) == true)
                {
                    discoveredCameras.Add(new PelcoCamera
                    {
                        IpAddress = args.Device.Location ?? ipAddress
                    });
                }

                tcs.TrySetResult(true);
            }

            try
            {
                upnpClient.DeviceDiscovered += DeviceDiscoveredHandler;
                upnpClient.Connect();
                await upnpClient.SendDiscoveryMessage();

                await Task.WhenAny(tcs.Task, Task.Delay(30000));
            }
            finally
            {
                upnpClient.DeviceDiscovered -= DeviceDiscoveredHandler;
                upnpClient.Dispose();
            }

            return discoveredCameras;
        }

        public async Task RebootAsync()
        {
            using (var httpClient = new HttpClient())
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "username", Username },
                    { "password", Password }
                });

                var uri = new Uri($"http://{IpAddress}/auth/validate");

                var response = await httpClient.PostAsync(uri, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.Found && !String.IsNullOrEmpty(responseContent))
                {
                    var sessionId = ExtractValue(response.Headers, "Set-Cookie", "PHPSESSID=", ";");
                    var authosToken = ExtractValue(response.Headers, "Set-Cookie", "authos-token=", ";");

                    if (!String.IsNullOrEmpty(sessionId) && !String.IsNullOrEmpty(authosToken))
                    {
                        uri = new Uri($"http://{IpAddress}/setup/system/general/");
                        response = await httpClient.GetAsync(uri);

                        var cookieContainer = new CookieContainer();
                        using (var handler = new HttpClientHandler() { CookieContainer = cookieContainer })
                        using (var client = new HttpClient(handler) { BaseAddress = uri })
                        {
                            cookieContainer.Add(uri, new Cookie("PHPSESSID", sessionId));
                            cookieContainer.Add(uri, new Cookie("authos-token", authosToken));
                            var result = await client.GetAsync("reboot");
                            if (result.IsSuccessStatusCode)
                            {
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Unexpected response status or content during reboot request.");
                }
            }
        }

        /// <summary>
        /// Extracts a value from a header field based on the key and delimiters.
        /// </summary>
        private string ExtractValue(HttpResponseHeaders headers, string key, string startDelimiter, string endDelimiter)
        {
            if (headers.TryGetValues(key, out var values))
            {
                var value = values.FirstOrDefault();

                if (!String.IsNullOrEmpty(value))
                {
                    var startIndex = value.IndexOf(startDelimiter);
                    if (startIndex != -1)
                    {
                        startIndex += startDelimiter.Length;
                        var endIndex = value.IndexOf(endDelimiter, startIndex);
                        if (endIndex > startIndex)
                        {
                            return value.Substring(startIndex, endIndex - startIndex);
                        }
                    }
                }
            }
            return String.Empty;
        }
    }
}