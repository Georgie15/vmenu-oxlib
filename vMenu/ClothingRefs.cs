using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace vMenuClient
{
    // Saved clothing by collection instead of by global number.
    //
    // A global drawable number is a position in one long list made of every clothing collection the
    // server streams (the base game, then each DLC or addon collection in load order). Adding,
    // removing or resizing any collection renumbers everything after it. A collection name plus the
    // drawable's index inside that collection (its "local" index) does not change when OTHER
    // collections change. This file is the game-call-free part: the saved format, the pre-change
    // layout baseline, the PSRP pack revisions and remaps, and the one resolution policy every
    // feature uses. ClothingService.cs supplies the game calls. Docs: docs/clothing-refs.md.

    /// <summary>A component (clothes) or prop slot.</summary>
    internal readonly struct ClothingSlot : IEquatable<ClothingSlot>
    {
        internal ClothingSlot(bool prop, int index)
        {
            Prop = prop;
            Index = index;
        }

        internal bool Prop { get; }
        internal int Index { get; }
        internal string Key => (Prop ? "p" : "c") + Index.ToString(CultureInfo.InvariantCulture);

        internal static ClothingSlot Component(int index) => new(false, index);
        internal static ClothingSlot PropSlot(int index) => new(true, index);

        public bool Equals(ClothingSlot other) => other.Prop == Prop && other.Index == Index;
        public override bool Equals(object obj) => obj is ClothingSlot other && Equals(other);
        public override int GetHashCode() => (Prop ? 1000 : 0) + Index;
        public override string ToString() => Key;

        private static readonly string[] ComponentNames = { "Head", "Mask", "Hair", "Upper Body", "Lower Body", "Bag & Parachute", "Shoes", "Scarf & Chain", "Shirt & Accessory", "Body Armor & Accessory 2", "Badges & Logos", "Shirt Overlay & Jacket" };
        private static readonly string[] PropNames = { "Hat & Helmet", "Glasses", "Misc Prop (ears)", "Prop 3", "Prop 4", "Prop 5", "Watch", "Bracelet" };

        internal string DisplayName => Prop
            ? (Index >= 0 && Index < PropNames.Length ? PropNames[Index] : "Prop " + Index)
            : (Index >= 0 && Index < ComponentNames.Length ? ComponentNames[Index] : "Component " + Index);

        /// <summary>Every key the layout prints cover: components 0-11, props 0-12.</summary>
        internal static readonly ClothingSlot[] AllLayoutSlots =
            Enumerable.Range(0, 12).Select(Component).Concat(Enumerable.Range(0, 13).Select(PropSlot)).ToArray();

        internal static bool TryParse(string key, out ClothingSlot slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(key) || key.Length < 2 || (key[0] != 'c' && key[0] != 'p'))
            {
                return false;
            }
            if (!int.TryParse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index > 20)
            {
                return false;
            }
            slot = new ClothingSlot(key[0] == 'p', index);
            return true;
        }
    }

    /// <summary>
    /// One saved piece. Normally a collection ("" is the base game), the drawable's index inside that
    /// collection and the texture. A prop slot can be saved as explicitly empty. A piece that has not
    /// been matched yet keeps the exact global numbers the old save had in <see cref="Legacy"/>.
    /// </summary>
    public sealed class ClothingRef
    {
        [JsonProperty("col", NullValueHandling = NullValueHandling.Ignore)]
        public string Collection { get; set; }

        [JsonProperty("drw", NullValueHandling = NullValueHandling.Ignore)]
        public int? Drawable { get; set; }

        [JsonProperty("tex", NullValueHandling = NullValueHandling.Ignore)]
        public int? Texture { get; set; }

        [JsonProperty("empty", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Empty { get; set; }

        // Only for PSRP pack collections: the pack revision and the pack print when this was saved,
        // so a later repack that moves the item can be followed with a remap.
        [JsonProperty("rev", NullValueHandling = NullValueHandling.Ignore)]
        public string Revision { get; set; }

        [JsonProperty("pp", NullValueHandling = NullValueHandling.Ignore)]
        public string PackPrint { get; set; }

        // The global layout print when this was saved. Lets a reference saved before the pack
        // revisions were configured be tied to the captured baseline's revision later.
        [JsonProperty("lp", NullValueHandling = NullValueHandling.Ignore)]
        public string LayoutPrint { get; set; }

        // [global drawable, texture] from a save made before collection references existed.
        [JsonProperty("legacy", NullValueHandling = NullValueHandling.Ignore)]
        public int[] Legacy { get; set; }

        [JsonProperty("why", NullValueHandling = NullValueHandling.Ignore)]
        public string Why { get; set; }

        [JsonIgnore]
        internal bool IsPending => Legacy != null;

        internal static ClothingRef EmptyProp() => new() { Empty = true };

        internal static ClothingRef Pending(int global, int texture, string why) => new() { Legacy = new[] { global, texture }, Why = why };

        internal ClothingRef Clone() => new()
        {
            Collection = Collection,
            Drawable = Drawable,
            Texture = Texture,
            Empty = Empty,
            Revision = Revision,
            PackPrint = PackPrint,
            LayoutPrint = LayoutPrint,
            Legacy = Legacy?.ToArray(),
            Why = Why,
        };

        internal bool SameItem(ClothingRef other) =>
            other != null && other.Empty == Empty && (Empty || (other.Collection ?? "") == (Collection ?? "") && other.Drawable == Drawable && other.Texture == Texture);

        internal string Describe() => Empty ? "nothing" : IsPending
            ? $"old #{Legacy[0]} texture {Legacy[1]}"
            : $"{(string.IsNullOrEmpty(Collection) ? "base game" : Collection)} #{Drawable} texture {Texture}";
    }

    /// <summary>The clothes and props of one save, by collection.</summary>
    public sealed class ClothingRefSet
    {
        internal const int FormatVersion = 2;

        [JsonProperty("v")]
        public int Version { get; set; } = FormatVersion;

        [JsonProperty("model")]
        public uint Model { get; set; }

        [JsonProperty("clothes")]
        public Dictionary<int, ClothingRef> Clothes { get; set; } = new();

        [JsonProperty("props")]
        public Dictionary<int, ClothingRef> Props { get; set; } = new();

        internal ClothingRef Get(ClothingSlot slot)
        {
            var map = slot.Prop ? Props : Clothes;
            return map != null && map.TryGetValue(slot.Index, out var item) ? item : null;
        }

        internal void Set(ClothingSlot slot, ClothingRef item)
        {
            Clothes ??= new Dictionary<int, ClothingRef>();
            Props ??= new Dictionary<int, ClothingRef>();
            var map = slot.Prop ? Props : Clothes;
            if (item == null)
            {
                map.Remove(slot.Index);
            }
            else
            {
                map[slot.Index] = item;
            }
        }

        internal ClothingRefSet Clone() => new()
        {
            Version = Version,
            Model = Model,
            Clothes = (Clothes ?? new Dictionary<int, ClothingRef>()).ToDictionary(p => p.Key, p => p.Value?.Clone()),
            Props = (Props ?? new Dictionary<int, ClothingRef>()).ToDictionary(p => p.Key, p => p.Value?.Clone()),
        };

        internal bool HasPending => (Clothes ?? new Dictionary<int, ClothingRef>()).Values.Concat((Props ?? new Dictionary<int, ClothingRef>()).Values).Any(r => r == null || r.IsPending);

        /// <summary>A stored set, or null when it is missing, damaged or from an unknown format.</summary>
        internal static ClothingRefSet Read(JToken token)
        {
            if (token == null || token.Type != JTokenType.Object)
            {
                return null;
            }
            try
            {
                // Lua's json turns an empty table into [], so a code that went through the server can
                // have empty lists where objects are expected.
                var copy = (JObject)token.DeepClone();
                foreach (var name in new[] { "clothes", "props" })
                {
                    if (copy[name] is JArray list && list.Count == 0)
                    {
                        copy[name] = new JObject();
                    }
                }
                var set = copy.ToObject<ClothingRefSet>();
                if (set == null || set.Version != FormatVersion || set.Model == 0)
                {
                    return null;
                }
                set.Clothes ??= new Dictionary<int, ClothingRef>();
                set.Props ??= new Dictionary<int, ClothingRef>();
                return set;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// What one save holds, in a neutral form: the collection references when there are any, and
    /// the global numbers (the only thing older saves have, and a cache in newer ones).
    /// </summary>
    internal sealed class ClothingSave
    {
        internal uint Model;
        internal ClothingRefSet Refs;

        // The layout print the global numbers were written under. Null means a save from before
        // this format existed: its numbers are read with the captured pre-change baseline.
        internal string GlobalsLayout;

        internal Dictionary<int, int[]> Components = new();
        internal Dictionary<int, int[]> Props = new();

        // Outfit codes only: when the code was created (unix seconds), 0 when unknown.
        internal long CreatedUnix;

        internal bool TryGetGlobal(ClothingSlot slot, out int drawable, out int texture)
        {
            drawable = texture = 0;
            var map = slot.Prop ? Props : Components;
            if (map == null || !map.TryGetValue(slot.Index, out var item) || item == null || item.Length != 2)
            {
                return false;
            }
            drawable = item[0];
            texture = item[1];
            return true;
        }
    }

    internal sealed class ClothingIssue
    {
        // Reasons a piece is not put on. The original entry is always kept.
        internal const string NotInBaseline = "not-in-baseline";
        internal const string NoBaseline = "no-baseline";
        internal const string BadTexture = "bad-texture";
        internal const string LayoutChanged = "layout-changed";
        internal const string Missing = "missing";
        internal const string Retired = "retired";
        internal const string NeedsDecision = "needs-decision";
        internal const string NoMap = "no-map";
        internal const string PackUnverified = "pack-unverified";
        internal const string Damaged = "damaged";
        internal const string WrongModel = "wrong-model";

        internal ClothingSlot Slot;
        internal string Reason;
        internal string Detail;

        public override string ToString() => $"{Slot.DisplayName} ({Slot.Key}): {Reason}{(string.IsNullOrEmpty(Detail) ? "" : " - " + Detail)}";

        internal string PlayerText => Reason switch
        {
            Retired => $"{Slot.DisplayName} was removed from the clothing pack",
            NeedsDecision => $"{Slot.DisplayName} was replaced and needs staff review",
            Missing => $"{Slot.DisplayName} isn't available on this server right now",
            _ => $"{Slot.DisplayName} couldn't be matched",
        };
    }

    internal sealed class ResolvedPiece
    {
        internal ClothingSlot Slot;
        internal bool Clear;          // prop slot left empty
        internal bool Raw;            // a global number put on as-is (no collection is known)
        internal string Collection;
        internal int Drawable;        // collection-local, or the global number when Raw
        internal int Texture;
        internal int Global;          // the current global number (for the menus and the globals cache)
    }

    internal sealed class ClothingResolution
    {
        internal uint Model;
        internal readonly List<ResolvedPiece> Pieces = new();
        internal readonly List<ClothingIssue> Issues = new();
        internal readonly List<string> Notes = new();

        // What to keep: every handled slot, with resolved pieces as current references and
        // unresolved ones exactly as they were.
        internal ClothingRefSet Refs;

        // Some global numbers were put on without a verified mapping (no baseline yet, or a model
        // this system doesn't track). Such a save is never rewritten automatically.
        internal bool Unverified;

        internal string RuntimeLayout;

        internal bool Clean => Issues.Count == 0 && !Unverified;
    }

    /// <summary>A run of consecutive global drawables that belong to one collection.</summary>
    internal sealed class LayoutSegment
    {
        internal string Collection;
        internal int Start;
        internal int Count;
        internal int[] Textures;   // texture count per drawable, when captured
    }

    /// <summary>The global numbering of one ped model, captured from a running client.</summary>
    internal sealed class ModelLayout
    {
        internal uint Model;
        internal string Name;
        internal string DeclaredPrint;
        internal readonly Dictionary<string, List<LayoutSegment>> Slots = new(StringComparer.Ordinal);

        internal bool TryFromGlobal(ClothingSlot slot, int global, out string collection, out int local, out int textures)
        {
            collection = null;
            local = -1;
            textures = -1;
            if (global < 0 || !Slots.TryGetValue(slot.Key, out var segments))
            {
                return false;
            }
            foreach (var segment in segments)
            {
                if (global >= segment.Start && global < segment.Start + segment.Count)
                {
                    collection = segment.Collection ?? "";
                    local = global - segment.Start;
                    textures = segment.Textures != null && local < segment.Textures.Length ? segment.Textures[local] : -1;
                    return true;
                }
            }
            return false;
        }

        internal int[] TextureCounts(string collection, ClothingSlot slot)
        {
            if (!Slots.TryGetValue(slot.Key, out var segments))
            {
                return Array.Empty<int>();
            }
            var segment = segments.FirstOrDefault(s => (s.Collection ?? "") == collection);
            return segment?.Textures ?? Array.Empty<int>();
        }

        internal string Print() => ClothingPrints.Layout(slot =>
            Slots.TryGetValue(slot.Key, out var segments)
                ? segments.OrderBy(s => s.Start).Select(s => new KeyValuePair<string, int>(s.Collection ?? "", s.Count))
                : Enumerable.Empty<KeyValuePair<string, int>>());
    }

    /// <summary>One known build of the PSRP clothing pack: texture counts per drawable of its collections.</summary>
    internal sealed class PackRevision
    {
        internal string Id;
        internal string Notes;
        internal readonly HashSet<string> Collections = new(StringComparer.Ordinal);

        // model -> "collection/slotKey" -> texture count per local drawable
        internal readonly Dictionary<uint, Dictionary<string, int[]>> Counts = new();

        internal static string CountKey(string collection, ClothingSlot slot) => collection + "/" + slot.Key;
    }

    internal sealed class RemapEntry
    {
        internal const string Move = "move";
        internal const string Retire = "retired";
        internal const string Review = "review";

        internal uint Model;
        internal ClothingSlot Slot;
        internal string Collection;
        internal int Drawable;
        internal int? Texture;          // null: the whole drawable
        internal string Action = Move;
        internal string ToCollection;
        internal int? ToDrawable;
        internal int? ToTexture;
        internal string Why;
    }

    /// <summary>The verified moves between two pack revisions, made from the EUP builder's maps.</summary>
    internal sealed class RemapStep
    {
        internal string From;
        internal string To;
        internal readonly List<RemapEntry> Entries = new();

        internal RemapEntry Find(uint model, ClothingSlot slot, string collection, int drawable, int texture)
        {
            RemapEntry whole = null;
            foreach (var e in Entries)
            {
                if (e.Model != model || !e.Slot.Equals(slot) || e.Collection != collection || e.Drawable != drawable)
                {
                    continue;
                }
                if (e.Texture == texture)
                {
                    return e;
                }
                if (e.Texture == null)
                {
                    whole = e;
                }
            }
            return whole;
        }
    }

    internal readonly struct PackStatus
    {
        internal PackStatus(string revision, string mode, string print)
        {
            Revision = revision;
            Mode = mode;
            Print = print;
        }

        internal string Revision { get; }   // null when the running pack is not a known revision
        internal string Mode { get; }       // exact, compatible (only additions), unknown, none (not configured)
        internal string Print { get; }
    }

    /// <summary>
    /// config/clothing-layouts.json: the captured pre-change baseline and the PSRP pack revisions and
    /// remaps. Made with tools/clothing-layouts; never edited by hand.
    /// </summary>
    internal sealed class ClothingLayouts
    {
        internal const uint MaleFreemode = 0x705E61F2;   // mp_m_freemode_01
        internal const uint FemaleFreemode = 0x9C9EFFD8; // mp_f_freemode_01

        internal static bool IsTrackedModel(uint model) => model == MaleFreemode || model == FemaleFreemode;

        // The baseline that saves made before this format are read with.
        internal string LegacyId;
        internal string LegacyRevision;
        internal string LegacyCapturedAt;
        internal long LegacyValidFromUnix;
        internal string LegacyValidFrom;
        internal readonly Dictionary<uint, ModelLayout> Legacy = new();

        internal string CurrentRevision;
        internal bool Strict = true;
        internal readonly List<PackRevision> Revisions = new();
        internal readonly List<RemapStep> Steps = new();
        internal readonly HashSet<string> PackCollections = new(StringComparer.Ordinal);


        internal static ClothingLayouts Empty() => new();

        internal bool IsPackCollection(string collection) => !string.IsNullOrEmpty(collection) && PackCollections.Contains(collection);

        internal ModelLayout LegacyFor(uint model) => Legacy.TryGetValue(model, out var layout) ? layout : null;

        internal PackRevision Revision(string id) => Revisions.FirstOrDefault(r => r.Id == id);

        /// <summary>The pack print over every PSRP collection, from any source of texture counts.</summary>
        internal string PackPrint(Func<string, ClothingSlot, int[]> counts) =>
            ClothingPrints.Pack(PackCollections, counts);

        /// <summary>Which known pack revision these texture counts are.</summary>
        internal PackStatus MatchPack(uint model, Func<string, ClothingSlot, int[]> counts)
        {
            if (Revisions.Count == 0)
            {
                return new PackStatus(null, "none", null);
            }
            var print = PackPrint(counts);
            var ordered = Revisions.Where(r => r.Id == CurrentRevision).Concat(Enumerable.Reverse(Revisions).Where(r => r.Id != CurrentRevision));
            foreach (var revision in ordered)
            {
                if (!revision.Counts.TryGetValue(model, out var expected) || expected.Count == 0)
                {
                    continue;
                }
                var exact = true;
                var compatible = true;
                foreach (var pair in expected)
                {
                    var split = pair.Key.LastIndexOf('/');
                    if (split < 0 || !ClothingSlot.TryParse(pair.Key.Substring(split + 1), out var slot))
                    {
                        exact = compatible = false;
                        break;
                    }
                    var actual = counts(pair.Key.Substring(0, split), slot) ?? Array.Empty<int>();
                    if (!actual.SequenceEqual(pair.Value))
                    {
                        exact = false;
                    }
                    if (actual.Length < pair.Value.Length || pair.Value.Where((count, i) => actual[i] < count).Any())
                    {
                        compatible = false;
                    }
                }
                if (exact)
                {
                    return new PackStatus(revision.Id, "exact", print);
                }
                // Only additions (more drawables or textures appended) keep existing references valid.
                if (compatible && revision.Id == CurrentRevision)
                {
                    return new PackStatus(revision.Id, "compatible", print);
                }
            }
            return new PackStatus(null, "unknown", print);
        }

        internal static ClothingLayouts Parse(string json, out string error)
        {
            error = null;
            var layouts = new ClothingLayouts();
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "no clothing layouts file";
                return layouts;
            }
            try
            {
                var root = JObject.Parse(json);
                if ((int?)root["version"] != 1)
                {
                    error = "unsupported clothing layouts version";
                    return Empty();
                }

                if (root["legacy"] is JObject legacy)
                {
                    layouts.LegacyId = (string)legacy["id"];
                    layouts.LegacyRevision = (string)legacy["rev"];
                    layouts.LegacyCapturedAt = (string)legacy["capturedAt"];
                    layouts.LegacyValidFrom = (string)legacy["validFrom"];
                    layouts.LegacyValidFromUnix = (long?)legacy["validFromUnix"] ?? 0;
                    if (legacy["models"] is JObject models)
                    {
                        foreach (var model in models.Properties())
                        {
                            if (!uint.TryParse(model.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var hash) || model.Value is not JObject body)
                            {
                                continue;
                            }
                            var layout = new ModelLayout { Model = hash, Name = (string)body["name"], DeclaredPrint = (string)body["layout"] };
                            if (body["slots"] is JObject slots)
                            {
                                foreach (var slot in slots.Properties())
                                {
                                    if (!ClothingSlot.TryParse(slot.Name, out _) || slot.Value is not JArray rows)
                                    {
                                        continue;
                                    }
                                    var segments = new List<LayoutSegment>();
                                    foreach (var row in rows.OfType<JArray>())
                                    {
                                        if (row.Count < 3)
                                        {
                                            continue;
                                        }
                                        segments.Add(new LayoutSegment
                                        {
                                            Collection = (string)row[0] ?? "",
                                            Start = (int)row[1],
                                            Count = (int)row[2],
                                            Textures = row.Count > 3 && row[3] is JArray textures ? textures.Select(t => (int)t).ToArray() : null,
                                        });
                                    }
                                    layout.Slots[slot.Name] = segments.OrderBy(s => s.Start).ToList();
                                }
                            }
                            // A baseline whose numbers don't add up to its own print is not used.
                            if (layout.DeclaredPrint != null && layout.Print() != layout.DeclaredPrint)
                            {
                                error = $"baseline for model {hash} does not match its print";
                                continue;
                            }
                            layouts.Legacy[hash] = layout;
                        }
                    }
                }

                if (root["pack"] is JObject pack)
                {
                    layouts.CurrentRevision = (string)pack["current"];
                    layouts.Strict = (bool?)pack["strict"] ?? true;
                    foreach (var rev in (pack["revisions"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var revision = new PackRevision { Id = (string)rev["id"], Notes = (string)rev["notes"] };
                        foreach (var name in (rev["collections"] as JArray ?? new JArray()).Select(c => (string)c).Where(c => !string.IsNullOrEmpty(c)))
                        {
                            revision.Collections.Add(name);
                            layouts.PackCollections.Add(name);
                        }
                        if (rev["counts"] is JObject counts)
                        {
                            foreach (var model in counts.Properties())
                            {
                                if (!uint.TryParse(model.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var hash) || model.Value is not JObject perModel)
                                {
                                    continue;
                                }
                                revision.Counts[hash] = perModel.Properties().ToDictionary(p => p.Name, p => (p.Value as JArray ?? new JArray()).Select(v => (int)v).ToArray(), StringComparer.Ordinal);
                            }
                        }
                        if (!string.IsNullOrEmpty(revision.Id))
                        {
                            layouts.Revisions.Add(revision);
                        }
                    }
                    foreach (var st in (pack["steps"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var step = new RemapStep { From = (string)st["from"], To = (string)st["to"] };
                        foreach (var e in (st["entries"] as JArray ?? new JArray()).OfType<JObject>())
                        {
                            if (!ClothingSlot.TryParse((string)e["slot"], out var slot) || !uint.TryParse((string)e["model"] ?? "", NumberStyles.None, CultureInfo.InvariantCulture, out var hash))
                            {
                                continue;
                            }
                            step.Entries.Add(new RemapEntry
                            {
                                Model = hash,
                                Slot = slot,
                                Collection = (string)e["col"],
                                Drawable = (int)e["drw"],
                                Texture = (int?)e["tex"],
                                Action = (string)e["action"] ?? RemapEntry.Move,
                                ToCollection = (string)e["toCol"],
                                ToDrawable = (int?)e["toDrw"],
                                ToTexture = (int?)e["toTex"],
                                Why = (string)e["why"],
                            });
                            layouts.PackCollections.Add((string)e["col"]);
                            if (!string.IsNullOrEmpty((string)e["toCol"]))
                            {
                                layouts.PackCollections.Add((string)e["toCol"]);
                            }
                        }
                        if (!string.IsNullOrEmpty(step.From) && !string.IsNullOrEmpty(step.To))
                        {
                            layouts.Steps.Add(step);
                        }
                    }
                }
                layouts.PackCollections.Remove(null);
                layouts.PackCollections.Remove("");
            }
            catch (Exception e) when (e is JsonException || e is InvalidCastException || e is FormatException || e is OverflowException || e is ArgumentException)
            {
                error = "clothing layouts file is damaged: " + e.Message;
                return Empty();
            }
            return layouts;
        }
    }

    /// <summary>Stable prints (FNV-1a 64) shared with tools/clothing-layouts/clothing_layouts.py.</summary>
    internal static class ClothingPrints
    {
        internal static string Fnv64(string text)
        {
            const ulong offset = 0xcbf29ce484222325;
            const ulong prime = 0x100000001b3;
            var hash = offset;
            foreach (var b in Encoding.UTF8.GetBytes(text ?? ""))
            {
                hash ^= b;
                hash *= prime;
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>The global layout: for every slot, its collections in order with their drawable counts.</summary>
        internal static string Layout(Func<ClothingSlot, IEnumerable<KeyValuePair<string, int>>> segments)
        {
            var parts = ClothingSlot.AllLayoutSlots.Select(slot =>
                slot.Key + "=" + string.Join(",", segments(slot).Where(s => s.Value > 0).Select(s => (s.Key ?? "") + ":" + s.Value.ToString(CultureInfo.InvariantCulture))));
            return Fnv64(string.Join("|", parts));
        }

        /// <summary>The pack print: texture counts per drawable of the named collections.</summary>
        internal static string Pack(IEnumerable<string> collections, Func<string, ClothingSlot, int[]> counts)
        {
            var parts = new List<string>();
            foreach (var collection in collections.Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c, StringComparer.Ordinal))
            {
                foreach (var slot in ClothingSlot.AllLayoutSlots)
                {
                    var values = counts(collection, slot) ?? Array.Empty<int>();
                    if (values.Length > 0)
                    {
                        parts.Add(collection + "/" + slot.Key + "=" + string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture))));
                    }
                }
            }
            return Fnv64(string.Join("|", parts));
        }
    }

    /// <summary>The game calls the resolver needs, for one ped.</summary>
    internal interface IClothingRuntime
    {
        uint Model { get; }

        /// <summary>Print of the current global layout for this model.</summary>
        string LayoutPrint { get; }

        /// <summary>The current global number of a collection drawable, or -1 when it doesn't exist.</summary>
        int ToGlobal(ClothingSlot slot, string collection, int local);

        /// <summary>How many textures the collection drawable has; 0 when it doesn't exist.</summary>
        int TextureCount(ClothingSlot slot, string collection, int local);

        /// <summary>The collection and local index of a current global number.</summary>
        bool FromGlobal(ClothingSlot slot, int global, out string collection, out int local);

        /// <summary>Texture count per drawable of one collection in one slot.</summary>
        int[] CollectionTextureCounts(string collection, ClothingSlot slot);

        /// <summary>Per-session memo for pack checks.</summary>
        IDictionary<string, object> Memo { get; }
    }

    /// <summary>
    /// The one resolution policy. Saved Outfits, MP characters (spawn, default, last appearance and
    /// the editor), Saved Peds and both outfit-code paths all go through here.
    /// </summary>
    internal static class ClothingResolver
    {
        internal static readonly ClothingSlot[] OutfitSlots =
            new[] { 1, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.Select(ClothingSlot.Component).Concat(Enumerable.Range(0, 8).Select(ClothingSlot.PropSlot)).ToArray();

        internal static readonly ClothingSlot[] SavedPedSlots =
            Enumerable.Range(0, 12).Select(ClothingSlot.Component).Concat(Enumerable.Range(0, 13).Select(ClothingSlot.PropSlot)).ToArray();

        internal static PackStatus Pack(ClothingLayouts layouts, IClothingRuntime runtime)
        {
            const string key = "pack-status";
            if (runtime.Memo.TryGetValue(key, out var cached) && cached is PackStatus status)
            {
                return status;
            }
            status = layouts.MatchPack(runtime.Model, runtime.CollectionTextureCounts);
            runtime.Memo[key] = status;
            return status;
        }

        /// <summary>Resolves the listed slots of a save against the running game.</summary>
        internal static ClothingResolution Resolve(ClothingSave save, IEnumerable<ClothingSlot> slots, ClothingLayouts layouts, IClothingRuntime runtime)
        {
            var result = new ClothingResolution
            {
                Model = save.Model,
                Refs = new ClothingRefSet { Model = save.Model },
                RuntimeLayout = runtime.LayoutPrint,
            };

            if (save.Model != runtime.Model)
            {
                foreach (var slot in slots)
                {
                    result.Issues.Add(new ClothingIssue { Slot = slot, Reason = ClothingIssue.WrongModel, Detail = $"saved for model {save.Model}, ped is {runtime.Model}" });
                }
                result.Refs = save.Refs?.Clone() ?? result.Refs;
                return result;
            }

            if (save.Refs == null && save.GlobalsLayout == null && save.CreatedUnix > 0 && layouts.LegacyValidFromUnix > 0 && save.CreatedUnix < layouts.LegacyValidFromUnix)
            {
                result.Notes.Add($"made before {layouts.LegacyValidFrom}; read with the {layouts.LegacyCapturedAt} clothing layout");
            }

            foreach (var slot in slots)
            {
                var entry = save.Refs?.Get(slot);
                if (entry != null && !entry.IsPending)
                {
                    ResolveRef(slot, entry, layouts, runtime, result);
                    continue;
                }

                int drawable, texture;
                string stamp;
                if (entry != null)
                {
                    if (entry.Legacy.Length != 2)
                    {
                        result.Issues.Add(new ClothingIssue { Slot = slot, Reason = ClothingIssue.Damaged });
                        result.Refs.Set(slot, entry.Clone());
                        continue;
                    }
                    drawable = entry.Legacy[0];
                    texture = entry.Legacy[1];
                    stamp = null; // pending pieces always come from a save made before this format
                }
                else if (save.TryGetGlobal(slot, out drawable, out texture))
                {
                    stamp = save.GlobalsLayout;
                }
                else
                {
                    continue; // nothing saved for this slot: leave it as it is
                }

                ResolveGlobal(slot, drawable, texture, stamp, save, layouts, runtime, result);
            }
            return result;
        }

        private static void ResolveGlobal(ClothingSlot slot, int drawable, int texture, string stamp, ClothingSave save, ClothingLayouts layouts, IClothingRuntime runtime, ClothingResolution result)
        {
            var original = ClothingRef.Pending(drawable, texture, null);

            if (slot.Prop && drawable < 0)
            {
                result.Pieces.Add(new ResolvedPiece { Slot = slot, Clear = true, Global = -1, Texture = -1 });
                result.Refs.Set(slot, ClothingRef.EmptyProp());
                return;
            }
            if (drawable < 0 || (!slot.Prop && texture < 0))
            {
                Fail(slot, ClothingIssue.Damaged, $"#{drawable} texture {texture}", original, result);
                return;
            }
            if (slot.Prop && texture < 0)
            {
                texture = 0; // as before: a worn prop with a bad texture uses texture 0
            }

            if (stamp != null)
            {
                if (stamp == runtime.LayoutPrint && runtime.FromGlobal(slot, drawable, out var col, out var local))
                {
                    ResolveRef(slot, StampRef(col, local, texture, layouts, runtime), layouts, runtime, result);
                    return;
                }
                var known = layouts.LegacyFor(save.Model);
                if (known == null || stamp != known.Print())
                {
                    Fail(slot, ClothingIssue.LayoutChanged, $"saved under layout {stamp}", original, result);
                    return;
                }
                // Written under the baseline layout: fall through to the baseline.
            }

            var baseline = layouts.LegacyFor(save.Model);
            if (baseline != null)
            {
                if (!baseline.TryFromGlobal(slot, drawable, out var col, out var local, out var textures))
                {
                    Fail(slot, ClothingIssue.NotInBaseline, $"#{drawable} is past the end of the captured layout", original, result);
                    return;
                }
                if (textures >= 0 && texture >= textures)
                {
                    Fail(slot, ClothingIssue.BadTexture, $"texture {texture} of {textures} in the captured layout", original, result);
                    return;
                }
                ResolveRef(slot, BaselineRef(col, local, texture, baseline, layouts), layouts, runtime, result, original);
                return;
            }

            // No captured baseline for this model. Models the clothing packs don't touch, and the
            // time before a baseline is captured (no legacy pack declared), behave as before. Once
            // the pack old saves were made with is declared, they are only put on while that pack
            // still runs. Either way the save is never rewritten from them.
            if (!ClothingLayouts.IsTrackedModel(save.Model) || layouts.LegacyRevision == null
                || Pack(layouts, runtime).Revision == layouts.LegacyRevision)
            {
                PutOnRaw(slot, drawable, texture, original, result);
                return;
            }
            Fail(slot, ClothingIssue.NoBaseline, "the clothing pack changed and no pre-change layout was captured", original, result);
        }

        private static void PutOnRaw(ClothingSlot slot, int drawable, int texture, ClothingRef original, ClothingResolution result)
        {
            result.Unverified = true;
            result.Pieces.Add(new ResolvedPiece { Slot = slot, Raw = true, Drawable = drawable, Texture = texture, Global = drawable });
            result.Refs.Set(slot, original);
        }

        private static void Fail(ClothingSlot slot, string reason, string detail, ClothingRef keep, ClothingResolution result)
        {
            result.Issues.Add(new ClothingIssue { Slot = slot, Reason = reason, Detail = detail });
            var kept = keep.Clone();
            if (kept.IsPending)
            {
                kept.Why = reason;
            }
            result.Refs.Set(slot, kept);
        }

        private static ClothingRef StampRef(string collection, int local, int texture, ClothingLayouts layouts, IClothingRuntime runtime)
        {
            var item = new ClothingRef { Collection = collection ?? "", Drawable = local, Texture = texture, LayoutPrint = runtime.LayoutPrint };
            if (layouts.IsPackCollection(item.Collection))
            {
                var pack = Pack(layouts, runtime);
                item.Revision = pack.Revision;
                item.PackPrint = pack.Print;
            }
            return item;
        }

        private static ClothingRef BaselineRef(string collection, int local, int texture, ModelLayout baseline, ClothingLayouts layouts)
        {
            var item = new ClothingRef { Collection = collection ?? "", Drawable = local, Texture = texture, LayoutPrint = baseline.Print() };
            if (layouts.IsPackCollection(item.Collection))
            {
                item.Revision = layouts.LegacyRevision;
                item.PackPrint = layouts.PackPrint(baseline.TextureCounts);
            }
            return item;
        }

        /// <summary>A reference to what a ped wears now, or null when the game can't place it in a collection.</summary>
        internal static ClothingRef Capture(ClothingSlot slot, int global, int texture, ClothingLayouts layouts, IClothingRuntime runtime)
        {
            if (slot.Prop && global < 0)
            {
                return ClothingRef.EmptyProp();
            }
            if (global < 0 || !runtime.FromGlobal(slot, global, out var collection, out var local))
            {
                return null;
            }
            return StampRef(collection, local, Math.Max(0, texture), layouts, runtime);
        }

        private static void ResolveRef(ClothingSlot slot, ClothingRef entry, ClothingLayouts layouts, IClothingRuntime runtime, ClothingResolution result, ClothingRef keepOnFailure = null)
        {
            var keep = keepOnFailure ?? entry;
            if (entry.Empty)
            {
                if (!slot.Prop)
                {
                    Fail(slot, ClothingIssue.Damaged, "a clothing slot can't be empty", keep, result);
                    return;
                }
                result.Pieces.Add(new ResolvedPiece { Slot = slot, Clear = true, Global = -1, Texture = -1 });
                result.Refs.Set(slot, ClothingRef.EmptyProp());
                return;
            }
            if (entry.Drawable == null || entry.Texture == null || entry.Drawable < 0 || entry.Texture < 0 || entry.Collection == null)
            {
                Fail(slot, ClothingIssue.Damaged, null, keep, result);
                return;
            }

            var collection = entry.Collection;
            var drawable = entry.Drawable.Value;
            var texture = entry.Texture.Value;
            var isPack = layouts.IsPackCollection(collection);
            PackStatus pack = default;

            if (isPack)
            {
                pack = Pack(layouts, runtime);
                var samePack = entry.PackPrint != null && entry.PackPrint == pack.Print;
                // Saved before pack revisions were configured: the baseline's layout means the
                // baseline's pack revision.
                var revision = entry.Revision
                    ?? (entry.LayoutPrint != null && layouts.LegacyFor(runtime.Model)?.Print() == entry.LayoutPrint ? layouts.LegacyRevision : null);
                if (!samePack)
                {
                    if (revision != null && pack.Revision != null)
                    {
                        if (revision != pack.Revision && !FollowSteps(slot, runtime.Model, revision, pack.Revision, layouts, ref collection, ref drawable, ref texture, keep, result))
                        {
                            return;
                        }
                    }
                    else if (layouts.Strict)
                    {
                        var detail = pack.Revision == null
                            ? "the running clothing pack is not a known revision"
                            : "this piece was saved with an unknown clothing pack";
                        Fail(slot, ClothingIssue.PackUnverified, detail, keep, result);
                        return;
                    }
                }
            }

            var global = runtime.ToGlobal(slot, collection, drawable);
            if (global < 0)
            {
                Fail(slot, ClothingIssue.Missing, $"{(collection == "" ? "base game" : collection)} #{drawable}", keep, result);
                return;
            }
            var textures = runtime.TextureCount(slot, collection, drawable);
            if (texture >= textures)
            {
                Fail(slot, ClothingIssue.BadTexture, $"texture {texture} of {textures}", keep, result);
                return;
            }

            result.Pieces.Add(new ResolvedPiece { Slot = slot, Collection = collection, Drawable = drawable, Texture = texture, Global = global });
            var resolved = new ClothingRef { Collection = collection, Drawable = drawable, Texture = texture, LayoutPrint = runtime.LayoutPrint };
            if (isPack)
            {
                resolved.Revision = pack.Revision;
                resolved.PackPrint = pack.Print;
            }
            result.Refs.Set(slot, resolved);
        }

        private static bool FollowSteps(ClothingSlot slot, uint model, string from, string to, ClothingLayouts layouts, ref string collection, ref int drawable, ref int texture, ClothingRef keep, ClothingResolution result)
        {
            var revision = from;
            for (var guard = 0; revision != to; guard++)
            {
                var step = guard < 64 ? layouts.Steps.FirstOrDefault(s => s.From == revision) : null;
                if (step == null)
                {
                    Fail(slot, ClothingIssue.NoMap, $"no verified map from pack {from} to {to}", keep, result);
                    return false;
                }
                var entry = step.Find(model, slot, collection, drawable, texture);
                if (entry != null)
                {
                    if (entry.Action == RemapEntry.Retire)
                    {
                        Fail(slot, ClothingIssue.Retired, entry.Why ?? $"retired in {step.To}", keep, result);
                        return false;
                    }
                    if (entry.Action != RemapEntry.Move)
                    {
                        Fail(slot, ClothingIssue.NeedsDecision, entry.Why ?? $"replaced in {step.To}", keep, result);
                        return false;
                    }
                    collection = entry.ToCollection ?? collection;
                    drawable = entry.ToDrawable ?? drawable;
                    texture = entry.ToTexture ?? texture;
                }
                revision = step.To;
            }
            return true;
        }

        /// <summary>
        /// Turns a save made before this format into collection references with the captured baseline
        /// alone (no game calls). Returns null, with the reasons, unless every listed piece converts:
        /// a save is only ever rewritten whole.
        /// </summary>
        internal static ClothingRefSet ConvertLegacy(ClothingSave save, IEnumerable<ClothingSlot> slots, ClothingLayouts layouts, List<ClothingIssue> issues)
        {
            var baseline = layouts.LegacyFor(save.Model);
            var set = new ClothingRefSet { Model = save.Model };
            var start = issues.Count;
            foreach (var slot in slots)
            {
                if (!save.TryGetGlobal(slot, out var drawable, out var texture))
                {
                    continue;
                }
                if (slot.Prop && drawable < 0)
                {
                    set.Set(slot, ClothingRef.EmptyProp());
                    continue;
                }
                if (drawable < 0 || (!slot.Prop && texture < 0))
                {
                    issues.Add(new ClothingIssue { Slot = slot, Reason = ClothingIssue.Damaged, Detail = $"#{drawable} texture {texture}" });
                    continue;
                }
                if (slot.Prop && texture < 0)
                {
                    texture = 0;
                }
                if (baseline == null)
                {
                    issues.Add(new ClothingIssue { Slot = slot, Reason = ClothingIssue.NoBaseline });
                    continue;
                }
                if (!baseline.TryFromGlobal(slot, drawable, out var collection, out var local, out var textures))
                {
                    issues.Add(new ClothingIssue { Slot = slot, Reason = ClothingIssue.NotInBaseline, Detail = $"#{drawable}" });
                    continue;
                }
                if (textures >= 0 && texture >= textures)
                {
                    issues.Add(new ClothingIssue { Slot = slot, Reason = ClothingIssue.BadTexture, Detail = $"texture {texture} of {textures}" });
                    continue;
                }
                set.Set(slot, BaselineRef(collection, local, texture, baseline, layouts));
            }
            return issues.Count == start ? set : null;
        }
    }
}
