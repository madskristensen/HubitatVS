using Newtonsoft.Json;
using System.ComponentModel;

namespace HubitatVS
{
    public sealed class HubitatHubConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        [Browsable(false)]
        [JsonIgnore]
        public string Password { get; set; } = string.Empty;
    }
}
