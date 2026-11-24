using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace vMenuClient
{
    /// <summary>
    /// Reads and upgrades every kind of vMenu save that holds clothing, on the stored JSON itself so
    /// nothing else in a save is touched: Saved Outfits (vmenu_outfit_*), MP characters (mp_ped_*)
    /// and the last-appearance snapshot, Saved Peds (ped_*) and outfit codes. No game calls.
    /// </summary>
    internal static class ClothingSaves
    {
        internal const string BackupPrefix = "vmenu_clothing_backup_";
        internal const string ReviewKey = "vmenu_clothing_review";
        internal const string LastSnapshotKey = "vmenu_last_mp_character_snapshot";

        // MP character JSON property names (MultiplayerPedData fields).
        internal const string CharacterRefsField = "ClothingRefs";
        internal const string CharacterLayoutField = "ClothingLayout";

        // Saved Ped JSON property names (PedInfo fields).
        internal const string PedRefsField = "clothingRefs";
        internal const string PedLayoutField = "clothingLayout";

        internal enum Kind { Outfit, Character, SavedPed }

        internal static Kind? KindOf(string key)
        {
            if (string.IsNullOrEmpty(key) || key.StartsWith(BackupPrefix, StringComparison.Ordinal))
            {
                return null;
            }
            if (key.StartsWith(SavedOutfit.KeyPrefix, StringComparison.Ordinal))
            {
                return Kind.Outfit;
            }
            if (key.StartsWith("mp_ped_", StringComparison.Ordinal) || key == LastSnapshotKey)
            {
                return Kind.Character;
            }
            if (key.StartsWith("ped_", StringComparison.Ordinal))
            {
                return Kind.SavedPed;
            }
            return null;
        }

        internal static IEnumerable<ClothingSlot> SlotsFor(Kind kind, ClothingSave save) => kind switch
        {
            Kind.SavedPed => ClothingResolver.SavedPedSlots.Where(s => !s.Prop || s.Index < 13),
            Kind.Character => CharacterSlots(save),
            _ => ClothingResolver.OutfitSlots,
        };

        /// <summary>MP characters: every saved clothing slot except head (0) and hair (2), and every saved prop.</summary>
        internal static IEnumerable<ClothingSlot> CharacterSlots(ClothingSave save)
        {
            var components = save.Components.Keys.Concat(save.Refs?.Clothes?.Keys ?? Enumerable.Empty<int>())
                .Where(k => k is not 0 and not 2 && k >= 0 && k < 12).Distinct().OrderBy(k => k).Select(ClothingSlot.Component);
            var props = save.Props.Keys.Concat(save.Refs?.Props?.Keys ?? Enumerable.Empty<int>())
                .Where(k => k >= 0 && k < 13).Distinct().OrderBy(k => k).Select(ClothingSlot.PropSlot);
            return components.Concat(props).ToList();
        }

        private static JObject Object(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static int? Int(JToken token)
        {
            if (token == null)
            {
                return null;
            }
            if (token.Type == JTokenType.Integer)
            {
                var value = token.Value<long>();
                return value is >= int.MinValue and <= int.MaxValue ? (int)value : null;
            }
            if (token.Type == JTokenType.Float)
            {
                var value = token.Value<double>();
                return Math.Abs(value - Math.Round(value)) < 1e-9 && value is >= int.MinValue and <= int.MaxValue ? (int)Math.Round(value) : null;
            }
            return null;
        }

        private static uint Model(JToken token)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                return 0;
            }
            var value = token.Value<double>();
            if (value >= 0 && value <= uint.MaxValue)
            {
                return (uint)value;
            }
            if (value < 0 && value >= int.MinValue)
            {
                return unchecked((uint)(int)value); // a signed hash
            }
            return 0;
        }

        private static bool SlotKey(string key, out int slot) =>
            int.TryParse(key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out slot) && slot >= 0 && slot <= 20;

        /// <summary>Reads any clothing save. Null when it can't be read.</summary>
        internal static ClothingSave Read(Kind kind, string json) => Read(kind, Object(json));

        internal static ClothingSave Read(Kind kind, JObject root)
        {
            if (root == null)
            {
                return null;
            }
            var save = new ClothingSave();
            switch (kind)
            {
                case Kind.Outfit:
                    save.Model = Model(root["model"]);
                    ReadPairs(root["clothes"], save.Components);
                    ReadPairs(root["props"], save.Props);
                    save.Refs = ClothingRefSet.Read(root["refs"]);
                    save.GlobalsLayout = (string)root["layout"];
                    break;
                case Kind.Character:
                    save.Model = Model(root["ModelHash"]);
                    ReadKeyValues(root["DrawableVariations"]?["clothes"], save.Components);
                    ReadKeyValues(root["PropVariations"]?["props"], save.Props);
                    save.Refs = ClothingRefSet.Read(root[CharacterRefsField]);
                    save.GlobalsLayout = (string)root[CharacterLayoutField];
                    break;
                case Kind.SavedPed:
                    save.Model = Model(root["model"]);
                    ReadSplit(root["drawableVariations"], root["drawableVariationTextures"], save.Components, false);
                    ReadSplit(root["props"], root["propTextures"], save.Props, true);
                    save.Refs = ClothingRefSet.Read(root[PedRefsField]);
                    save.GlobalsLayout = (string)root[PedLayoutField];
                    break;
            }
            if (save.Model == 0)
            {
                return null;
            }
            if (save.Refs != null && save.Refs.Model != save.Model)
            {
                save.Refs = null; // references for another body are never applied
            }
            return save;
        }

        private static void ReadPairs(JToken token, Dictionary<int, int[]> into)
        {
            if (token is not JObject map)
            {
                return;
            }
            foreach (var p in map.Properties())
            {
                if (SlotKey(p.Name, out var slot) && p.Value is JArray pair && pair.Count == 2 && Int(pair[0]) is int d && Int(pair[1]) is int t)
                {
                    into[slot] = new[] { d, t };
                }
            }
        }

        private static void ReadKeyValues(JToken token, Dictionary<int, int[]> into)
        {
            if (token is not JObject map)
            {
                return;
            }
            foreach (var p in map.Properties())
            {
                if (SlotKey(p.Name, out var slot) && p.Value is JObject kv && Int(kv["Key"]) is int d && Int(kv["Value"]) is int t)
                {
                    into[slot] = new[] { d, t };
                }
            }
        }

        private static void ReadSplit(JToken drawables, JToken textures, Dictionary<int, int[]> into, bool props)
        {
            if (drawables is not JObject d)
            {
                return;
            }
            var t = textures as JObject;
            foreach (var p in d.Properties())
            {
                if (SlotKey(p.Name, out var slot) && Int(p.Value) is int drawable)
                {
                    var texture = Int(t?[p.Name]) ?? (props ? -1 : 0);
                    // As Saved Peds always did: a prop without a texture is taken off.
                    into[slot] = props && texture < 0 ? new[] { -1, -1 } : new[] { drawable, texture };
                }
            }
        }

        /// <summary>Writes references (and the layout the global numbers were written under) into a save's JSON.</summary>
        internal static void WriteRefs(Kind kind, JObject root, ClothingRefSet refs, string globalsLayout)
        {
            var refsField = kind == Kind.Character ? CharacterRefsField : kind == Kind.SavedPed ? PedRefsField : "refs";
            var layoutField = kind == Kind.Character ? CharacterLayoutField : kind == Kind.SavedPed ? PedLayoutField : "layout";
            root[refsField] = refs == null ? null : JObject.FromObject(refs);
            if (globalsLayout == null)
            {
                root.Remove(layoutField);
            }
            else
            {
                root[layoutField] = globalsLayout;
            }
        }

        internal sealed class MigrationResult
        {
            internal string Key;
            internal string NewJson;          // null when the save was left as it is
            internal bool AlreadyCurrent;
            internal bool Skipped;            // not a clothing save this system upgrades
            internal readonly List<ClothingIssue> Issues = new();
        }

        /// <summary>
        /// Upgrades one save made before collection references, using the captured baseline only.
        /// The save is rewritten only when every piece converts; otherwise the issues say which
        /// pieces need review and the save stays exactly as it was.
        /// </summary>
        internal static MigrationResult Migrate(string key, string json, ClothingLayouts layouts)
        {
            var result = new MigrationResult { Key = key };
            var kind = KindOf(key);
            var root = Object(json);
            var save = kind == null ? null : Read(kind.Value, root);
            if (kind == null || save == null)
            {
                result.Skipped = true;
                return result;
            }
            if (save.Refs != null)
            {
                result.AlreadyCurrent = true;
                return result;
            }
            if (save.GlobalsLayout != null || !ClothingLayouts.IsTrackedModel(save.Model) || layouts.LegacyFor(save.Model) == null)
            {
                // Numbers stamped with a layout (written by this version), a model the clothing
                // packs don't touch, or no captured baseline yet: nothing to convert now.
                result.Skipped = true;
                return result;
            }
            var refs = ClothingResolver.ConvertLegacy(save, SlotsFor(kind.Value, save), layouts, result.Issues);
            if (refs == null)
            {
                return result;
            }
            WriteRefs(kind.Value, root, refs, layouts.LegacyFor(save.Model)?.Print());
            result.NewJson = root.ToString(Formatting.None);
            return result;
        }

        /// <summary>
        /// Upgrades every listed save in storage. Each original is copied to
        /// <see cref="BackupPrefix"/> + key first, once: an existing backup (the first original) is
        /// never overwritten, and a save whose backup can't be written is left alone. Saves already
        /// upgraded are skipped, so this is safe to run on every join.
        /// </summary>
        internal static List<MigrationResult> MigrateAll(IEnumerable<string> keys, Func<string, string> get, Action<string, string> set, ClothingLayouts layouts, Action<string> log, out int upgraded)
        {
            var results = new List<MigrationResult>();
            upgraded = 0;
            foreach (var key in keys)
            {
                var json = get(key);
                if (string.IsNullOrEmpty(json))
                {
                    continue;
                }
                var result = Migrate(key, json, layouts);
                results.Add(result);
                if (result.NewJson == null)
                {
                    continue;
                }
                var backupKey = BackupPrefix + key;
                if (string.IsNullOrEmpty(get(backupKey)))
                {
                    set(backupKey, json);
                    if (get(backupKey) != json)
                    {
                        log($"couldn't back up {key}; left unchanged");
                        continue;
                    }
                }
                set(key, result.NewJson);
                if (get(key) == result.NewJson)
                {
                    upgraded++;
                }
                else
                {
                    log($"couldn't upgrade {key}; the original is in {backupKey}");
                }
            }
            return results;
        }

        /// <summary>
        /// References for every slot of a save: existing references, then the baseline, then the old
        /// numbers kept as pending pieces (resolved, or reported, each time they are worn).
        /// </summary>
        internal static ClothingRefSet CompleteRefs(ClothingSave save, IEnumerable<ClothingSlot> slots, ClothingLayouts layouts)
        {
            var set = save.Refs?.Clone() ?? new ClothingRefSet { Model = save.Model };
            set.Model = save.Model;
            foreach (var slot in slots)
            {
                if (set.Get(slot) != null || !save.TryGetGlobal(slot, out var drawable, out var texture))
                {
                    continue;
                }
                var single = new ClothingSave { Model = save.Model };
                (slot.Prop ? single.Props : single.Components)[slot.Index] = new[] { drawable, texture };
                var converted = save.GlobalsLayout == null
                    ? ClothingResolver.ConvertLegacy(single, new[] { slot }, layouts, new List<ClothingIssue>())
                    : null;
                set.Set(slot, converted?.Get(slot) ?? ClothingRef.Pending(drawable, texture, converted == null ? "unmatched" : null));
            }
            return set;
        }

        // ---- Outfit codes ---------------------------------------------------------------------

        /// <summary>
        /// The outfit-code payload of an outfit (a Saved Outfits entry, or what the ped wears now
        /// captured the same way): the old Clothes/Props numbers, kept for anything that still reads
        /// them, plus the model and collection references. Null when a piece has no reference yet;
        /// <paramref name="unshareable"/> lists those pieces. A code never shares a guess.
        /// </summary>
        internal static JObject BuildCode(ClothingSave outfit, ClothingLayouts layouts, out List<ClothingSlot> unshareable)
        {
            unshareable = new List<ClothingSlot>();
            if (outfit == null || outfit.Model == 0)
            {
                return null;
            }
            var slots = ClothingResolver.OutfitSlots.Where(s => outfit.TryGetGlobal(s, out _, out _) || outfit.Refs?.Get(s) != null).ToList();
            var all = CompleteRefs(outfit, slots, layouts);
            var refs = new ClothingRefSet { Model = outfit.Model };
            foreach (var slot in slots)
            {
                var item = all.Get(slot);
                if (item == null || item.IsPending)
                {
                    unshareable.Add(slot);
                }
                refs.Set(slot, item);
            }
            if (unshareable.Count > 0)
            {
                return null;
            }
            var code = new JObject
            {
                ["v"] = ClothingRefSet.FormatVersion,
                ["Model"] = outfit.Model,
                ["Clothes"] = new JObject(),
                ["Props"] = new JObject(),
            };
            foreach (var slot in slots)
            {
                if (outfit.TryGetGlobal(slot, out var drawable, out var texture))
                {
                    ((JObject)code[slot.Prop ? "Props" : "Clothes"])[slot.Index.ToString(CultureInfo.InvariantCulture)] = new JObject { ["Item"] = drawable, ["Texture"] = texture };
                }
            }
            if (outfit.GlobalsLayout != null)
            {
                code["Layout"] = outfit.GlobalsLayout;
            }
            else if (layouts.LegacyFor(outfit.Model) is ModelLayout baseline)
            {
                code["Layout"] = baseline.Print();
            }
            code["Refs"] = JObject.FromObject(refs);
            return code;
        }

        /// <summary>
        /// Reads an outfit code. A version 2 code carries its model and references. An older code
        /// has only global numbers and no model; it is read for the model it is used on, with the
        /// captured baseline.
        /// </summary>
        internal static ClothingSave ReadCode(string codeJson, uint targetModel, long createdUnix, out string error)
        {
            error = null;
            var root = Object(codeJson);
            // Lua's json writes an empty table as [].
            JObject Section(JToken token) => token as JObject ?? (token is JArray list && list.Count == 0 ? new JObject() : null);
            var clothes = Section(root?["Clothes"]);
            var props = Section(root?["Props"]);
            if (root == null || clothes == null || props == null)
            {
                error = "This outfit code is damaged and can't be loaded.";
                return null;
            }
            var save = new ClothingSave { CreatedUnix = createdUnix };
            foreach (var pair in new[] { new KeyValuePair<JObject, Dictionary<int, int[]>>(clothes, save.Components), new KeyValuePair<JObject, Dictionary<int, int[]>>(props, save.Props) })
            {
                var into = pair.Value;
                foreach (var p in pair.Key.Properties())
                {
                    if (SlotKey(p.Name, out var slot) && p.Value is JObject item && Int(item["Item"]) is int d && Int(item["Texture"]) is int t)
                    {
                        into[slot] = new[] { d, t };
                    }
                }
            }
            save.Components.Remove(0);
            save.Components.Remove(2);

            if (Int(root["v"]) == ClothingRefSet.FormatVersion)
            {
                save.Model = Model(root["Model"]);
                save.Refs = ClothingRefSet.Read(root["Refs"]);
                save.GlobalsLayout = (string)root["Layout"];
                if (save.Model == 0 || save.Refs == null || save.Refs.Model != save.Model)
                {
                    error = "This outfit code is damaged and can't be loaded.";
                    return null;
                }
                if (save.Model != targetModel)
                {
                    error = save.Model == ClothingLayouts.MaleFreemode
                        ? "This outfit code is for a male character."
                        : save.Model == ClothingLayouts.FemaleFreemode ? "This outfit code is for a female character." : "This outfit code is for a different ped model.";
                    return null;
                }
                save.Refs.Clothes.Remove(0);
                save.Refs.Clothes.Remove(2);
            }
            else
            {
                save.Model = targetModel;
            }
            return save;
        }

        /// <summary>
        /// A Saved Outfits entry from an outfit code read with <see cref="ReadCode"/> (Saved Outfits >
        /// Load Outfit Code). Every clothing slot and prop the code has gets a reference: the code's
        /// own, or, for a code made before references, its numbers converted with the captured
        /// baseline, else kept exactly as they are (<paramref name="pending"/>) and checked each time
        /// the outfit is worn. Props the code doesn't mention are taken off, as wearing a code
        /// always did. Face and hair are never part of it.
        /// </summary>
        internal static SavedOutfit OutfitFromCode(ClothingSave code, ClothingLayouts layouts, out List<ClothingIssue> pending)
        {
            pending = new List<ClothingIssue>();
            var outfit = new SavedOutfit
            {
                Model = code.Model,
                // A code made before references has numbers from an unknown layout: don't claim one.
                Layout = code.Refs != null ? code.GlobalsLayout : null,
                Refs = new ClothingRefSet { Model = code.Model },
            };
            var refs = CompleteRefs(code, ClothingResolver.OutfitSlots, layouts);
            foreach (var slot in ClothingResolver.OutfitSlots)
            {
                var map = slot.Prop ? outfit.Props : outfit.Clothes;
                var item = refs.Get(slot);
                if (code.TryGetGlobal(slot, out var drawable, out var texture))
                {
                    map[slot.Index] = new[] { drawable, texture };
                }
                else if (slot.Prop && item == null)
                {
                    map[slot.Index] = new[] { -1, -1 };
                    item = ClothingRef.EmptyProp();
                }
                if (item == null)
                {
                    continue;
                }
                outfit.Refs.Set(slot, item.Clone());
                if (item.IsPending)
                {
                    pending.Add(new ClothingIssue { Slot = slot, Reason = ClothingIssue.NoBaseline, Detail = "kept as the code's original numbers" });
                }
            }
            return outfit;
        }

        // ---- Review log -----------------------------------------------------------------------

        internal static string ReviewJson(IEnumerable<MigrationResult> results) =>
            new JArray(results.Where(r => r.Issues.Count > 0).Select(r => new JObject
            {
                ["key"] = r.Key,
                ["pieces"] = new JArray(r.Issues.Select(i => new JObject { ["slot"] = i.Slot.Key, ["name"] = i.Slot.DisplayName, ["reason"] = i.Reason, ["detail"] = i.Detail })),
            })).ToString(Formatting.None);
    }
}
