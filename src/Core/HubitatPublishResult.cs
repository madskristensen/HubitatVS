namespace HubitatVS
{
    internal sealed class HubitatPublishResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public int? CodeId { get; set; }
        public int? PublishedVersion { get; set; }
        public HubitatCodeKind CodeKind { get; set; } = HubitatCodeKind.Unknown;
    }
}
