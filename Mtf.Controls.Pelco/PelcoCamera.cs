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
        public const string PelcoConfigurationUrn = "urn:schemas-pelco-com:service:PelcoConfiguration:1";
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
            return SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrn, "GetModelName", "modelName");
        }

        public string GetLocation()
        {
            return SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrn, "GetLocation", "location");
        }

        public string GetConfiguration()
        {
            return SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrn, "GetConfiguration", "pelcoConfig");
        }

        public void Restart()
        {
            _ = SoapClient.SendRequest(SoapUrl, PelcoConfigurationUrn, "Restart", null,
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

        public async Task<bool> RebootAsync()
        {
            var baseUri = new Uri($"http://{IpAddress}");
            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler { CookieContainer = cookieContainer };

            try
            {
                using (var httpClient = new HttpClient(handler) { BaseAddress = baseUri })
                {
                    // Authenticate
                    var authContent = new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            { "username", Username },
                            { "password", Password }
                        });

                    var authResponse = await httpClient.PostAsync("auth/validate", authContent);
                    if (authResponse.StatusCode != HttpStatusCode.Found)
                    {
                        Console.WriteLine("Authentication failed.");
                        return false;
                    }

                    // Extract cookies
                    if (authResponse.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
                    {
                        foreach (var cookieHeader in cookieHeaders)
                        {
                            cookieContainer.SetCookies(baseUri, cookieHeader);
                        }
                    }

                    // Reboot
                    var rebootResponse = await httpClient.GetAsync("setup/system/general/reboot");
                    if (rebootResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Reboot request succeeded.");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("Reboot request failed.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reboot request failed: {ex.Message}");
                throw;
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