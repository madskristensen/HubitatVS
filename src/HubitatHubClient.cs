using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HubitatVS
{
    internal sealed class HubitatHubClient : IDisposable
    {
        private readonly HubitatHubConfig _config;
        private readonly HttpClient _http;
        private string _sessionCookie = string.Empty;
        private bool _prepared;

        public HubitatHubClient(HubitatHubConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            };
            // Strip any scheme the user may have typed (e.g. "http://192.168.1.1" → "192.168.1.1")
            var host = config.Host ?? string.Empty;
            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(7);
            else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(8);
            host = host.TrimEnd('/');

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://{host}")
            };
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            await EnsureAuthenticatedAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/hub2/hubData");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (!string.IsNullOrEmpty(_sessionCookie))
                request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            return json.Contains("\"hubId\"");
        }

        public async Task<string> LoginAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_config.Username) || string.IsNullOrEmpty(_config.Password))
                return string.Empty;

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", _config.Username),
                new KeyValuePair<string, string>("password", _config.Password),
                new KeyValuePair<string, string>("submit", "Login")
            });

            var response = await _http.PostAsync("/login", content, ct);

            if (response.Headers.TryGetValues("set-cookie", out var cookieValues))
            {
                var first = System.Linq.Enumerable.FirstOrDefault(cookieValues);
                _sessionCookie = first != null ? first.Split(';')[0] : string.Empty;
            }

            return _sessionCookie;
        }

        private async Task EnsureAuthenticatedAsync(CancellationToken ct)
        {
            if (_prepared) return;
            _prepared = true;
            if (!string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.Password))
                await LoginAsync(ct);
        }

        public async Task<HubitatPublishResult> PublishDriverAsync(
            string source,
            CancellationToken ct = default)
        {
            await EnsureAuthenticatedAsync(ct);

            var (name, ns) = ParseGroovyDefinition(source);
            if (string.IsNullOrEmpty(name))
                return new HubitatPublishResult
                {
                    Success = false,
                    Message = "Could not parse driver name from the Groovy definition() block."
                };

            var existingId = await FindDriverOnHubAsync(name, ns, ct);
            return existingId.HasValue
                ? await UpdateDriverAsync(existingId.Value, source, ct)
                : await CreateDriverAsync(source, ct);
        }

        private async Task<int?> FindDriverOnHubAsync(string name, string ns, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "/hub2/userDeviceTypes");
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (!string.IsNullOrEmpty(_sessionCookie))
                    req.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);

                var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                var list = JsonConvert.DeserializeObject<List<DriverListEntry>>(json);
                if (list == null) return null;

                var match = list.FirstOrDefault(d =>
                    string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(ns) || string.Equals(d.Namespace, ns, StringComparison.OrdinalIgnoreCase)));

                return match?.Id;
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
                return null;
            }
        }

        private static (string name, string ns) ParseGroovyDefinition(string source)
        {
            // Matches: definition(name: "My Driver", namespace: "myns", ...)
            // name and namespace can appear in either order and use single or double quotes
            var nameMatch = Regex.Match(source,
                @"definition\s*\([^)]*\bname\s*:\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var nsMatch = Regex.Match(source,
                @"definition\s*\([^)]*\bnamespace\s*:\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return (nameMatch.Success ? nameMatch.Groups[1].Value : string.Empty,
                    nsMatch.Success ? nsMatch.Groups[1].Value : string.Empty);
        }

        private async Task<HubitatPublishResult> UpdateDriverAsync(
            int driverId,
            string source,
            CancellationToken ct)
        {
            using var codeRequest = new HttpRequestMessage(HttpMethod.Get, $"/driver/ajax/code?id={driverId}");
            codeRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (!string.IsNullOrEmpty(_sessionCookie))
                codeRequest.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);

            var codeResponse = await _http.SendAsync(codeRequest, ct);
            if (!codeResponse.IsSuccessStatusCode)
                return new HubitatPublishResult
                {
                    Success = false,
                    Message = $"Failed to fetch driver {driverId}: {codeResponse.StatusCode}"
                };

            var codeJson = await codeResponse.Content.ReadAsStringAsync();
            var codeData = JsonConvert.DeserializeObject<DriverCodeResponse>(codeJson);
            if (codeData == null)
                return new HubitatPublishResult { Success = false, Message = "Failed to parse driver code response." };

            var updateContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", driverId.ToString()),
                new KeyValuePair<string, string>("version", codeData.Version.ToString()),
                new KeyValuePair<string, string>("source", source)
            });

            using var updateRequest = new HttpRequestMessage(HttpMethod.Post, "/driver/ajax/update");
            if (!string.IsNullOrEmpty(_sessionCookie))
                updateRequest.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            updateRequest.Content = updateContent;

            var updateResponse = await _http.SendAsync(updateRequest, ct);
            var updateJson = await updateResponse.Content.ReadAsStringAsync();
            var updateData = JsonConvert.DeserializeObject<DriverUpdateResponse>(updateJson);

            if (updateData?.Status == "success")
                return new HubitatPublishResult
                {
                    Success = true,
                    Message = $"Updated driver ID {driverId} to version {updateData.Version}.",
                    DriverId = driverId
                };

            return new HubitatPublishResult
            {
                Success = false,
                Message = $"Update failed: {updateData?.ErrorMessage ?? "Unknown error"}"
            };
        }

        private async Task<HubitatPublishResult> CreateDriverAsync(
            string source,
            CancellationToken ct)
        {
            var createContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", ""),
                new KeyValuePair<string, string>("version", ""),
                new KeyValuePair<string, string>("create", ""),
                new KeyValuePair<string, string>("source", source)
            });

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/driver/save");
            if (!string.IsNullOrEmpty(_sessionCookie))
                createRequest.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            createRequest.Content = createContent;

            var createResponse = await _http.SendAsync(createRequest, ct);

            if (createResponse.StatusCode == System.Net.HttpStatusCode.Redirect ||
                createResponse.StatusCode == System.Net.HttpStatusCode.Found)
            {
                var location = createResponse.Headers.Location?.ToString() ?? string.Empty;
                const string editSegment = "/driver/editor/";
                var segIdx = location.IndexOf(editSegment, StringComparison.OrdinalIgnoreCase);
                if (segIdx >= 0)
                {
                    var idStr = location.Substring(segIdx + editSegment.Length).TrimEnd('/');
                    if (int.TryParse(idStr, out int newId))
                        return new HubitatPublishResult
                        {
                            Success = true,
                            Message = $"Created new driver with ID {newId}.",
                            DriverId = newId
                        };
                }

                return new HubitatPublishResult
                {
                    Success = false,
                    Message = $"Driver created but could not parse ID from location: {location}"
                };
            }

            return new HubitatPublishResult
            {
                Success = false,
                Message = $"Create failed with status: {createResponse.StatusCode}"
            };
        }

        public void Dispose() => _http?.Dispose();

        private class DriverListEntry
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("namespace")]
            public string Namespace { get; set; }
        }

        private class DriverCodeResponse
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("source")]
            public string Source { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }
        }

        private class DriverUpdateResponse
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("errorMessage")]
            public string ErrorMessage { get; set; }
        }
    }
}
