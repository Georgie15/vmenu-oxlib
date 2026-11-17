using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

namespace vMenuClient
{
    /// <summary>
    /// A saved MP character's own outfits and hair and makeup styles (Saved Characters > a character >
    /// Outfits / Hair &amp; Makeup Styles). Each entry is its own KVP and never part of the character's
    /// JSON, so an older vMenu (or a rollback) reads characters exactly as before and leaves these alone:
    /// <list type="bullet">
    /// <item><c>vmenu_character_outfit_&lt;character&gt;|&lt;outfit&gt;</c>: a <see cref="SavedOutfit"/>, the Saved Outfits format.</item>
    /// <item><c>vmenu_character_style_&lt;character&gt;|&lt;style&gt;</c>: a <see cref="HairMakeupStyle"/>.</item>
    /// </list>
    /// &lt;character&gt; is the character's save name without its <c>mp_ped_</c> prefix. Entry names can't
    /// contain '|', so the part after a character's prefix is only its entry when it has no '|': a
    /// character called "A|B" never sees the entries of "A", or the other way round. Both prefixes
    /// start with "vmenu" (MainMenu's one-off KVP cleanup keeps them) and are in the save backups.
    /// No game calls, so it can be tested.
    /// </summary>
    internal static class CharacterWardrobe
    {
        internal const string OutfitPrefix = "vmenu_character_outfit_";
        internal const string StylePrefix = "vmenu_character_style_";
        internal const char Separator = '|';
        internal const int MaxNameLength = 30;
        internal const int MaxEntries = 50;

        internal enum Kind { Outfit, Style }

        internal static readonly Kind[] Kinds = { Kind.Outfit, Kind.Style };

        internal static string Prefix(Kind kind) => kind == Kind.Outfit ? OutfitPrefix : StylePrefix;

        /// <summary>The character's name from its save key (<c>mp_ped_&lt;name&gt;</c>) or the bare name.</summary>
        internal static string CharacterName(string characterKey) =>
            string.IsNullOrEmpty(characterKey) ? "" : characterKey.StartsWith("mp_ped_", StringComparison.Ordinal) ? characterKey.Substring("mp_ped_".Length) : characterKey;

        /// <summary>What every entry key of one character starts with (pass it to StartFindKvp).</summary>
        internal static string KeyPrefix(Kind kind, string characterKey) => Prefix(kind) + CharacterName(characterKey) + Separator;

        internal static string KeyFor(Kind kind, string characterKey, string name) => KeyPrefix(kind, characterKey) + name;

        /// <summary>The entry name of a key, or null when the key isn't an entry of this character.</summary>
        internal static string NameOf(Kind kind, string characterKey, string key)
        {
            var prefix = KeyPrefix(kind, characterKey);
            if (string.IsNullOrEmpty(key) || !key.StartsWith(prefix, StringComparison.Ordinal) || key.Length == prefix.Length)
            {
                return null;
            }
            var name = key.Substring(prefix.Length);
            return name.IndexOf(Separator) >= 0 ? null : name;
        }

        /// <summary>The entry names of one character among keys found by its prefix, sorted.</summary>
        internal static List<string> Names(Kind kind, string characterKey, IEnumerable<string> keys) =>
            (keys ?? Enumerable.Empty<string>()).Select(k => NameOf(kind, characterKey, k)).Where(n => n != null)
                .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>A name typed by the player, trimmed. Null, with a reason, when it can't be used.</summary>
        internal static string CleanName(string input, out string error)
        {
            error = null;
            var name = (input ?? "").Trim();
            if (name.Length == 0)
            {
                error = "Enter a name.";
                return null;
            }
            if (name.Length > MaxNameLength)
            {
                error = $"Names can be up to {MaxNameLength} characters.";
                return null;
            }
            if (name.IndexOf(Separator) >= 0 || name.IndexOf('~') >= 0 || name.Any(char.IsControl))
            {
                error = "Names can't contain | or ~.";
                return null;
            }
            return name;
        }

        /// <summary>
        /// Gives a character's outfits and styles to another name: a rename moves them
        /// (<paramref name="keepSource"/> false), a clone copies them. Each copy is read back before
        /// its source is removed, and an entry the other name already has is never overwritten (the
        /// source then stays where it is). Returns how many entries were moved or copied.
        /// </summary>
        internal static int Transfer(string fromCharacter, string toCharacter, bool keepSource, Func<string, IEnumerable<string>> find,
            Func<string, string> get, Action<string, string> set, Action<string> delete)
        {
            if (CharacterName(fromCharacter) == CharacterName(toCharacter))
            {
                return 0;
            }
            var done = 0;
            foreach (var kind in Kinds)
            {
                foreach (var name in Names(kind, fromCharacter, find(KeyPrefix(kind, fromCharacter))))
                {
                    var from = KeyFor(kind, fromCharacter, name);
                    var to = KeyFor(kind, toCharacter, name);
                    var value = get(from);
                    if (string.IsNullOrEmpty(value) || !string.IsNullOrEmpty(get(to)))
                    {
                        continue;
                    }
                    set(to, value);
                    if (get(to) != value)
                    {
                        continue;
                    }
                    if (!keepSource)
                    {
                        delete(from);
                    }
                    done++;
                }
            }
            return done;
        }

        /// <summary>Removes every outfit and style of a character (when the character is deleted).</summary>
        internal static int DeleteAll(string characterKey, Func<string, IEnumerable<string>> find, Action<string> delete)
        {
            var removed = 0;
            foreach (var kind in Kinds)
            {
                foreach (var name in Names(kind, characterKey, find(KeyPrefix(kind, characterKey))))
                {
                    delete(KeyFor(kind, characterKey, name));
                    removed++;
                }
            }
            return removed;
        }
    }

    /// <summary>One face layer of a style: the game's head overlay value, its opacity and colour.</summary>
    internal sealed class StyleLayer
    {
        [JsonProperty("style")]
        public int Style { get; set; }

        [JsonProperty("opacity")]
        public float Opacity { get; set; }

        [JsonProperty("color")]
        public int Color { get; set; }
    }

    /// <summary>
    /// A hair and makeup style: the haircut (a global hair drawable, as MP characters save it), its
    /// colour and highlight, the hair's fade overlay, and the beard, eyebrows, makeup, blush and
    /// lipstick layers (style, opacity, colour). Indices are the game's for the freemode model it was
    /// saved on. No game calls.
    /// </summary>
    internal sealed class HairMakeupStyle
    {
        internal const int FormatVersion = 1;
        internal const int NoOverlay = 255;
        internal const int MaxColor = 63;

        /// <summary>Head overlays a style keeps: beard, eyebrows, makeup, blush, lipstick.</summary>
        internal static readonly int[] OverlayIds = { 1, 2, 4, 5, 8 };

        /// <summary>The colour type SET_PED_HEAD_OVERLAY_COLOR takes, as the character creator uses it.</summary>
        internal static int ColorType(int overlay) => overlay is 1 or 2 ? 1 : 2;

        internal static string OverlayName(int overlay) => overlay switch
        {
            1 => "beard",
            2 => "eyebrows",
            4 => "makeup",
            5 => "blush",
            8 => "lipstick",
            _ => "layer " + overlay,
        };

        [JsonProperty("v")]
        public int Version { get; set; } = FormatVersion;

        [JsonProperty("model")]
        public uint Model { get; set; }

        [JsonProperty("hair")]
        public int Hair { get; set; }

        [JsonProperty("hairColor")]
        public int HairColor { get; set; }

        [JsonProperty("hairHighlight")]
        public int HairHighlight { get; set; }

        // [collection, overlay] of the hair's fade (SET_PED_FACIAL_DECORATION); none when null.
        [JsonProperty("hairOverlay", NullValueHandling = NullValueHandling.Ignore)]
        public string[] HairOverlay { get; set; }

        [JsonProperty("overlays")]
        public Dictionary<int, StyleLayer> Overlays { get; set; } = new();

        /// <summary>
        /// What a ped has now. <paramref name="layer"/> reads one head overlay; null when it can't be
        /// read (the layer is then left out, and applying the style leaves it as it is).
        /// </summary>
        internal static HairMakeupStyle Capture(uint model, int hair, int hairColor, int hairHighlight, string[] hairOverlay, Func<int, StyleLayer> layer)
        {
            var style = new HairMakeupStyle
            {
                Model = model,
                Hair = Math.Max(0, hair),
                HairColor = Clamp(hairColor, 0, MaxColor),
                HairHighlight = Clamp(hairHighlight, 0, MaxColor),
                HairOverlay = ValidOverlay(hairOverlay) ? new[] { hairOverlay[0], hairOverlay[1] } : null,
            };
            foreach (var id in OverlayIds)
            {
                var read = layer(id);
                if (read != null)
                {
                    style.Overlays[id] = Normalize(read);
                }
            }
            return style;
        }

        /// <summary>
        /// A layer as the character creator keeps it: no overlay (255) is style 0 at opacity 0, opacity
        /// is 0 to 1, colours are in the game's range.
        /// </summary>
        internal static StyleLayer Normalize(StyleLayer layer)
        {
            var none = layer.Style < 0 || layer.Style >= NoOverlay;
            var opacity = float.IsNaN(layer.Opacity) ? 0f : Math.Max(0f, Math.Min(1f, layer.Opacity));
            return new StyleLayer
            {
                Style = none ? 0 : layer.Style,
                Opacity = none ? 0f : (float)Math.Round(opacity, 2),
                Color = Clamp(layer.Color, 0, MaxColor),
            };
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static bool ValidOverlay(string[] overlay) =>
            overlay != null && overlay.Length == 2 && overlay.All(s => !string.IsNullOrEmpty(s) && s.Length <= 64);

        internal string ToJson() => JsonConvert.SerializeObject(this);

        /// <summary>A stored style, normalized. Null when there is none or it can't be read.</summary>
        internal static HairMakeupStyle Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                var style = JsonConvert.DeserializeObject<HairMakeupStyle>(json);
                if (style == null || style.Model == 0 || style.Version != FormatVersion || style.Hair < 0)
                {
                    return null;
                }
                style.HairColor = Clamp(style.HairColor, 0, MaxColor);
                style.HairHighlight = Clamp(style.HairHighlight, 0, MaxColor);
                style.HairOverlay = ValidOverlay(style.HairOverlay) ? style.HairOverlay : null;
                style.Overlays = (style.Overlays ?? new Dictionary<int, StyleLayer>())
                    .Where(p => OverlayIds.Contains(p.Key) && p.Value != null)
                    .ToDictionary(p => p.Key, p => Normalize(p.Value));
                return style;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
