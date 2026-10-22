using System;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace vMenuClient
{
    /// <summary>
    /// MP Ped Customization > Saved Outfits: the clothes and props an MP character wears, saved under
    /// a name as <c>vmenu_outfit_&lt;name&gt;</c> and changed into later. Face (component 0), hair (2),
    /// tattoos and everything else about the character stay as they are. An outfit belongs to the
    /// freemode model it was saved on (male or female). Kept free of game calls so it can be tested.
    /// </summary>
    internal sealed class SavedOutfit
    {
        // Starts with "vmenu" so MainMenu's one-off KVP cleanup keeps it; save backups include it.
        internal const string KeyPrefix = "vmenu_outfit_";

        // The same slots ReplacePedDataClothing treats as clothing.
        internal static readonly int[] ComponentSlots = { 1, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        internal static readonly int[] PropSlots = { 0, 1, 2, 3, 4, 5, 6, 7 };

        [JsonProperty("model")]
        public uint Model { get; set; }

        // Slot -> [drawable, texture]. A prop drawable of -1 means nothing is worn there.
        [JsonProperty("clothes")]
        public Dictionary<int, int[]> Clothes { get; set; } = new Dictionary<int, int[]>();

        [JsonProperty("props")]
        public Dictionary<int, int[]> Props { get; set; } = new Dictionary<int, int[]>();

        internal static string KeyFor(string name) => KeyPrefix + name;

        internal static SavedOutfit Capture(uint model, Func<int, int> drawable, Func<int, int> texture, Func<int, int> prop, Func<int, int> propTexture)
        {
            var outfit = new SavedOutfit { Model = model };
            foreach (var slot in ComponentSlots)
            {
                outfit.Clothes[slot] = new[] { drawable(slot), texture(slot) };
            }
            foreach (var slot in PropSlots)
            {
                var worn = prop(slot);
                outfit.Props[slot] = new[] { worn, worn < 0 ? -1 : propTexture(slot) };
            }
            return outfit;
        }

        /// <summary>
        /// Dresses the ped. Only clothing slots are touched, and damaged entries are skipped.
        /// </summary>
        internal void Apply(Action<int, int, int> setComponent, Action<int> clearProp, Action<int, int, int> setProp)
        {
            foreach (var slot in ComponentSlots)
            {
                if (Clothes.TryGetValue(slot, out var item) && item != null && item.Length == 2 && item[0] >= 0 && item[1] >= 0)
                {
                    setComponent(slot, item[0], item[1]);
                }
            }
            foreach (var slot in PropSlots)
            {
                if (!Props.TryGetValue(slot, out var item) || item == null || item.Length != 2)
                {
                    continue;
                }
                if (item[0] < 0)
                {
                    clearProp(slot);
                }
                else
                {
                    setProp(slot, item[0], Math.Max(0, item[1]));
                }
            }
        }

        internal string ToJson() => JsonConvert.SerializeObject(this);

        /// <summary>
        /// Reads a stored outfit. Null when there is none or it can't be read.
        /// </summary>
        internal static SavedOutfit Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                var outfit = JsonConvert.DeserializeObject<SavedOutfit>(json);
                if (outfit == null || outfit.Model == 0)
                {
                    return null;
                }
                outfit.Clothes ??= new Dictionary<int, int[]>();
                outfit.Props ??= new Dictionary<int, int[]>();
                return outfit;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
