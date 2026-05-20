using Community.VisualStudio.Toolkit;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;

namespace HubitatVS
{
    public class HubitatHubSettings : BaseOptionModel<HubitatHubSettings>
    {
        [Browsable(false)]
        public string HubsJson { get; set; } = "[]";

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
