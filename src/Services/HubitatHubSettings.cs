using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace HubitatVS
{
    public class HubitatHubSettings : BaseOptionModel<HubitatHubSettings>, IRatingConfig
    {
        private static readonly object CacheLock = new object();
        private static List<HubitatHubConfig>? CachedHubs;
        private static DateTimeOffset CachedAt;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

        [Browsable(false)]
        public string HubsJson { get; set; } = "[]";

        [Browsable(false)]
        public int RatingRequests { get; set; }

        public List<HubitatHubConfig> GetHubs()
        {
            try
            {
                return JsonConvert.DeserializeObject<List<HubitatHubConfig>>(HubsJson)
                    ?? new List<HubitatHubConfig>();
            }
            catch (Exception ex)
            {
                ex.Log();
                return new List<HubitatHubConfig>();
            }
        }

        public void SetHubs(List<HubitatHubConfig> hubs)
        {
            HubsJson = JsonConvert.SerializeObject(hubs);
            InvalidateCache();
        }

        public static async System.Threading.Tasks.Task<List<HubitatHubConfig>> GetHubsCachedAsync(bool forceRefresh = false)
        {
            var now = DateTimeOffset.UtcNow;
            lock (CacheLock)
            {
                if (!forceRefresh && CachedHubs != null && now - CachedAt <= CacheTtl)
                    return CloneHubs(CachedHubs);
            }

            var settings = await GetLiveInstanceAsync();
            var hubs = settings.GetHubs();

            lock (CacheLock)
            {
                CachedHubs = CloneHubs(hubs);
                CachedAt = DateTimeOffset.UtcNow;
                return CloneHubs(CachedHubs);
            }
        }

        public static void InvalidateCache()
        {
            lock (CacheLock)
            {
                CachedHubs = null;
                CachedAt = default;
            }
        }

        private static List<HubitatHubConfig> CloneHubs(IEnumerable<HubitatHubConfig> hubs)
            => hubs.Select(h => new HubitatHubConfig
            {
                Name = h.Name,
                Host = h.Host,
                Username = h.Username,
                Password = h.Password
            }).ToList();
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class HubitatHubSettingsDialogPage : BaseOptionPage<HubitatHubSettings> { }
}
