using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HubitatVS
{
    internal readonly struct HubitatPublishTargetSelection
    {
        public HubitatPublishTargetSelection(bool cancelled, int? targetId)
        {
            Cancelled = cancelled;
            TargetId = targetId;
        }

        public bool Cancelled { get; }

        public int? TargetId { get; }
    }

    internal sealed class HubitatPublishWorkflowResult
    {
        public HubitatPublishWorkflowResult(bool cancelled, HubitatPublishResult? result)
        {
            Cancelled = cancelled;
            Result = result;
        }

        public bool Cancelled { get; }

        public HubitatPublishResult? Result { get; }
    }

    internal static class HubitatPublishWorkflow
    {
        internal delegate Task<HubitatPublishTargetSelection> SelectPublishTargetAsync(
            string displayName,
            string namespaceName,
            IReadOnlyList<HubitatCodeEntry> namespaceMatches);

        internal static async Task<HubitatPublishWorkflowResult> PublishAsync(
            HubitatCodeCandidate candidate,
            string source,
            IHubitatHubClient client,
            SelectPublishTargetAsync selectTargetAsync,
            CancellationToken ct)
        {
            if (candidate is null)
                throw new ArgumentNullException(nameof(candidate));
            if (client is null)
                throw new ArgumentNullException(nameof(client));
            if (selectTargetAsync is null)
                throw new ArgumentNullException(nameof(selectTargetAsync));

            int? targetId = null;
            if (!string.IsNullOrWhiteSpace(candidate.NamespaceName))
            {
                var namespaceMatches = await client.GetNamespaceMatchesAsync(candidate.Kind, candidate.NamespaceName, ct);
                var exactMatch = namespaceMatches.FirstOrDefault(entry =>
                    string.Equals(entry.Name, candidate.DisplayName, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    targetId = exactMatch.Id;
                }
                else if (namespaceMatches.Count > 0)
                {
                    var selection = await selectTargetAsync(candidate.DisplayName, candidate.NamespaceName, namespaceMatches);
                    if (selection.Cancelled)
                    {
                        return new HubitatPublishWorkflowResult(cancelled: true, result: null);
                    }

                    targetId = selection.TargetId;
                }
            }

            var result = targetId.HasValue || !string.IsNullOrWhiteSpace(candidate.NamespaceName)
                ? await client.PublishToTargetAsync(candidate, source, targetId, ct)
                : await client.PublishAsync(candidate, source, ct);

            return new HubitatPublishWorkflowResult(cancelled: false, result: result);
        }
    }
}
