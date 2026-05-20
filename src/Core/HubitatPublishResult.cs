namespace HubitatVS
{
    internal sealed class HubitatPublishResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? DriverId { get; set; }
    }
}
