using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatCompareWorkflowTests
    {
        [TestMethod]
        public async Task ResolveAsync_ReturnsNotFound_WhenNoMatchExists()
        {
            var client = new FakeClient
            {
                NamespaceMatches = new List<HubitatCodeEntry>
                {
                    new HubitatCodeEntry { Id = 1, Name = "Other", Namespace = "mads" }
                }
            };

            var result = await HubitatCompareWorkflow.ResolveAsync(client, CreateCandidate());

            Assert.AreEqual(HubitatCompareFailure.NotFound, result.Failure);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public async Task ResolveAsync_ReturnsDownloadFailed_WhenMatchFoundButNoSource()
        {
            var client = new FakeClient
            {
                NamespaceMatches = new List<HubitatCodeEntry>
                {
                    new HubitatCodeEntry { Id = 2, Name = "Target", Namespace = "mads" }
                },
                DownloadSource = null
            };

            var result = await HubitatCompareWorkflow.ResolveAsync(client, CreateCandidate());

            Assert.AreEqual(HubitatCompareFailure.DownloadFailed, result.Failure);
            Assert.IsNotNull(result.Match);
            Assert.AreEqual(2, result.Match.Id);
            Assert.IsFalse(result.Success);
        }

        [TestMethod]
        public async Task ResolveAsync_ReturnsSuccess_WhenMatchAndSourceExist()
        {
            var client = new FakeClient
            {
                NamespaceMatches = new List<HubitatCodeEntry>
                {
                    new HubitatCodeEntry { Id = 9, Name = "TARGET", Namespace = "mads" }
                },
                DownloadSource = "def driver(){}"
            };

            var result = await HubitatCompareWorkflow.ResolveAsync(client, CreateCandidate());

            Assert.AreEqual(HubitatCompareFailure.None, result.Failure);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("def driver(){}", result.HubSource);
            Assert.AreEqual(9, result.Match!.Id);
        }

        [TestMethod]
        public async Task ResolveAsync_ThrowsForNullArguments()
        {
            var client = new FakeClient();
            var candidate = CreateCandidate();

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
                await HubitatCompareWorkflow.ResolveAsync(null!, candidate));

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
                await HubitatCompareWorkflow.ResolveAsync(client, null!));
        }

        [TestMethod]
        public async Task ResolveAsync_TreatsEmptyDownloadAsFailure()
        {
            var client = new FakeClient
            {
                NamespaceMatches = new List<HubitatCodeEntry>
                {
                    new HubitatCodeEntry { Id = 9, Name = "Target", Namespace = "mads" }
                },
                DownloadSource = string.Empty
            };

            var result = await HubitatCompareWorkflow.ResolveAsync(client, CreateCandidate());

            Assert.AreEqual(HubitatCompareFailure.DownloadFailed, result.Failure);
        }

        private static HubitatCodeCandidate CreateCandidate()
            => new HubitatCodeCandidate("C:\\repo\\target.groovy", "Target", "mads", "Author", "1.0.0", HubitatCodeKind.Driver, Array.Empty<string>());

        private sealed class FakeClient : IHubitatHubClient
        {
            public IReadOnlyList<HubitatCodeEntry> NamespaceMatches { get; set; } = Array.Empty<HubitatCodeEntry>();
            public string? DownloadSource { get; set; }

            public Task<IReadOnlyList<HubitatCodeEntry>> GetNamespaceMatchesAsync(HubitatCodeKind kind, string namespaceName, CancellationToken ct = default)
                => Task.FromResult(NamespaceMatches);

            public Task<string> DownloadCodeSourceAsync(HubitatCodeKind kind, int codeId, CancellationToken ct = default)
                => Task.FromResult(DownloadSource!);

            public Task<HubitatPublishResult> PublishAsync(HubitatCodeCandidate candidate, string source, CancellationToken ct = default)
                => throw new NotImplementedException();

            public Task<HubitatPublishResult> PublishToTargetAsync(HubitatCodeCandidate candidate, string source, int? targetId, CancellationToken ct = default)
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
