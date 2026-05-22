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

        [Browsable(false)]
        public bool PublishOnSaveEnabled { get; set; }

        public List<HubitatHubConfig> GetHubs()
        {
            try
            {
                var hubs = JsonConvert.DeserializeObject<List<HubitatHubConfig>>(HubsJson)
                    ?? new List<HubitatHubConfig>();

                foreach (var hub in hubs)
                    hub.Password = HubitatCredentialStore.ReadPassword(hub);

                return hubs;
            }
            catch (Exception ex)
            {
                ex.Log();
                return new List<HubitatHubConfig>();
            }
        }

        public void SetHubs(List<HubitatHubConfig> hubs)
        {
            var previousHubs = GetStoredHubsFromJson();

            foreach (var previousHub in previousHubs)
            {
                if (!hubs.Any(current => IsSameHub(previousHub, current)))
                    HubitatCredentialStore.DeletePassword(previousHub);
            }

            foreach (var hub in hubs)
            {
                if (string.IsNullOrWhiteSpace(hub.Password))
                    HubitatCredentialStore.DeletePassword(hub);
                else
                    HubitatCredentialStore.SavePassword(hub, hub.Password);
            }

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

        private List<HubitatHubConfig> GetStoredHubsFromJson()
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

        private static bool IsSameHub(HubitatHubConfig left, HubitatHubConfig right)
            => string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class HubitatHubSettingsDialogPage : BaseOptionPage<HubitatHubSettings> { }
}
