namespace HubitatVS
{
    /// <summary>Represents an app or driver on the Hubitat hub.</summary>
    public sealed class HubitatCodeEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrWhiteSpace(Namespace)
            ? Name
            : $"{Name} ({Namespace})";
    }
}
