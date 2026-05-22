using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HubitatVS
{
    internal interface IHubitatHubClient : IDisposable
    {
        Task<bool> TestConnectionAsync(CancellationToken ct = default);
        Task<HubitatPublishResult> PublishAsync(HubitatCodeCandidate candidate, string source, CancellationToken ct = default);
        Task<HubitatPublishResult> PublishToTargetAsync(HubitatCodeCandidate candidate, string source, int? targetId, CancellationToken ct = default);
        Task<IReadOnlyList<HubitatCodeEntry>> GetNamespaceMatchesAsync(HubitatCodeKind kind, string namespaceName, CancellationToken ct = default);
        Task<string> DownloadCodeSourceAsync(HubitatCodeKind kind, int codeId, CancellationToken ct = default);
        Task<HubitatHubInfoEntry> GetAdornmentInfoAsync(HubitatCodeKind kind, string name, string namespaceName, string localSource, CancellationToken ct = default);
    }

    internal sealed class HubitatHubClient : IHubitatHubClient
    {
        private readonly HubitatHubConfig _config;
        private readonly HttpClient _http;
        private string _sessionCookie = string.Empty;
        private bool _prepared;

        private static readonly ConcurrentDictionary<string, HttpClient> ClientPool =
            new ConcurrentDictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan ConnectionProbeTimeout = TimeSpan.FromSeconds(3);

        public HubitatHubClient(HubitatHubConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            var baseAddress = BuildBaseAddress(config.Host);
            var poolKey = baseAddress.GetLeftPart(UriPartial.Authority);

            _http = ClientPool.GetOrAdd(poolKey, _ =>
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                };

                return new HttpClient(handler)
                {
                    BaseAddress = baseAddress
                };
            });
        }

        internal HubitatHubClient(HubitatHubConfig config, HttpClient httpClient)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        private static Uri BuildBaseAddress(string? host)
        {
            var value = (host ?? string.Empty).Trim();
            if (value.Length == 0)
                throw new ArgumentException("Hub host is required.", nameof(host));

            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Hub host must be a valid HTTP or HTTPS URL.", nameof(host));
            }

            var builder = new UriBuilder(parsed)
            {
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri;
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectionProbeTimeout);

            try
            {
                await EnsureAuthenticatedAsync(timeoutCts.Token);
                using var request = CreateRequest(HttpMethod.Get, "/hub2/hubData");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                var response = await _http.SendAsync(request, timeoutCts.Token);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                return json.Contains("\"hubId\"");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
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
                var first = cookieValues.FirstOrDefault();
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

        public async Task<HubitatPublishResult> PublishAsync(
            HubitatCodeCandidate candidate,
            string source,
            CancellationToken ct = default)
        {
            _ = candidate ?? throw new ArgumentNullException(nameof(candidate));
            await EnsureAuthenticatedAsync(ct);

            if (candidate.Kind == HubitatCodeKind.Unknown)
            {
                return CreateFailure(HubitatCodeKind.Unknown, "Could not determine whether this Groovy file is a Hubitat app or driver.");
            }

            if (string.IsNullOrWhiteSpace(candidate.DisplayName))
            {
                var descriptor = GetDescriptor(candidate.Kind);
                return CreateFailure(candidate.Kind, $"Could not parse the {descriptor.Noun} name from the Groovy definition() block.");
            }

            var codeDescriptor = GetDescriptor(candidate.Kind);
            var existingId = await FindCodeOnHubAsync(codeDescriptor, candidate.DisplayName, candidate.NamespaceName, ct);
            return existingId.HasValue
                ? await UpdateCodeAsync(codeDescriptor, existingId.Value, source, ct)
                : await CreateCodeAsync(codeDescriptor, candidate.DisplayName, candidate.NamespaceName, source, ct);
        }

        /// <summary>Publishes to a specific app/driver ID (update) or creates new (targetId = null).</summary>
        public async Task<HubitatPublishResult> PublishToTargetAsync(
            HubitatCodeCandidate candidate,
            string source,
            int? targetId,
            CancellationToken ct = default)
        {
            _ = candidate ?? throw new ArgumentNullException(nameof(candidate));
            await EnsureAuthenticatedAsync(ct);

            if (candidate.Kind == HubitatCodeKind.Unknown)
            {
                return CreateFailure(HubitatCodeKind.Unknown, "Could not determine whether this Groovy file is a Hubitat app or driver.");
            }

            if (string.IsNullOrWhiteSpace(candidate.DisplayName))
            {
                var descriptor = GetDescriptor(candidate.Kind);
                return CreateFailure(candidate.Kind, $"Could not parse the {descriptor.Noun} name from the Groovy definition() block.");
            }

            var codeDescriptor = GetDescriptor(candidate.Kind);
            return targetId.HasValue
                ? await UpdateCodeAsync(codeDescriptor, targetId.Value, source, ct)
                : await CreateCodeAsync(codeDescriptor, candidate.DisplayName, candidate.NamespaceName, source, ct);
        }

        /// <summary>Gets all apps or drivers matching the namespace from the hub.</summary>
        public async Task<IReadOnlyList<HubitatCodeEntry>> GetNamespaceMatchesAsync(
            HubitatCodeKind kind,
            string namespaceName,
            CancellationToken ct = default)
        {
            await EnsureAuthenticatedAsync(ct);
            var descriptor = GetDescriptor(kind);

            using var request = CreateRequest(HttpMethod.Get, descriptor.ListEndpoint);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<HubitatCodeEntry>();

            var json = await response.Content.ReadAsStringAsync();
            var list = JsonConvert.DeserializeObject<List<HubitatCodeListEntry>>(json);
            if (list == null)
                return Array.Empty<HubitatCodeEntry>();

            return list
                .Where(entry => string.Equals(entry.Namespace, namespaceName, StringComparison.OrdinalIgnoreCase))
                .Select(entry => new HubitatCodeEntry
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Namespace = entry.Namespace
                })
                .ToArray();
        }

        private async Task<int?> FindCodeOnHubAsync(
            HubitatCodeDescriptor descriptor,
            string name,
            string namespaceName,
            CancellationToken ct)
        {
            using var request = CreateRequest(HttpMethod.Get, descriptor.ListEndpoint);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var list = JsonConvert.DeserializeObject<List<HubitatCodeListEntry>>(json);
            if (list == null) return null;

            var exactMatch = FindExactMatch(list, name, namespaceName);
            return exactMatch?.Id;
        }

        /// <summary>Downloads the source code of an existing app/driver from the hub.</summary>
        public async Task<string> DownloadCodeSourceAsync(
            HubitatCodeKind kind,
            int codeId,
            CancellationToken ct = default)
        {
            await EnsureAuthenticatedAsync(ct);
            var descriptor = GetDescriptor(kind);

            using var codeRequest = CreateRequest(HttpMethod.Get, $"{descriptor.EditorBasePath}/ajax/code?id={codeId}");
            codeRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

            var codeResponse = await _http.SendAsync(codeRequest, ct);
            var codeJson = await codeResponse.Content.ReadAsStringAsync();
            if (!codeResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var codeData = JsonConvert.DeserializeObject<HubitatCodeResponse>(codeJson);
            return codeData?.Source;
        }

        private async Task<HubitatPublishResult> UpdateCodeAsync(
            HubitatCodeDescriptor descriptor,
            int codeId,
            string source,
            CancellationToken ct)
        {
            using var codeRequest = CreateRequest(HttpMethod.Get, $"{descriptor.EditorBasePath}/ajax/code?id={codeId}");
            codeRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

            var codeResponse = await _http.SendAsync(codeRequest, ct);
            var codeJson = await codeResponse.Content.ReadAsStringAsync();
            if (!codeResponse.IsSuccessStatusCode)
            {
                return CreateFailure(
                    descriptor.Kind,
                    $"Failed to fetch {descriptor.Noun} {codeId}: {codeResponse.StatusCode}",
                    $"Response body: {FormatDiagnosticText(codeJson)}");
            }

            var codeData = JsonConvert.DeserializeObject<HubitatCodeResponse>(codeJson);
            if (codeData == null)
            {
                return CreateFailure(
                    descriptor.Kind,
                    $"Failed to parse {descriptor.Noun} code response.",
                    $"Raw response: {FormatDiagnosticText(codeJson)}");
            }

            if (NormalizeSource(codeData.Source) == NormalizeSource(source))
            {
                return new HubitatPublishResult
                {
                    Success = true,
                    Message = $"Already up to date (version {codeData.Version}).",
                    CodeId = codeId,
                    PublishedVersion = codeData.Version,
                    CodeKind = descriptor.Kind
                };
            }

            if (descriptor.Kind == HubitatCodeKind.App)
            {
                return await SaveAppCodeJsonAsync(codeId, codeData.Version, source, ct);
            }

            var updateContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", codeId.ToString()),
                new KeyValuePair<string, string>("version", codeData.Version.ToString()),
                new KeyValuePair<string, string>("source", source)
            });

            using var updateRequest = CreateRequest(HttpMethod.Post, $"{descriptor.EditorBasePath}/ajax/update");
            updateRequest.Content = updateContent;

            var updateResponse = await _http.SendAsync(updateRequest, ct);
            var updateJson = await updateResponse.Content.ReadAsStringAsync();
            var updateData = JsonConvert.DeserializeObject<HubitatUpdateResponse>(updateJson);

            if (updateData?.Status == "success")
            {
                return new HubitatPublishResult
                {
                    Success = true,
                    Message = $"Updated {descriptor.Noun} ID {codeId} to version {updateData.Version}.",
                    CodeId = codeId,
                    PublishedVersion = updateData.Version,
                    CodeKind = descriptor.Kind
                };
            }

            return CreateFailure(
                descriptor.Kind,
                $"{descriptor.NounCapitalized} update failed: {updateData?.ErrorMessage ?? "Unknown error"}",
                $"HTTP {(int)updateResponse.StatusCode} {updateResponse.StatusCode}\r\nRaw response: {FormatDiagnosticText(updateJson)}");
        }

        private async Task<HubitatPublishResult> CreateCodeAsync(
            HubitatCodeDescriptor descriptor,
            string name,
            string namespaceName,
            string source,
            CancellationToken ct)
        {
            if (descriptor.Kind == HubitatCodeKind.App)
            {
                return await SaveAppCodeJsonAsync(null, null, source, ct);
            }

            var createContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("id", string.Empty),
                new KeyValuePair<string, string>("version", string.Empty),
                new KeyValuePair<string, string>("create", string.Empty),
                new KeyValuePair<string, string>("source", source)
            });

            using var createRequest = CreateRequest(HttpMethod.Post, $"{descriptor.EditorBasePath}/save");
            createRequest.Content = createContent;

            var createResponse = await _http.SendAsync(createRequest, ct);
            var responseBody = await createResponse.Content.ReadAsStringAsync();
            if (!IsCreateAccepted(createResponse.StatusCode))
            {
                return CreateFailure(
                    descriptor.Kind,
                    $"Create failed with status: {createResponse.StatusCode}",
                    $"Response body: {FormatDiagnosticText(responseBody)}");
            }

            var newId = TryParseCreatedCodeId(descriptor, createResponse.Headers.Location?.ToString(), responseBody)
                ?? await FindCodeOnHubAsync(descriptor, name, namespaceName, ct);
            if (!newId.HasValue)
            {
                return CreateFailure(descriptor.Kind, $"{descriptor.NounCapitalized} save completed but the hub did not return a new {descriptor.Noun} ID.");
            }

            var publishedVersion = await GetCodeVersionAsync(descriptor, newId.Value, ct);

            return new HubitatPublishResult
            {
                Success = true,
                Message = $"Created new {descriptor.Noun} with ID {newId.Value}.",
                CodeId = newId.Value,
                PublishedVersion = publishedVersion,
                CodeKind = descriptor.Kind
            };
        }

        private async Task<HubitatPublishResult> SaveAppCodeJsonAsync(
            int? codeId,
            int? version,
            string source,
            CancellationToken ct)
        {
            var requestDetails = $"Request payload: id={(codeId?.ToString() ?? "<new>")}, version={(version?.ToString() ?? "<null>")}";
            var payload = JsonConvert.SerializeObject(new HubitatAppSaveRequest
            {
                Id = codeId,
                Version = version,
                Source = source
            });

            using var request = CreateRequest(HttpMethod.Post, "/app/saveOrUpdateJson");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<HubitatAppSaveResponse>(json);
            if (data?.Success == true)
            {
                var publishedId = data.Id ?? codeId;
                var operation = codeId.HasValue ? "Updated" : "Created new";
                var suffix = publishedId.HasValue ? $" app with ID {publishedId.Value}." : " app.";

                return new HubitatPublishResult
                {
                    Success = true,
                    Message = !string.IsNullOrWhiteSpace(data.Message) ? data.Message : $"{operation}{suffix}",
                    CodeId = publishedId,
                    PublishedVersion = data.Version ?? version,
                    CodeKind = HubitatCodeKind.App
                };
            }

            var failureMessage = data?.Message;
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                return CreateFailure(
                    HubitatCodeKind.App,
                    failureMessage!,
                    $"{requestDetails}\r\nHTTP {(int)response.StatusCode} {response.StatusCode}\r\nRaw response: {FormatDiagnosticText(json)}");
            }

            return CreateFailure(
                HubitatCodeKind.App,
                $"App save failed: {response.StatusCode}",
                $"{requestDetails}\r\nRaw response: {FormatDiagnosticText(json)}");
        }

        private async Task<int?> GetCodeVersionAsync(HubitatCodeDescriptor descriptor, int codeId, CancellationToken ct)
        {
            using var codeRequest = CreateRequest(HttpMethod.Get, $"{descriptor.EditorBasePath}/ajax/code?id={codeId}");
            codeRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

            var codeResponse = await _http.SendAsync(codeRequest, ct);
            if (!codeResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var codeJson = await codeResponse.Content.ReadAsStringAsync();
            var codeData = JsonConvert.DeserializeObject<HubitatCodeResponse>(codeJson);
            return codeData?.Version;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string requestUri)
        {
            var request = new HttpRequestMessage(method, requestUri);
            if (!string.IsNullOrEmpty(_sessionCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            }

            return request;
        }

        private static int? TryParseCreatedCodeId(HubitatCodeDescriptor descriptor, string? location, string responseBody)
        {
            if (!string.IsNullOrWhiteSpace(location))
            {
                var locationMatch = Regex.Match(location, $@"{Regex.Escape(descriptor.EditorBasePath)}/editor/(?<id>\d+)", RegexOptions.IgnoreCase);
                if (locationMatch.Success && int.TryParse(locationMatch.Groups["id"].Value, out int locationId))
                {
                    return locationId;
                }
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            var variableMatch = Regex.Match(
                responseBody,
                $@"global{descriptor.NounCapitalized}IdToEdit\s*=\s*(?<id>\d+)",
                RegexOptions.IgnoreCase);
            if (variableMatch.Success && int.TryParse(variableMatch.Groups["id"].Value, out int variableId))
            {
                return variableId;
            }

            return null;
        }

        private static bool IsCreateAccepted(HttpStatusCode statusCode)
            => statusCode == HttpStatusCode.OK
            || statusCode == HttpStatusCode.Created
            || statusCode == HttpStatusCode.Redirect
            || statusCode == HttpStatusCode.Found
            || statusCode == HttpStatusCode.SeeOther;

        private static HubitatCodeDescriptor GetDescriptor(HubitatCodeKind kind) => kind switch
        {
            HubitatCodeKind.Driver => new HubitatCodeDescriptor(HubitatCodeKind.Driver, "driver", "Driver", "/hub2/userDeviceTypes", "/driver"),
            HubitatCodeKind.App => new HubitatCodeDescriptor(HubitatCodeKind.App, "app", "App", "/hub2/userAppTypes", "/app"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        private static HubitatPublishResult CreateFailure(HubitatCodeKind kind, string message, string details = "")
            => new HubitatPublishResult
            {
                Success = false,
                Message = message,
                Details = details,
                CodeKind = kind
            };

        /// <summary>Fetches hub metadata for the viewport adornment.</summary>
        public async Task<HubitatHubInfoEntry> GetAdornmentInfoAsync(
            HubitatCodeKind kind,
            string name,
            string namespaceName,
            string localSource,
            CancellationToken ct = default)
        {
            await EnsureAuthenticatedAsync(ct);
            var descriptor = GetDescriptor(kind);

            using var listRequest = CreateRequest(HttpMethod.Get, descriptor.ListEndpoint);
            listRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
            var listResponse = await _http.SendAsync(listRequest, ct);
            if (!listResponse.IsSuccessStatusCode)
                return new HubitatHubInfoEntry { HubName = _config.Name, Kind = kind, ConnectionError = true };

            var listJson = await listResponse.Content.ReadAsStringAsync();
            var entries = JsonConvert.DeserializeObject<List<HubitatCodeListEntry>>(listJson);
            var match = entries == null ? null : FindExactMatch(entries, name, namespaceName);

            if (match == null)
                return new HubitatHubInfoEntry { HubName = _config.Name, Found = false, Kind = kind };

            var detailPath = $"{descriptor.EditorBasePath}/list/single/data/{match.Id}";
            using var detailRequest = CreateRequest(HttpMethod.Get, detailPath);
            detailRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
            var detailResponse = await _http.SendAsync(detailRequest, ct);
            if (!detailResponse.IsSuccessStatusCode)
                return new HubitatHubInfoEntry { HubName = _config.Name, Found = false, Kind = kind, ConnectionError = true };

            var detailJson = await detailResponse.Content.ReadAsStringAsync();
            var trimmedDetailJson = detailJson?.TrimStart();
            var detail = trimmedDetailJson != null && trimmedDetailJson.StartsWith("[", StringComparison.Ordinal)
                ? JsonConvert.DeserializeObject<List<HubitatCodeDetailEntry>>(detailJson)?.FirstOrDefault()
                : JsonConvert.DeserializeObject<HubitatCodeDetailEntry>(detailJson);
            if (detail == null)
                return new HubitatHubInfoEntry { HubName = _config.Name, Found = false, Kind = kind, ConnectionError = true };

            DateTimeOffset? lastModified = null;
            if (!string.IsNullOrEmpty(match.LastModified) &&
                DateTimeOffset.TryParse(match.LastModified, out var lm))
                lastModified = lm;

            var usedByCount = match.UsedBy?.Count ?? 0;
            var count = kind == HubitatCodeKind.Driver
                ? (detail.InstalledDriverCount ?? usedByCount)
                : (detail.InstalledAppCount ?? usedByCount);

            return new HubitatHubInfoEntry
            {
                HubName = _config.Name,
                Found = true,
                CodeId = match.Id,
                Version = detail.Version,
                InstalledCount = count,
                Kind = kind,
                LastModified = lastModified,
                IsInSync = NormalizeSource(detail.Source) == NormalizeSource(localSource),
                UsedByNames = match.UsedBy?.Select(u => u.Name).ToArray() ?? Array.Empty<string>(),
            };
        }

        public void Dispose() { }

        private static HubitatCodeListEntry FindExactMatch(
            IEnumerable<HubitatCodeListEntry> entries,
            string name,
            string namespaceName)
        {
            var entryList = entries?.ToList();
            if (entryList == null || entryList.Count == 0)
            {
                return null;
            }

            var normalizedNamespace = NormalizeComparableText(namespaceName);
            if (!string.IsNullOrEmpty(normalizedNamespace))
            {
                var namespaced = entryList
                    .Where(entry => string.Equals(NormalizeComparableText(entry.Namespace), normalizedNamespace, StringComparison.Ordinal))
                    .ToList();

                var namespacedMatch = MatchByName(namespaced, name);
                if (namespacedMatch != null)
                {
                    return namespacedMatch;
                }
            }

            return MatchByName(entryList, name);
        }

        private static HubitatCodeListEntry MatchByName(IReadOnlyList<HubitatCodeListEntry> entries, string name)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            var normalizedName = NormalizeComparableText(name);
            var canonicalName = CanonicalizeName(name);

            var exact = entries.FirstOrDefault(entry =>
                string.Equals(NormalizeComparableText(entry.Name), normalizedName, StringComparison.Ordinal));
            if (exact != null)
            {
                return exact;
            }

            var canonicalMatches = entries.Where(entry =>
                    string.Equals(CanonicalizeName(entry.Name), canonicalName, StringComparison.Ordinal))
                .ToList();

            if (canonicalMatches.Count == 1)
            {
                return canonicalMatches[0];
            }

            return canonicalMatches.FirstOrDefault(entry =>
                string.Equals(NormalizeComparableText(entry.Name), normalizedName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeComparableText(string? value)
        {
            var decoded = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
            return Regex.Replace(decoded, @"\s+", " ");
        }

        private static string CanonicalizeName(string? value)
        {
            var comparable = NormalizeComparableText(value);
            if (comparable.Length == 0)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder(comparable.Length);
            foreach (var ch in comparable)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    buffer.Append(char.ToLowerInvariant(ch));
                }
            }

            return buffer.ToString();
        }

        private static string NormalizeSource(string? source)
            => (source ?? string.Empty).Replace("\r\n", "\n").Trim();

        private static string FormatDiagnosticText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "<empty>";
            }

            const int maxLength = 1200;
            var trimmed = text.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed.Substring(0, maxLength) + "…";
        }

        private sealed class HubitatCodeDescriptor
        {
            public HubitatCodeDescriptor(
                HubitatCodeKind kind,
                string noun,
                string nounCapitalized,
                string listEndpoint,
                string editorBasePath)
            {
                Kind = kind;
                Noun = noun;
                NounCapitalized = nounCapitalized;
                ListEndpoint = listEndpoint;
                EditorBasePath = editorBasePath;
            }

            public HubitatCodeKind Kind { get; }

            public string Noun { get; }

            public string NounCapitalized { get; }

            public string ListEndpoint { get; }

            public string EditorBasePath { get; }
        }

        private sealed class HubitatCodeListEntry
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("namespace")]
            public string Namespace { get; set; } = string.Empty;

            [JsonProperty("lastModified")]
            public string LastModified { get; set; } = string.Empty;

            [JsonProperty("usedBy")]
            public List<HubitatUsedByEntry> UsedBy { get; set; } = new List<HubitatUsedByEntry>();
        }

        private sealed class HubitatUsedByEntry
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;
        }

        private sealed class HubitatCodeDetailEntry
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("source")]
            public string Source { get; set; } = string.Empty;

            [JsonProperty("installedDriverCount")]
            public int? InstalledDriverCount { get; set; }

            [JsonProperty("installedAppCount")]
            public int? InstalledAppCount { get; set; }
        }

        private sealed class HubitatCodeResponse
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("source")]
            public string Source { get; set; } = string.Empty;

            [JsonProperty("status")]
            public string Status { get; set; } = string.Empty;
        }

        private sealed class HubitatUpdateResponse
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; } = string.Empty;

            [JsonProperty("errorMessage")]
            public string ErrorMessage { get; set; } = string.Empty;
        }

        private sealed class HubitatAppSaveRequest
        {
            [JsonProperty("id")]
            public int? Id { get; set; }

            [JsonProperty("version")]
            public int? Version { get; set; }

            [JsonProperty("source")]
            public string Source { get; set; } = string.Empty;
        }

        private sealed class HubitatAppSaveResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; } = string.Empty;

            [JsonProperty("id")]
            public int? Id { get; set; }

            [JsonProperty("version")]
            public int? Version { get; set; }
        }
    }
}
