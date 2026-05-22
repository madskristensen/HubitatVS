using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HubitatVS
{
    internal enum HubitatCompareFailure
    {
        None,
        NotFound,
        DownloadFailed
    }

    internal sealed class HubitatCompareWorkflowResult
    {
        public HubitatCompareWorkflowResult(HubitatCompareFailure failure, HubitatCodeEntry? match = null, string? hubSource = null)
        {
            Failure = failure;
            Match = match;
            HubSource = hubSource;
        }

        public HubitatCompareFailure Failure { get; }

        public HubitatCodeEntry? Match { get; }

        public string? HubSource { get; }

        public bool Success => Failure == HubitatCompareFailure.None && Match != null && !string.IsNullOrEmpty(HubSource);
    }

    internal static class HubitatCompareWorkflow
    {
        internal static async Task<HubitatCompareWorkflowResult> ResolveAsync(
            IHubitatHubClient client,
            HubitatCodeCandidate candidate,
            CancellationToken ct = default)
        {
            if (client is null)
                throw new ArgumentNullException(nameof(client));
            if (candidate is null)
                throw new ArgumentNullException(nameof(candidate));

            var matches = await client.GetNamespaceMatchesAsync(candidate.Kind, candidate.NamespaceName, ct);
            var match = matches.FirstOrDefault(m =>
                string.Equals(m.Name, candidate.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return new HubitatCompareWorkflowResult(HubitatCompareFailure.NotFound);
            }

            var hubSource = await client.DownloadCodeSourceAsync(candidate.Kind, match.Id, ct);
            if (string.IsNullOrEmpty(hubSource))
            {
                return new HubitatCompareWorkflowResult(HubitatCompareFailure.DownloadFailed, match);
            }

            return new HubitatCompareWorkflowResult(HubitatCompareFailure.None, match, hubSource);
        }
    }
}
