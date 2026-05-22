using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatPublishWorkflowTests
    {
        [TestMethod]
        public async Task PublishAsync_WithoutNamespace_UsesPublishAsyncPath()
        {
            var client = new FakeClient();
            var candidate = CreateCandidate(HubitatCodeKind.Driver, "Test Driver", namespaceName: "");

            var result = await HubitatPublishWorkflow.PublishAsync(
                candidate,
                "source",
                client,
                (_, _, _) => Task.FromResult(new HubitatPublishTargetSelection(false, null)),
                CancellationToken.None);

            Assert.IsFalse(result.Cancelled);
            Assert.IsNotNull(result.Result);
            Assert.AreEqual(1, client.PublishCalls);
            Assert.AreEqual(0, client.PublishToTargetCalls);
        }

        [TestMethod]
        public async Task PublishAsync_WithExactNamespaceMatch_UsesMatchedId()
        {
            var client = new FakeClient
            {
                NamespaceMatches = new List<HubitatCodeEntry>
                {
                    new HubitatCodeEntry { Id = 12, Name = "Test Driver", Namespace = "mads" }
                }
            };
            var candidate = CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads");

            var result = await HubitatPublishWorkflow.PublishAsync(
                candidate,
                "source",
                client,
                (_, _, _) => Task.FromResult(new HubitatPublishTargetSelection(false, null)),
                CancellationToken.None);

            Assert.IsFalse(result.Cancelled);
            Assert.AreEqual(1, client.PublishToTargetCalls);
            Assert.AreEqual(12, client.LastTargetId);
        }

        [TestMethod]
        public async Task PublishAsync_WithNamespaceMatchesAndCancel_ReturnsCancelled()
        {
            var client = new FakeClient
            {
                NamespaceMatches = new List<HubitatCodeEntry>
                {
                    new HubitatCodeEntry { Id = 33, Name = "Other", Namespace = "mads" }
                }
            };
            var candidate = CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads");

            var result = await HubitatPublishWorkflow.PublishAsync(
                candidate,
                "source",
                client,
                (_, _, _) => Task.FromResult(new HubitatPublishTargetSelection(true, null)),
                CancellationToken.None);

            Assert.IsTrue(result.Cancelled);
            Assert.IsNull(result.Result);
            Assert.AreEqual(0, client.PublishCalls);
            Assert.AreEqual(0, client.PublishToTargetCalls);
        }

        [TestMethod]
        public async Task PublishAsync_WithNamespaceMatchesAndCreateNew_UsesPublishToTargetWithNullId()
        {
            var client = new FakeClient
            {
                NamespaceMatches = new List<HubitatCodeEntry>
                {
                    new HubitatCodeEntry { Id = 33, Name = "Other", Namespace = "mads" }
                }
            };
            var candidate = CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads");

            var result = await HubitatPublishWorkflow.PublishAsync(
                candidate,
                "source",
                client,
                (_, _, _) => Task.FromResult(new HubitatPublishTargetSelection(false, null)),
                CancellationToken.None);

            Assert.IsFalse(result.Cancelled);
            Assert.AreEqual(1, client.PublishToTargetCalls);
            Assert.IsNull(client.LastTargetId);
        }

        [TestMethod]
        public async Task PublishAsync_ThrowsForNullArguments()
        {
            var client = new FakeClient();
            var candidate = CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads");

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
                await HubitatPublishWorkflow.PublishAsync(null!, "source", client, (_, _, _) => Task.FromResult(new HubitatPublishTargetSelection(false, null)), CancellationToken.None));

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
                await HubitatPublishWorkflow.PublishAsync(candidate, "source", null!, (_, _, _) => Task.FromResult(new HubitatPublishTargetSelection(false, null)), CancellationToken.None));

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
                await HubitatPublishWorkflow.PublishAsync(candidate, "source", client, null!, CancellationToken.None));
        }

        [TestMethod]
        public async Task PublishAsync_WithNamespaceAndNoMatches_UsesPublishToTargetWithoutPrompt()
        {
            var client = new FakeClient
            {
                NamespaceMatches = Array.Empty<HubitatCodeEntry>()
            };
            var candidate = CreateCandidate(HubitatCodeKind.Driver, "Test Driver", "mads");
            var promptCalls = 0;

            var result = await HubitatPublishWorkflow.PublishAsync(
                candidate,
                "source",
                client,
                (_, _, _) =>
                {
                    promptCalls++;
                    return Task.FromResult(new HubitatPublishTargetSelection(false, null));
                },
                CancellationToken.None);

            Assert.IsFalse(result.Cancelled);
            Assert.AreEqual(0, promptCalls);
            Assert.AreEqual(1, client.PublishToTargetCalls);
            Assert.IsNull(client.LastTargetId);
        }

        private static HubitatCodeCandidate CreateCandidate(HubitatCodeKind kind, string name, string namespaceName)
            => new HubitatCodeCandidate("C:\\repo\\file.groovy", name, namespaceName, "Author", "1.0.0", kind, Array.Empty<string>());

        private sealed class FakeClient : IHubitatHubClient
        {
            public IReadOnlyList<HubitatCodeEntry> NamespaceMatches { get; set; } = Array.Empty<HubitatCodeEntry>();
            public int PublishCalls { get; private set; }
            public int PublishToTargetCalls { get; private set; }
            public int? LastTargetId { get; private set; }

            public Task<IReadOnlyList<HubitatCodeEntry>> GetNamespaceMatchesAsync(HubitatCodeKind kind, string namespaceName, CancellationToken ct = default)
                => Task.FromResult(NamespaceMatches);

            public Task<HubitatPublishResult> PublishAsync(HubitatCodeCandidate candidate, string source, CancellationToken ct = default)
            {
                PublishCalls++;
                return Task.FromResult(new HubitatPublishResult { Success = true, Message = "publish" });
            }

            public Task<HubitatPublishResult> PublishToTargetAsync(HubitatCodeCandidate candidate, string source, int? targetId, CancellationToken ct = default)
            {
                PublishToTargetCalls++;
                LastTargetId = targetId;
                return Task.FromResult(new HubitatPublishResult { Success = true, Message = "target" });
            }

            public Task<string> DownloadCodeSourceAsync(HubitatCodeKind kind, int codeId, CancellationToken ct = default)
                => throw new NotImplementedException();

            public Task<HubitatHubInfoEntry> GetAdornmentInfoAsync(HubitatCodeKind kind, string name, string namespaceName, string localSource, CancellationToken ct = default)
                => throw new NotImplementedException();

            public Task<bool> TestConnectionAsync(CancellationToken ct = default)
                => throw new NotImplementedException();

            public void Dispose()
            {
            }
        }
    }
}
