using System;
using System.Collections.Generic;

namespace HubitatVS
{
    internal sealed class HubitatHubInfoEntry
    {
        public string HubName { get; init; } = string.Empty;
        public bool Found { get; init; }
        public bool ConnectionError { get; init; }
        public int CodeId { get; init; }
        public int Version { get; init; }
        public int InstalledCount { get; init; }
        public HubitatCodeKind Kind { get; init; }
        public DateTimeOffset? LastModified { get; init; }
        public bool IsInSync { get; init; }
        public IReadOnlyList<string> UsedByNames { get; init; } = Array.Empty<string>();
    }
}
