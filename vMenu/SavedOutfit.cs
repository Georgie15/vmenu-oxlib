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
    ///
    /// Since collection references: <see cref="Refs"/> is what the outfit is (collection, local
    /// drawable, texture per slot) and is what gets worn; <see cref="Clothes"/> and <see cref="Props"/>
    /// still hold the global numbers, stamped with the <see cref="Layout"/> they were written under,
    /// so an older vMenu can still read the outfit. Outfits saved before have no refs and are read
    /// with the captured pre-change baseline (ClothingResolver).
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

        [JsonProperty("refs", NullValueHandling = NullValueHandling.Ignore)]
        public ClothingRefSet Refs { get; set; }

        [JsonProperty("layout", NullValueHandling = NullValueHandling.Ignore)]
        public string Layout { get; set; }

        internal static string KeyFor(string name) => KeyPrefix + name;

        /// <summary>
        /// What the ped wears now. <paramref name="reference"/> places a worn global number in its
        /// collection (null when it can't); <paramref name="layout"/> is the running layout's print.
        /// </summary>
        internal static SavedOutfit Capture(uint model, Func<int, int> drawable, Func<int, int> texture, Func<int, int> prop, Func<int, int> propTexture,
            Func<ClothingSlot, int, int, ClothingRef> reference = null, string layout = null)
        {
            var outfit = new SavedOutfit { Model = model, Layout = layout };
            if (reference != null)
            {
                outfit.Refs = new ClothingRefSet { Model = model };
            }
            foreach (var slot in ComponentSlots)
            {
                var d = drawable(slot);
                var t = texture(slot);
                outfit.Clothes[slot] = new[] { d, t };
                outfit.Refs?.Set(ClothingSlot.Component(slot), reference(ClothingSlot.Component(slot), d, t));
            }
            foreach (var slot in PropSlots)
            {
                var worn = prop(slot);
                var t = worn < 0 ? -1 : propTexture(slot);
                outfit.Props[slot] = new[] { worn, t };
                outfit.Refs?.Set(ClothingSlot.PropSlot(slot), reference(ClothingSlot.PropSlot(slot), worn, t));
            }
            return outfit;
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
