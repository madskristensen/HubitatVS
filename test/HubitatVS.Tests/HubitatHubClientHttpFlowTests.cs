using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatHubClientHttpFlowTests
    {
        [TestMethod]
        public async Task GetNamespaceMatchesAsync_FiltersByNamespace()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("[{\"id\":1,\"name\":\"A\",\"namespace\":\"mads\"},{\"id\":2,\"name\":\"B\",\"namespace\":\"other\"}]");
            using var client = CreateClient(handler);

            var matches = await client.GetNamespaceMatchesAsync(HubitatCodeKind.Driver, "MADS");

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual(1, matches[0].Id);
            Assert.AreEqual("A", matches[0].Name);
        }

        [TestMethod]
        public async Task PublishAsync_UpdatesExistingDriver()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("[{\"id\":5,\"name\":\"Test Driver\",\"namespace\":\"mads\"}]");
            handler.EnqueueJson("{\"id\":5,\"name\":\"Test Driver\",\"version\":2,\"source\":\"old\"}");
            handler.EnqueueJson("{\"status\":\"success\",\"version\":3}");
            using var client = CreateClient(handler);

            var result = await client.PublishAsync(CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads"), "new-source");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, result.CodeId);
            Assert.AreEqual(3, result.PublishedVersion);
            StringAssert.Contains(result.Message, "Updated driver ID 5");
        }

        [TestMethod]
        public async Task PublishAsync_CreatesNewDriver_WhenNoExistingMatch()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("[]");
            handler.EnqueueResponse(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Content = new StringContent("", Encoding.UTF8, "text/plain")
                };
                response.Headers.Location = new Uri("https://hub.local/driver/editor/42");
                return response;
            });
            handler.EnqueueJson("{\"id\":42,\"name\":\"Test Driver\",\"version\":1,\"source\":\"abc\"}");
            using var client = CreateClient(handler);

            var result = await client.PublishAsync(CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads"), "source");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(42, result.CodeId);
            Assert.AreEqual(1, result.PublishedVersion);
            StringAssert.Contains(result.Message, "Created new driver with ID 42.");
        }

        [TestMethod]
        public async Task PublishToTargetAsync_ForApp_UsesSaveOrUpdateJson()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("{\"id\":10,\"name\":\"Test App\",\"version\":7,\"source\":\"old\"}");
            handler.EnqueueJson("{\"success\":true,\"id\":10,\"version\":8,\"message\":\"Updated\"}");
            using var client = CreateClient(handler);

            var result = await client.PublishToTargetAsync(CreateCandidate(HubitatCodeKind.App, "Test App", "mads"), "new", 10);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(10, result.CodeId);
            Assert.AreEqual(8, result.PublishedVersion);
            Assert.AreEqual(HubitatCodeKind.App, result.CodeKind);
        }

        [TestMethod]
        public async Task PublishToTargetAsync_ForLargeDriverSource_UsesUrlEncodedRequestWithoutUriLengthFailure()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("{\"id\":10,\"name\":\"Test Driver\",\"version\":7,\"source\":\"old\"}");
            handler.EnqueueJson("{\"status\":\"success\",\"version\":8}");
            using var client = CreateClient(handler);
            var largeSource = new string('x', 70000);

            var result = await client.PublishToTargetAsync(CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads"), largeSource, 10);

            Assert.IsTrue(result.Success);
            var updateRequest = handler.Requests.FirstOrDefault(r =>
                string.Equals(r.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase)
                && r.RequestUri.EndsWith("/driver/ajax/update", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(updateRequest);
            Assert.AreEqual("application/x-www-form-urlencoded", updateRequest.ContentType);
            StringAssert.Contains(updateRequest.Content, "id=10");
            StringAssert.Contains(updateRequest.Content, "version=7");
            Assert.IsTrue(updateRequest.Content.Length > largeSource.Length);
        }

        [TestMethod]
        public async Task GetAdornmentInfoAsync_ReturnsFoundAndInSync()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("[{\"id\":9,\"name\":\"Test Driver\",\"namespace\":\"mads\",\"lastModified\":\"2026-05-20T10:00:00Z\",\"usedBy\":[{\"id\":1,\"name\":\"Device 1\"}]}]");
            handler.EnqueueJson("{\"id\":9,\"version\":4,\"source\":\"same\",\"installedDriverCount\":2}");
            using var client = CreateClient(handler);

            var info = await client.GetAdornmentInfoAsync(HubitatCodeKind.Driver, "Test Driver", "mads", "same");

            Assert.IsTrue(info.Found);
            Assert.IsTrue(info.IsInSync);
            Assert.AreEqual(4, info.Version);
            Assert.AreEqual(2, info.InstalledCount);
            Assert.AreEqual(1, info.UsedByNames.Count);
        }

        [TestMethod]
        public async Task GetNamespaceMatchesAsync_ReturnsEmpty_WhenEndpointFails()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("{}", HttpStatusCode.InternalServerError);
            using var client = CreateClient(handler);

            var matches = await client.GetNamespaceMatchesAsync(HubitatCodeKind.Driver, "mads");

            Assert.AreEqual(0, matches.Count);
        }

        [TestMethod]
        public async Task DownloadCodeSourceAsync_ReturnsNull_WhenEndpointFails()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("{\"error\":\"missing\"}", HttpStatusCode.NotFound);
            using var client = CreateClient(handler);

            var source = await client.DownloadCodeSourceAsync(HubitatCodeKind.Driver, 77);

            Assert.IsNull(source);
        }

        [TestMethod]
        public async Task TestConnectionAsync_ReturnsFalse_WhenHubIdMissing()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("{\"status\":\"ok\"}");
            using var client = CreateClient(handler);

            var connected = await client.TestConnectionAsync();

            Assert.IsFalse(connected);
        }

        [TestMethod]
        public async Task PublishAsync_ReturnsFailure_ForUnknownKindWithoutHttpCalls()
        {
            var handler = new QueueMessageHandler();
            using var client = CreateClient(handler);

            var candidate = CreateCandidate(HubitatCodeKind.Unknown, "Unknown", "mads");
            var result = await client.PublishAsync(candidate, "source");

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Could not determine whether this Groovy file is a Hubitat app, driver, or library.");
        }

        [TestMethod]
        public async Task GetNamespaceMatchesAsync_Throws_OnMalformedJson()
        {
            var handler = new QueueMessageHandler();
            handler.EnqueueJson("{not-json");
            using var client = CreateClient(handler);

            await Assert.ThrowsExceptionAsync<Newtonsoft.Json.JsonReaderException>(async () =>
                await client.GetNamespaceMatchesAsync(HubitatCodeKind.Driver, "mads"));
        }

        private static HubitatCodeCandidate CreateCandidate(HubitatCodeKind kind, string name, string namespaceName)
            => new HubitatCodeCandidate("C:\\repo\\file.groovy", name, namespaceName, "Author", "1.0.0", kind, Array.Empty<string>());

        private static HubitatHubClient CreateClient(QueueMessageHandler handler)
        {
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://hub.local")
            };

            var config = new HubitatHubConfig
            {
                Name = "Main",
                Host = "hub.local"
            };

            return new HubitatHubClient(config, httpClient);
        }

        private sealed class QueueMessageHandler : HttpMessageHandler
        {
            internal sealed class CapturedRequest
            {
                public string Method = string.Empty;
                public string RequestUri = string.Empty;
                public string ContentType = string.Empty;
                public string Content = string.Empty;
            }

            private readonly Queue<Func<HttpResponseMessage>> _responses = new Queue<Func<HttpResponseMessage>>();
            public List<CapturedRequest> Requests { get; } = new List<CapturedRequest>();

            public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                _responses.Enqueue(() => new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            public void EnqueueResponse(Func<HttpResponseMessage> responseFactory)
                => _responses.Enqueue(responseFactory);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var captured = new CapturedRequest
                {
                    Method = request.Method.Method,
                    RequestUri = request.RequestUri?.ToString() ?? string.Empty,
                    ContentType = request.Content?.Headers?.ContentType?.MediaType ?? string.Empty,
                    Content = request.Content != null ? await request.Content.ReadAsStringAsync() : string.Empty
                };
                Requests.Add(captured);

                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException($"No queued HTTP response for {request.Method} {request.RequestUri}");
                }

                return _responses.Dequeue().Invoke();
            }
        }
    }
}
