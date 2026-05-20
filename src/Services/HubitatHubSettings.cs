using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace HubitatVS
{
    public class HubitatHubSettings : BaseOptionModel<HubitatHubSettings>, IRatingConfig
    {
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
        }

    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class HubitatHubSettingsDialogPage : BaseOptionPage<HubitatHubSettings> { }
}
