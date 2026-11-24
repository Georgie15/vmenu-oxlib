using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static CitizenFX.Core.Native.API;
using static vMenuClient.MpPedDataManager;

namespace vMenuClient
{
    /// <summary>The game calls behind <see cref="IClothingRuntime"/> for one ped.</summary>
    internal sealed class NativeClothingRuntime : IClothingRuntime
    {
        private readonly int ped;

        internal NativeClothingRuntime(int ped, IDictionary<string, object> memo)
        {
            this.ped = ped;
            Model = (uint)GetEntityModel(ped);
            Memo = memo;
        }

        public uint Model { get; }
        public IDictionary<string, object> Memo { get; }

        private static string Name(string collection) => collection ?? "";

        internal List<string> Collections()
        {
            var names = new List<string>();
            var count = GetPedCollectionsCount(ped);
            for (var i = 0; i < count; i++)
            {
                names.Add(i == 0 ? "" : Name(GetPedCollectionName(ped, i)));
            }
            return names;
        }

        private int CollectionDrawables(ClothingSlot slot, string collection) => slot.Prop
            ? GetNumberOfPedCollectionPropDrawableVariations(ped, slot.Index, collection)
            : GetNumberOfPedCollectionDrawableVariations(ped, slot.Index, collection);

        public string LayoutPrint
        {
            get
            {
                if (Memo.TryGetValue("layout", out var cached) && cached is string print)
                {
                    return print;
                }
                var collections = Collections();
                print = ClothingPrints.Layout(slot => collections.Select(c => new KeyValuePair<string, int>(c, CollectionDrawables(slot, c))).ToList());
                Memo["layout"] = print;
                return print;
            }
        }

        public int ToGlobal(ClothingSlot slot, string collection, int local)
        {
            collection = Name(collection);
            if (local < 0 || local >= CollectionDrawables(slot, collection))
            {
                return -1;
            }
            return slot.Prop
                ? GetPedPropGlobalIndexFromCollection(ped, slot.Index, collection, local)
                : GetPedDrawableGlobalIndexFromCollection(ped, slot.Index, collection, local);
        }

        public int TextureCount(ClothingSlot slot, string collection, int local) => slot.Prop
            ? GetNumberOfPedCollectionPropTextureVariations(ped, slot.Index, Name(collection), local)
            : GetNumberOfPedCollectionTextureVariations(ped, slot.Index, Name(collection), local);

        public bool FromGlobal(ClothingSlot slot, int global, out string collection, out int local)
        {
            collection = null;
            local = -1;
            var total = slot.Prop ? GetNumberOfPedPropDrawableVariations(ped, slot.Index) : GetNumberOfPedDrawableVariations(ped, slot.Index);
            if (global < 0 || global >= total)
            {
                return false;
            }
            collection = Name(slot.Prop ? GetPedCollectionNameFromProp(ped, slot.Index, global) : GetPedCollectionNameFromDrawable(ped, slot.Index, global));
            local = slot.Prop ? GetPedCollectionLocalIndexFromProp(ped, slot.Index, global) : GetPedCollectionLocalIndexFromDrawable(ped, slot.Index, global);
            return local >= 0;
        }

        public int[] CollectionTextureCounts(string collection, ClothingSlot slot)
        {
            var key = "tex/" + collection + "/" + slot.Key;
            if (Memo.TryGetValue(key, out var cached) && cached is int[] counts)
            {
                return counts;
            }
            var drawables = CollectionDrawables(slot, Name(collection));
            counts = new int[Math.Max(0, drawables)];
            for (var i = 0; i < counts.Length; i++)
            {
                counts[i] = TextureCount(slot, collection, i);
            }
            Memo[key] = counts;
            return counts;
        }
    }

    /// <summary>
    /// Collection-based clothing for every vMenu feature: resolving and putting on saves, capturing
    /// what a ped wears, upgrading old saves on join, the outfit-code exports used by
    /// client/outfitCodes.lua and the staff layout capture. Policy: ClothingResolver.
    /// </summary>
    public class ClothingService : BaseScript
    {
        private const string LayoutsFile = "config/clothing-layouts.json";

        private static ClothingLayouts layouts;
        private static readonly Dictionary<uint, Dictionary<string, object>> memo = new();
        private static bool captureBusy;

        internal static ClothingLayouts Layouts
        {
            get
            {
                if (layouts == null)
                {
                    layouts = ClothingLayouts.Parse(LoadResourceFile(GetCurrentResourceName(), LayoutsFile), out var error);
                    if (error != null)
                    {
                        Debug.WriteLine($"[vMenu] [clothing] {error}");
                    }
                }
                return layouts;
            }
        }

        public ClothingService()
        {
            // exports.vMenu:loadOutfitFromCode puts a code straight on (client/outfitCodes.lua).
            // Saved Outfits loads and makes codes itself (MpPedCustomization.CreateSavedOutfitsMenu).
            Exports.Add("vMenuApplyOutfitCode", new Func<string, object, string>(ApplyOutfitCode));

            // Another resource streaming clothes starting or stopping changes the layout.
            EventHandlers["onClientResourceStart"] += new Action<string>(_ => memo.Clear());
            EventHandlers["onClientResourceStop"] += new Action<string>(_ => memo.Clear());
            EventHandlers["vMenu:ClothingCapture:Saved"] += new Action<string>(message => Notify.Info(message));

            RegisterCommand("vmenu_clothing_capture", new Action<int, List<object>, string>(async (_, args, __) => await CaptureCommand(args)), false);
            RegisterCommand("vmenu_clothing_review", new Action<int, List<object>, string>((_, __, ___) => PrintReview()), false);
            RegisterCommand("vmenu_clothing_restore", new Action<int, List<object>, string>((_, args, __) => RestoreCommand(args)), false);

            Tick += MigrateOnJoin;
        }

        internal static IClothingRuntime Runtime(int ped)
        {
            var model = (uint)GetEntityModel(ped);
            if (!memo.TryGetValue(model, out var cache))
            {
                cache = new Dictionary<string, object>();
                memo[model] = cache;
            }
            return new NativeClothingRuntime(ped, cache);
        }

        internal static string LayoutPrint(int ped) => Runtime(ped).LayoutPrint;

        internal static ClothingResolution Resolve(ClothingSave save, IEnumerable<ClothingSlot> slots, int ped) =>
            ClothingResolver.Resolve(save, slots, Layouts, Runtime(ped));

        /// <summary>Puts the resolved pieces on. Unresolved pieces are left as they are.</summary>
        internal static void Apply(ClothingResolution resolution, int ped, int palette)
        {
            foreach (var piece in resolution.Pieces)
            {
                var slot = piece.Slot;
                if (piece.Clear)
                {
                    ClearPedProp(ped, slot.Index);
                }
                else if (piece.Raw)
                {
                    if (slot.Prop)
                    {
                        SetPedPropIndex(ped, slot.Index, piece.Drawable, piece.Texture, true);
                    }
                    else
                    {
                        SetPedComponentVariation(ped, slot.Index, piece.Drawable, piece.Texture, palette);
                    }
                }
                else if (slot.Prop)
                {
                    SetPedCollectionPropIndex(ped, slot.Index, piece.Collection, piece.Drawable, piece.Texture, true);
                }
                else
                {
                    SetPedCollectionComponentVariation(ped, slot.Index, piece.Collection, piece.Drawable, piece.Texture, palette);
                }
            }
        }

        /// <summary>Console detail, and a short notice when pieces were left off.</summary>
        internal static void Report(string what, ClothingResolution resolution, bool notify)
        {
            foreach (var note in resolution.Notes)
            {
                Debug.WriteLine($"[vMenu] [clothing] {what}: {note}");
            }
            if (resolution.Issues.Count == 0)
            {
                return;
            }
            foreach (var issue in resolution.Issues)
            {
                Debug.WriteLine($"[vMenu] [clothing] {what}: {issue}");
            }
            if (notify)
            {
                var shown = string.Join("; ", resolution.Issues.Take(3).Select(i => i.PlayerText));
                var more = resolution.Issues.Count > 3 ? $" and {resolution.Issues.Count - 3} more" : "";
                Notify.Alert($"{what}: {shown}{more}. Those pieces were left off and your save is unchanged.");
            }
        }

        internal static void ReportNotes(string what, ClothingResolution resolution)
        {
            if (resolution.Notes.Count > 0)
            {
                Notify.Info($"{what} was {resolution.Notes[0]}. Check each piece.");
            }
        }

        /// <summary>A reference to what the ped wears in one slot; null when the game can't place it.</summary>
        internal static ClothingRef CaptureRef(int ped, ClothingSlot slot)
        {
            var global = slot.Prop ? GetPedPropIndex(ped, slot.Index) : GetPedDrawableVariation(ped, slot.Index);
            var texture = slot.Prop ? GetPedPropTextureIndex(ped, slot.Index) : GetPedTextureVariation(ped, slot.Index);
            return ClothingResolver.Capture(slot, global, texture, Layouts, Runtime(ped));
        }

        /// <summary>References to what the ped wears. With keepPending, slots still waiting for review in <paramref name="existing"/> are kept.</summary>
        internal static ClothingRefSet CaptureSet(int ped, IEnumerable<ClothingSlot> slots, ClothingRefSet existing = null, bool keepPending = false)
        {
            var model = (uint)GetEntityModel(ped);
            var set = existing != null && existing.Model == model ? existing.Clone() : new ClothingRefSet { Model = model };
            foreach (var slot in slots)
            {
                if (keepPending && set.Get(slot)?.IsPending == true)
                {
                    continue;
                }
                set.Set(slot, CaptureRef(ped, slot));
            }
            return set;
        }

        // ---- MP characters ----------------------------------------------------------------------

        internal static ClothingSave FromCharacter(MultiplayerPedData character)
        {
            var save = new ClothingSave { Model = character.ModelHash, GlobalsLayout = character.ClothingLayout };
            if (character.ClothingRefs != null && character.ClothingRefs.Version == ClothingRefSet.FormatVersion && character.ClothingRefs.Model == character.ModelHash)
            {
                save.Refs = character.ClothingRefs;
                save.Refs.Clothes ??= new Dictionary<int, ClothingRef>();
                save.Refs.Props ??= new Dictionary<int, ClothingRef>();
            }
            foreach (var item in character.DrawableVariations.clothes ?? new Dictionary<int, KeyValuePair<int, int>>())
            {
                save.Components[item.Key] = new[] { item.Value.Key, item.Value.Value };
            }
            foreach (var item in character.PropVariations.props ?? new Dictionary<int, KeyValuePair<int, int>>())
            {
                save.Props[item.Key] = new[] { item.Value.Key, item.Value.Value };
            }
            return save;
        }

        /// <summary>The character after a resolution: current references, and current numbers where pieces were put on.</summary>
        internal static MultiplayerPedData WithResolution(MultiplayerPedData character, ClothingResolution resolution)
        {
            character.DrawableVariations.clothes ??= new Dictionary<int, KeyValuePair<int, int>>();
            character.PropVariations.props ??= new Dictionary<int, KeyValuePair<int, int>>();
            foreach (var piece in resolution.Pieces.Where(p => !p.Raw))
            {
                var map = piece.Slot.Prop ? character.PropVariations.props : character.DrawableVariations.clothes;
                map[piece.Slot.Index] = piece.Clear ? new KeyValuePair<int, int>(-1, -1) : new KeyValuePair<int, int>(piece.Global, piece.Texture);
            }
            character.ClothingRefs = resolution.Refs;
            // Every slot whose number is not current has a reference (or its kept original), so the
            // numbers are now stamped with the running layout.
            character.ClothingLayout = resolution.RuntimeLayout;
            return character;
        }

        /// <summary>Records what the ped now wears in one slot of the character (the editor and its defaults).</summary>
        internal static MultiplayerPedData SetCharacterSlot(MultiplayerPedData character, int ped, ClothingSlot slot)
        {
            if ((uint)GetEntityModel(ped) != character.ModelHash)
            {
                return character;
            }
            var refs = character.ClothingRefs != null && character.ClothingRefs.Model == character.ModelHash
                ? character.ClothingRefs
                : new ClothingRefSet { Model = character.ModelHash };
            refs.Set(slot, CaptureRef(ped, slot));
            character.ClothingRefs = refs;
            character.ClothingLayout = LayoutPrint(ped);
            return character;
        }

        /// <summary>Before saving a character: references for every slot that has none yet, from the ped.</summary>
        internal static MultiplayerPedData CompleteFromPed(MultiplayerPedData character, int ped)
        {
            if ((uint)GetEntityModel(ped) != character.ModelHash)
            {
                return character;
            }
            var save = FromCharacter(character);
            foreach (var slot in ClothingSaves.CharacterSlots(save))
            {
                if (save.Refs?.Get(slot) == null)
                {
                    character = SetCharacterSlot(character, ped, slot);
                }
            }
            character.ClothingLayout = LayoutPrint(ped);
            return character;
        }

        // ---- Saved Peds -------------------------------------------------------------------------

        internal static ClothingSave FromPedInfo(CommonFunctions.PedInfo info, uint model)
        {
            var save = new ClothingSave { Model = model, GlobalsLayout = info.clothingLayout };
            if (info.clothingRefs != null && info.clothingRefs.Version == ClothingRefSet.FormatVersion && info.clothingRefs.Model == model)
            {
                save.Refs = info.clothingRefs;
                save.Refs.Clothes ??= new Dictionary<int, ClothingRef>();
                save.Refs.Props ??= new Dictionary<int, ClothingRef>();
            }
            foreach (var item in info.drawableVariations ?? new Dictionary<int, int>())
            {
                var texture = info.drawableVariationTextures != null && info.drawableVariationTextures.TryGetValue(item.Key, out var t) ? t : 0;
                save.Components[item.Key] = new[] { item.Value, texture };
            }
            foreach (var item in info.props ?? new Dictionary<int, int>())
            {
                var texture = info.propTextures != null && info.propTextures.TryGetValue(item.Key, out var t) ? t : -1;
                // As Saved Peds always did: a prop without a texture is taken off.
                save.Props[item.Key] = texture < 0 ? new[] { -1, -1 } : new[] { item.Value, texture };
            }
            return save;
        }

        // ---- Outfit codes (client/outfitCodes.lua) ---------------------------------------------

        private static string Reply(bool ok, string message, IEnumerable<ClothingIssue> issues = null)
        {
            var reply = new JObject { ["ok"] = ok, ["message"] = message };
            if (issues != null)
            {
                reply["issues"] = new JArray(issues.Select(i => i.ToString()));
            }
            return reply.ToString(Formatting.None);
        }

        private static long Unix(object value)
        {
            try
            {
                return value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// An outfit code fetched by client/outfitCodes.lua, as a Saved Outfits entry for the body
        /// <paramref name="ped"/> has. Null, with the reason, when the code is damaged or is for the
        /// other body type.
        /// </summary>
        internal static SavedOutfit OutfitFromCode(string codeJson, object created, int ped, out string error)
        {
            var code = ClothingSaves.ReadCode(codeJson, (uint)GetEntityModel(ped), Unix(created), out error);
            if (code == null)
            {
                return null;
            }
            var outfit = ClothingSaves.OutfitFromCode(code, Layouts, out var pending);
            foreach (var issue in pending)
            {
                Debug.WriteLine($"[vMenu] [clothing] outfit code: {issue}");
            }
            return outfit;
        }

        private static string ApplyOutfitCode(string codeJson, object created)
        {
            var ped = Game.PlayerPed.Handle;
            var save = ClothingSaves.ReadCode(codeJson, (uint)GetEntityModel(ped), Unix(created), out var error);
            if (save == null)
            {
                return Reply(false, error);
            }
            var slots = ClothingSaves.CharacterSlots(save);
            var resolution = Resolve(save, slots, ped);
            if (resolution.Pieces.Count == 0)
            {
                Report("Outfit code", resolution, false);
                return Reply(false, "None of this outfit's pieces could be matched on this server.", issues: resolution.Issues);
            }
            // As before: start from a bare body (head and hair kept), then dress.
            for (var i = 0; i < 12; i++)
            {
                if (i is not 0 and not 2)
                {
                    SetPedComponentVariation(ped, i, 0, 0, 0);
                }
            }
            for (var i = 0; i <= 12; i++)
            {
                ClearPedProp(ped, i);
            }
            Apply(resolution, ped, 0);
            Report("Outfit code", resolution, true);
            ReportNotes("This outfit code", resolution);
            return Reply(true, "", issues: resolution.Issues);
        }

        // ---- Upgrading old saves on join -------------------------------------------------------

        private static List<string> Keys(string prefix)
        {
            var keys = new List<string>();
            var handle = StartFindKvp(prefix);
            if (handle == -1)
            {
                return keys;
            }
            while (true)
            {
                var key = FindKvp(handle);
                if (string.IsNullOrEmpty(key))
                {
                    break;
                }
                keys.Add(key);
            }
            EndFindKvp(handle);
            return keys;
        }

        private async Task MigrateOnJoin()
        {
            Tick -= MigrateOnJoin;
            while (!NetworkIsSessionStarted())
            {
                await Delay(1000);
            }
            await Delay(3000);
            try
            {
                Migrate(notify: true);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[vMenu] [clothing] save upgrade stopped: {e.Message}");
            }
        }

        /// <summary>
        /// Upgrades every save made before collection references with the captured baseline. Each
        /// original is backed up first (once: the first original is never overwritten). A save is
        /// rewritten only when every piece converts; the rest are listed for review. Safe to run
        /// again: upgraded saves are skipped.
        /// </summary>
        internal static List<ClothingSaves.MigrationResult> Migrate(bool notify)
        {
            var keys = Keys(SavedOutfit.KeyPrefix).Concat(Keys("mp_ped_")).Concat(Keys("ped_")).Concat(new[] { ClothingSaves.LastSnapshotKey }).Distinct();
            var results = ClothingSaves.MigrateAll(keys, GetResourceKvpString, SetResourceKvp, Layouts, message => Debug.WriteLine($"[vMenu] [clothing] {message}"), out var upgraded);

            var review = results.Where(r => r.Issues.Count > 0).ToList();
            if (review.Count > 0)
            {
                SetResourceKvp(ClothingSaves.ReviewKey, ClothingSaves.ReviewJson(review));
            }
            else
            {
                DeleteResourceKvp(ClothingSaves.ReviewKey);
            }
            Debug.WriteLine($"[vMenu] [clothing] saves upgraded: {upgraded}, need review: {review.Count}, baseline: {Layouts.LegacyId ?? "not captured"}");
            if (notify && review.Count > 0 && Layouts.Legacy.Count > 0)
            {
                Notify.Alert($"{review.Count} of your saved outfits or characters have pieces that couldn't be matched to the current clothing. They were left unchanged. Type vmenu_clothing_review in F8 for details.");
            }
            return results;
        }

        private static void PrintReview()
        {
            var json = GetResourceKvpString(ClothingSaves.ReviewKey);
            if (string.IsNullOrEmpty(json))
            {
                Debug.WriteLine("[vMenu] [clothing] Nothing needs review.");
                return;
            }
            foreach (var save in JArray.Parse(json).OfType<JObject>())
            {
                Debug.WriteLine($"[vMenu] [clothing] {save["key"]}:");
                foreach (var piece in (save["pieces"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    Debug.WriteLine($"    {piece["name"]} ({piece["slot"]}): {piece["reason"]} {piece["detail"]}");
                }
            }
            Debug.WriteLine("[vMenu] [clothing] Originals of upgraded saves: vmenu_clothing_restore list");
        }

        private static void RestoreCommand(List<object> args)
        {
            var key = args.Count > 0 ? args[0]?.ToString() : null;
            if (string.IsNullOrEmpty(key) || key == "list")
            {
                foreach (var backup in Keys(ClothingSaves.BackupPrefix))
                {
                    Debug.WriteLine($"[vMenu] [clothing] {backup.Substring(ClothingSaves.BackupPrefix.Length)}");
                }
                Debug.WriteLine("[vMenu] [clothing] vmenu_clothing_restore <save key> puts that save back exactly as it was before the upgrade.");
                return;
            }
            var original = GetResourceKvpString(ClothingSaves.BackupPrefix + key);
            if (string.IsNullOrEmpty(original))
            {
                Debug.WriteLine($"[vMenu] [clothing] No original kept for {key}.");
                return;
            }
            SetResourceKvp(key, original);
            Debug.WriteLine($"[vMenu] [clothing] {key} restored to its original. It is upgraded again on your next join if it still converts.");
        }

        // ---- Layout capture (staff) ------------------------------------------------------------

        private sealed class CaptureRow
        {
            internal string Collection;
            internal int Start;
            internal List<int> Textures;
        }

        /// <summary>A ped of the model: the player's own, or a hidden local one (negative handle = delete it after).</summary>
        private static async Task<int> PedFor(uint model)
        {
            var player = Game.PlayerPed.Handle;
            if ((uint)GetEntityModel(player) == model)
            {
                return player;
            }
            RequestModel(model);
            var timeout = GetGameTimer() + 10000;
            while (!HasModelLoaded(model) && GetGameTimer() < timeout)
            {
                await Delay(0);
            }
            if (!HasModelLoaded(model))
            {
                return 0;
            }
            var position = GetEntityCoords(player, false);
            var ped = CreatePed(26, model, position.X, position.Y, position.Z - 50f, 0f, false, false);
            SetModelAsNoLongerNeeded(model);
            SetEntityVisible(ped, false, false);
            SetEntityCollision(ped, false, false);
            FreezeEntityPosition(ped, true);
            await Delay(0);
            return -ped;
        }

        /// <summary>The global numbering of one model as the running game reports it, drawable by drawable.</summary>
        private static async Task<JObject> CaptureModel(int ped, uint model, string name)
        {
            var slots = new JObject();
            var anomalies = new JArray();
            var calls = 0;
            foreach (var slot in ClothingSlot.AllLayoutSlots)
            {
                var total = slot.Prop ? GetNumberOfPedPropDrawableVariations(ped, slot.Index) : GetNumberOfPedDrawableVariations(ped, slot.Index);
                var rows = new List<CaptureRow>();
                for (var global = 0; global < total; global++)
                {
                    var collection = (slot.Prop ? GetPedCollectionNameFromProp(ped, slot.Index, global) : GetPedCollectionNameFromDrawable(ped, slot.Index, global)) ?? "";
                    var local = slot.Prop ? GetPedCollectionLocalIndexFromProp(ped, slot.Index, global) : GetPedCollectionLocalIndexFromDrawable(ped, slot.Index, global);
                    var textures = slot.Prop ? GetNumberOfPedPropTextureVariations(ped, slot.Index, global) : GetNumberOfPedTextureVariations(ped, slot.Index, global);
                    if (rows.Count > 0 && rows[rows.Count - 1].Collection == collection && local == rows[rows.Count - 1].Textures.Count)
                    {
                        rows[rows.Count - 1].Textures.Add(textures);
                    }
                    else
                    {
                        if (local != 0)
                        {
                            anomalies.Add($"{slot.Key} global {global}: {collection} local {local} does not start a run");
                        }
                        rows.Add(new CaptureRow { Collection = collection, Start = global, Textures = new List<int> { textures } });
                    }
                    if (++calls % 400 == 0)
                    {
                        await Delay(0);
                    }
                }
                if (rows.Count > 0)
                {
                    slots[slot.Key] = new JArray(rows.Select(r => new JArray(r.Collection, r.Start, r.Textures.Count, new JArray(r.Textures))));
                }
            }

            var runtime = (NativeClothingRuntime)Runtime(ped);
            var collections = runtime.Collections();
            var captured = new ModelLayout { Model = model };
            foreach (var p in slots.Properties())
            {
                captured.Slots[p.Name] = ((JArray)p.Value).OfType<JArray>()
                    .Select(r => new LayoutSegment { Collection = (string)r[0], Start = (int)r[1], Count = (int)r[2], Textures = ((JArray)r[3]).Select(t => (int)t).ToArray() }).ToList();
            }
            var print = captured.Print();
            if (print != runtime.LayoutPrint)
            {
                anomalies.Add($"drawable-by-drawable print {print} differs from the collection-count print {runtime.LayoutPrint}");
            }
            var pack = ClothingResolver.Pack(Layouts, runtime);
            return new JObject
            {
                ["name"] = name,
                ["layout"] = print,
                ["collections"] = new JArray(collections),
                ["pack"] = new JObject { ["revision"] = pack.Revision, ["mode"] = pack.Mode, ["print"] = pack.Print },
                ["anomalies"] = anomalies,
                ["slots"] = slots,
            };
        }

        private static async Task CaptureCommand(List<object> args)
        {
            var checkOnly = args.Count > 0 && string.Equals(args[0]?.ToString(), "check", StringComparison.OrdinalIgnoreCase);
            if (captureBusy)
            {
                return;
            }
            captureBusy = true;
            try
            {
                Notify.Info(checkOnly ? "Checking the clothing layout..." : "Capturing the clothing layout...");
                var resources = new JArray();
                for (var i = 0; i < GetNumResources(); i++)
                {
                    var resource = GetResourceByFindIndex(i);
                    if (!string.IsNullOrEmpty(resource) && GetResourceState(resource) == "started")
                    {
                        resources.Add(resource);
                    }
                }
                var capture = new JObject
                {
                    ["kind"] = "vmenu-clothing-capture",
                    ["version"] = 1,
                    ["gameBuild"] = GetGameBuildNumber(),
                    ["clientResources"] = resources,
                    ["models"] = new JObject(),
                };
                foreach (var target in new Dictionary<uint, string> { [ClothingLayouts.MaleFreemode] = "mp_m_freemode_01", [ClothingLayouts.FemaleFreemode] = "mp_f_freemode_01" })
                {
                    var model = target.Key;
                    var name = target.Value;
                    var handle = await PedFor(model);
                    var temporary = handle < 0;
                    var ped = Math.Abs(handle);
                    if (ped == 0)
                    {
                        Notify.Error($"Couldn't load {name}; nothing was captured.");
                        return;
                    }
                    try
                    {
                        var result = await CaptureModel(ped, model, name);
                        capture["models"][model.ToString(CultureInfo.InvariantCulture)] = result;
                        var baseline = Layouts.LegacyFor(model);
                        var verdict = baseline == null ? "no baseline in config" : baseline.Print() == (string)result["layout"] ? "MATCHES the baseline" : "DIFFERS from the baseline";
                        Debug.WriteLine($"[vMenu] [clothing] {name}: layout {result["layout"]} ({verdict}); pack {result["pack"]?["revision"] ?? "unknown"} ({result["pack"]?["mode"]}); anomalies {((JArray)result["anomalies"]).Count}");
                    }
                    finally
                    {
                        if (temporary)
                        {
                            DeletePed(ref ped);
                        }
                    }
                }
                if (checkOnly)
                {
                    Notify.Info("Clothing layout checked. See F8.");
                    return;
                }
                TriggerLatentServerEvent("vMenu:ClothingCapture:Save", 100000, capture.ToString(Formatting.None));
                Notify.Info("Clothing layout sent to the server.");
            }
            finally
            {
                captureBusy = false;
            }
        }
    }
}
