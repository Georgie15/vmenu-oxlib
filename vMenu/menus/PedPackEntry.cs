using Newtonsoft.Json;

namespace vMenuClient.menus
{
    public class PedPackEntry
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }
}
