// Run: dotnet run --project tests/clothing-refs
// Drives the production clothing resolver, save upgrades and outfit-code payloads against a
// pretend game (FakeClothing.cs): a saved uniform must survive a global number shift, a drawable
// move inside a PSRP collection and a texture reorder, for both freemode models, clothes and props
// and an empty prop. Old saves are read with the captured baseline only, and anything ambiguous,
// retired or replaced is reported and left exactly as it was: never put on as something else and
// never saved back as something else.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using vMenuClient;

static class Program
{
    const uint Male = ClothingLayouts.MaleFreemode;
    const uint Female = ClothingLayouts.FemaleFreemode;
    const string Dept = "mp_m_deptmerge2";
    const string Leo = "mp_m_leoaccessories";

    static int checks;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static ClothingSlot C(int i) => ClothingSlot.Component(i);
    static ClothingSlot P(int i) => ClothingSlot.PropSlot(i);

    // ---- The pretend server -------------------------------------------------------------------

    // The live layout before the FIB change (pack revision A).
    static FakeRuntime MaleBefore()
    {
        var rt = new FakeRuntime(Male);
        rt.Fill("c0", "", 1, 1).Fill("c1", "", 10, 3).Fill("c2", "", 20, 1).Fill("c3", "", 20, 2);
        rt.Fill("c4", "", 30, 4).Add("c4", Dept, 1, 2, 3, 1, 1);
        foreach (var c in new[] { 5, 6, 7, 8, 9, 10 }) rt.Fill("c" + c, "", 10, 2);
        rt.Fill("c11", "", 40, 5).Fill("c11", "mp_m_business_01", 3, 2).Add("c11", Dept, 1, 3, 4, 2, 1, 1).Add("c11", Leo, 1, 1);
        rt.Fill("p0", "", 15, 3).Add("p0", Dept, 1, 2, 1, 1);
        rt.Fill("p1", "", 5, 2).Fill("p6", "", 4, 1);
        return rt;
    }

    // Another resource adds a collection before the pack: every pack global number moves by 7.
    static FakeRuntime MaleShifted()
    {
        var rt = MaleBefore();
        rt.Slots["c11"].Insert(2, new KeyValuePair<string, int[]>("mp_m_newdlc", Enumerable.Repeat(2, 7).ToArray()));
        rt.Slots["p0"].Insert(1, new KeyValuePair<string, int[]>("mp_m_newdlc", Enumerable.Repeat(1, 7).ToArray()));
        rt.Slots["c4"].Insert(1, new KeyValuePair<string, int[]>("mp_m_newdlc", Enumerable.Repeat(1, 3).ToArray()));
        rt.Memo.Clear();
        return rt;
    }

    // The FIB repack (pack revision B), on top of the shift: Upper drawable 2 retired (later ones
    // move down by one), drawable 1's three textures reordered, a lower-body item replaced, and a
    // new FIB collection appended.
    static FakeRuntime MaleRepacked()
    {
        var rt = MaleShifted();
        var c11 = rt.Slots["c11"];
        var dept = c11.FindIndex(s => s.Key == Dept);
        c11[dept] = new KeyValuePair<string, int[]>(Dept, new[] { 1, 3, 2, 1, 1 });
        rt.Add("c11", "mp_m_fibeup", 2, 2);
        rt.Memo.Clear();
        return rt;
    }

    static FakeRuntime FemaleBefore()
    {
        var rt = new FakeRuntime(Female);
        rt.Fill("c3", "", 15, 2).Fill("c4", "", 20, 3).Add("c4", "mp_f_deptmerge2", 2, 2);
        rt.Fill("c8", "", 12, 2).Fill("c11", "", 30, 4).Add("c11", "mp_f_deptmerge2", 3, 1, 5);
        rt.Fill("p0", "", 10, 2).Add("p0", "mp_f_deptmerge2", 2);
        rt.Fill("p1", "", 4, 1);
        return rt;
    }

    static FakeRuntime FemaleShifted()
    {
        var rt = FemaleBefore();
        rt.Slots["c11"].Insert(1, new KeyValuePair<string, int[]>("mp_f_newdlc", new[] { 1, 1, 1, 1 }));
        rt.Slots["p0"].Insert(1, new KeyValuePair<string, int[]>("mp_f_newdlc", new[] { 1, 1 }));
        rt.Memo.Clear();
        return rt;
    }

    static readonly string[] MalePack = { Dept, Leo };
    static readonly string[] FemalePack = { "mp_f_deptmerge2" };

    /// <summary>A layouts file as tools/clothing-layouts writes it, parsed by the production parser.</summary>
    static ClothingLayouts Layouts(bool baseline = true, bool legacyRev = true, bool repack = true, bool strict = true, string validFrom = null)
    {
        var maleA = MaleBefore().ToRevision("A", MalePack);
        var femaleA = FemaleBefore().ToRevision("A", FemalePack);
        var maleB = MaleRepacked().ToRevision("B", MalePack.Concat(new[] { "mp_m_fibeup" }).ToArray());
        JObject Rev(string id, params PackRevision[] parts) => new JObject
        {
            ["id"] = id,
            ["collections"] = new JArray(parts.SelectMany(p => p.Collections).Distinct()),
            ["counts"] = new JObject(parts.Select(p => new JProperty(p.Counts.Keys.Single().ToString(), JObject.FromObject(p.Counts.Values.Single())))),
        };
        JObject Base(FakeRuntime rt) => new JObject
        {
            ["name"] = rt.Model == Male ? "mp_m_freemode_01" : "mp_f_freemode_01",
            ["layout"] = rt.LayoutPrint,
            ["slots"] = new JObject(rt.ToBaseline().Slots.Select(s => new JProperty(s.Key, new JArray(s.Value.Select(r => new JArray(r.Collection, r.Start, r.Count, new JArray(r.Textures))))))),
        };
        JObject Entry(string slot, int drw, int? tex, string action, int? toDrw = null, int? toTex = null, string why = null)
        {
            var e = new JObject { ["model"] = Male.ToString(), ["slot"] = slot, ["col"] = Dept, ["drw"] = drw, ["action"] = action };
            if (tex != null) e["tex"] = tex;
            if (toDrw != null) e["toDrw"] = toDrw;
            if (toTex != null) e["toTex"] = toTex;
            if (why != null) e["why"] = why;
            return e;
        }
        var root = new JObject
        {
            ["version"] = 1,
            ["legacy"] = new JObject
            {
                ["id"] = baseline ? "live-before-fib" : null,
                ["rev"] = legacyRev ? "A" : null,
                ["capturedAt"] = "2026-11-10T12:00:00Z",
                ["validFrom"] = validFrom,
                ["validFromUnix"] = validFrom == null ? 0 : DateTimeOffset.Parse(validFrom).ToUnixTimeSeconds(),
                ["models"] = baseline ? new JObject { [Male.ToString()] = Base(MaleBefore()), [Female.ToString()] = Base(FemaleBefore()) } : new JObject(),
            },
            ["pack"] = new JObject
            {
                ["current"] = repack ? "B" : "A",
                ["strict"] = strict,
                ["revisions"] = repack ? new JArray(Rev("A", maleA, femaleA), Rev("B", maleB, femaleA)) : new JArray(Rev("A", maleA, femaleA)),
                ["steps"] = !repack ? new JArray() : new JArray(new JObject
                {
                    ["from"] = "A",
                    ["to"] = "B",
                    ["entries"] = new JArray(
                        Entry("c11", 1, 0, "move", toTex: 1), Entry("c11", 1, 1, "move", toTex: 2), Entry("c11", 1, 2, "move", toTex: 0),
                        Entry("c11", 2, null, "retired", why: "FIB jacket retired in the revamp"),
                        Entry("c11", 3, null, "move", toDrw: 2), Entry("c11", 4, null, "move", toDrw: 3), Entry("c11", 5, null, "move", toDrw: 4),
                        Entry("c4", 4, 0, "review", why: "FIB trousers replaced; needs a decision")),
                }),
            },
        };
        var layouts = ClothingLayouts.Parse(root.ToString(), out var error);
        Check(error == null, "test layouts parse: " + error);
        return layouts;
    }

    // A character saved before collection references: global numbers from the live layout.
    static ClothingSave LegacyMale() => new()
    {
        Model = Male,
        Components =
        {
            [1] = new[] { 2, 1 },          // base mask
            [3] = new[] { 5, 0 },          // base arms
            [4] = new[] { 31, 1 },         // pack lower body: deptmerge2 #1 texture 1
            [11] = new[] { 44, 2 },        // pack jacket: deptmerge2 #1 texture 2 (40 base + 3 business)
        },
        Props =
        {
            [0] = new[] { 17, 0 },         // pack hat: deptmerge2 #2
            [1] = new[] { -1, -1 },        // no glasses: an explicit empty prop
            [6] = new[] { 3, 0 },          // base watch
        },
    };

    static ResolvedPiece Piece(ClothingResolution r, ClothingSlot slot) => r.Pieces.SingleOrDefault(p => p.Slot.Equals(slot));
    static ClothingIssue Issue(ClothingResolution r, ClothingSlot slot) => r.Issues.SingleOrDefault(i => i.Slot.Equals(slot));
    static IEnumerable<ClothingSlot> Slots(ClothingSave s) => ClothingSaves.CharacterSlots(s);

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        return dir;
    }

    static int Main()
    {
        Prints();
        GlobalShift();
        MovesAndReorders();
        AmbiguousLegacy();
        StampedNumbers();
        PackChecks();
        FemaleFreemode();
        Migration();
        OutfitCodes();
        SavedOutfits();
        ShippedConfig();
        StorageAndWiring();
        Console.WriteLine($"PASS: clothing references ({checks} checks)");
        return 0;
    }

    static void Prints()
    {
        // Pinned to the same values tools/clothing-layouts computes (tests/clothing-layouts).
        Check(ClothingPrints.Fnv64("") == "cbf29ce484222325" && ClothingPrints.Fnv64("vMenu") == "2f255d694ef3b77e", "FNV-1a 64");
        var rt = new FakeRuntime(Male).Add("c11", "", 1, 1, 1).Add("c11", Dept, 1, 3).Add("p0", "", 2);
        Check(rt.LayoutPrint == "29c1b6590c7eb66e", "layout print matches the Python tool: " + rt.LayoutPrint);
        Check(rt.ToBaseline().Print() == rt.LayoutPrint, "a captured baseline prints like the game it came from");
        Check(ClothingPrints.Pack(new[] { Dept }, rt.CollectionTextureCounts) == "04b248cc583fef0f", "pack print matches the Python tool");
        Console.WriteLine("PASS: prints");
    }

    static void GlobalShift()
    {
        var layouts = Layouts(repack: false);
        var runtime = MaleShifted();
        var legacy = LegacyMale();
        var r = ClothingResolver.Resolve(legacy, Slots(legacy), layouts, runtime);
        Check(r.Issues.Count == 0 && r.Clean, "an old save after a global shift resolves cleanly: " + string.Join("; ", r.Issues));
        var jacket = Piece(r, C(11));
        Check(jacket.Collection == Dept && jacket.Drawable == 1 && jacket.Texture == 2 && jacket.Global == 51, "the jacket is still deptmerge2 #1 (now global 51, not 44)");
        Check(runtime.FromGlobal(C(11), 44, out var wrong, out _) && wrong == "mp_m_newdlc", "read naively, global 44 would now be another resource's item");
        var lower = Piece(r, C(4));
        Check(lower.Collection == Dept && lower.Drawable == 1 && lower.Global == 34, "lower body follows its collection too");
        Check(Piece(r, C(1)).Collection == "" && Piece(r, C(1)).Global == 2, "base-game items keep the empty collection name");
        Check(Piece(r, P(0)).Collection == Dept && Piece(r, P(0)).Drawable == 2 && Piece(r, P(0)).Global == 24, "pack hat follows its collection");
        Check(Piece(r, P(1)).Clear && r.Refs.Get(P(1)).Empty, "an empty prop stays explicitly empty");
        Check(Piece(r, P(6)).Global == 3 && !Piece(r, P(6)).Raw, "base watch");
        var jacketRef = r.Refs.Get(C(11));
        Check(jacketRef.Revision == "A" && jacketRef.PackPrint != null && jacketRef.LayoutPrint == runtime.LayoutPrint, "a pack reference records the pack revision and prints");
        Check(r.Refs.Get(C(1)).Revision == null && r.Refs.Get(C(1)).Collection == "", "base-game references carry no pack revision");
        Console.WriteLine("PASS: global number shift (male, clothes and props, empty prop)");
    }

    static void MovesAndReorders()
    {
        var layouts = Layouts();
        var before = MaleBefore();
        // References saved under revision A by wearing and saving the uniform before the repack.
        var saved = ClothingResolver.Resolve(LegacyMale(), Slots(LegacyMale()), layouts, before).Refs;
        saved.Set(C(11), ClothingResolver.Capture(C(11), 43 + 1, 0, layouts, before));   // deptmerge2 #1 texture 0
        saved.Set(C(9), ClothingResolver.Capture(C(9), 4, 1, layouts, before));          // base armour
        saved.Set(C(4), ClothingResolver.Capture(C(4), 34, 0, layouts, before));         // deptmerge2 #4: replaced in B
        Check(saved.Get(C(11)).Revision == "A", "captured under the pre-repack pack");
        var repacked = MaleRepacked();
        var save = new ClothingSave { Model = Male, Refs = saved };
        var r = ClothingResolver.Resolve(save, Slots(save), layouts, repacked);

        var jacket = Piece(r, C(11));
        Check(jacket != null && jacket.Drawable == 1 && jacket.Texture == 1, "a reordered texture follows the verified map (texture 0 -> 1)");
        Check(r.Refs.Get(C(11)).Revision == "B" && r.Refs.Get(C(11)).Texture == 1, "the kept reference is now current");
        Check(Issue(r, C(4)) is { Reason: ClothingIssue.NeedsDecision } && Piece(r, C(4)) == null, "a replaced item needs a decision and is not put on");
        Check(r.Refs.Get(C(4)).SameItem(saved.Get(C(4))) && r.Refs.Get(C(4)).Revision == "A", "the replaced item's original reference is kept for recovery");
        Check(Piece(r, P(0)).Drawable == 2 && Piece(r, C(1)).Global == 2 && Piece(r, C(9)).Global == 4, "everything else still goes on");

        // A garment moved inside its collection.
        var moved = saved.Clone();
        moved.Set(C(11), ClothingResolver.Capture(C(11), 43 + 4, 0, layouts, before)); // deptmerge2 #4
        r = ClothingResolver.Resolve(new ClothingSave { Model = Male, Refs = moved }, new[] { C(11) }, layouts, repacked);
        Check(Piece(r, C(11)) is { Collection: Dept, Drawable: 3, Texture: 0 } && Piece(r, C(11)).Global == repacked.ToGlobal(C(11), Dept, 3), "a moved drawable is followed (#4 -> #3)");
        Check(r.Issues.Count == 0, "and nothing to review");

        // A retired item.
        var retired = saved.Clone();
        retired.Set(C(11), ClothingResolver.Capture(C(11), 43 + 2, 3, layouts, before));
        r = ClothingResolver.Resolve(new ClothingSave { Model = Male, Refs = retired }, new[] { C(11), C(1) }, layouts, repacked);
        Check(Issue(r, C(11)) is { Reason: ClothingIssue.Retired } && Issue(r, C(11)).Detail.Contains("retired"), "a retired item is reported");
        Check(Piece(r, C(11)) == null && Piece(r, C(1)) != null, "it is not put on (not even as what now sits at #2); the rest is");
        Check(r.Refs.Get(C(11)).Drawable == 2 && r.Refs.Get(C(11)).Texture == 3 && r.Refs.Get(C(11)).Revision == "A", "its reference is kept unchanged");
        Check(Issue(r, C(11)).PlayerText.Contains("removed from the clothing pack"), "players are told plainly");

        // The same old global-number save through both changes at once.
        r = ClothingResolver.Resolve(LegacyMale(), Slots(LegacyMale()), layouts, repacked);
        Check(Piece(r, C(11)) is { Collection: Dept, Drawable: 1, Texture: 0 }, "old save: baseline (#1 t2), then the reorder map (t2 -> t0)");
        Check(Issue(r, C(4)) == null && Piece(r, C(4)).Drawable == 1, "old save: unaffected pack items go on");
        Console.WriteLine("PASS: drawable move, texture reorder, replaced and retired items");
    }

    static void AmbiguousLegacy()
    {
        var layouts = Layouts();
        var odd = LegacyMale();
        odd.Components[11] = new[] { 200, 0 }; // past the end of the captured layout
        odd.Components[3] = new[] { 5, 9 };    // texture the baseline says doesn't exist
        var r = ClothingResolver.Resolve(odd, Slots(odd), layouts, MaleRepacked());
        Check(Issue(r, C(11)) is { Reason: ClothingIssue.NotInBaseline } && Piece(r, C(11)) == null, "a number the baseline doesn't have is never put on");
        Check(Issue(r, C(3)) is { Reason: ClothingIssue.BadTexture } && Piece(r, C(3)) == null, "nor a texture the baseline says didn't exist");
        Check(r.Refs.Get(C(11)).IsPending && r.Refs.Get(C(11)).Legacy.SequenceEqual(new[] { 200, 0 }), "the original numbers are kept as they were");
        Check(MaleRepacked().FromGlobal(C(11), 200, out _, out _) == false || true, "(whatever 200 means now is irrelevant)");
        var migrated = ClothingSaves.Migrate("mp_ped_Odd", CharacterJson(odd), layouts);
        Check(migrated.NewJson == null && migrated.Issues.Select(i => i.Slot).OrderBy(s => s.Key).SequenceEqual(new[] { C(11), C(3) }.OrderBy(s => s.Key)), "a save with an ambiguous piece is never rewritten; the pieces are listed");

        // No baseline, and the pack the old numbers were made with is gone: refuse, don't guess.
        var noBaseline = Layouts(baseline: false);
        r = ClothingResolver.Resolve(LegacyMale(), Slots(LegacyMale()), noBaseline, MaleRepacked());
        Check(r.Pieces.All(p => p.Clear) && r.Pieces.Count == 1 && r.Issues.Count == 6 && r.Issues.All(i => i.Reason == ClothingIssue.NoBaseline), "without a baseline after the pack changed, nothing old is put on (empty props stay empty)");
        // Same, but the old pack still runs: the numbers still mean the same garments.
        r = ClothingResolver.Resolve(LegacyMale(), Slots(LegacyMale()), noBaseline, MaleBefore());
        Check(r.Issues.Count == 0 && r.Unverified && !r.Clean && Piece(r, C(11)).Raw && Piece(r, C(11)).Global == 44, "while the old pack runs, old numbers go on as before but are marked unverified");
        Check(ClothingSaves.Migrate("mp_ped_Main", CharacterJson(LegacyMale()), noBaseline) is { NewJson: null, Skipped: true }, "and nothing is rewritten without a baseline");
        // Not configured at all (the rollout before the capture): exactly as before.
        r = ClothingResolver.Resolve(LegacyMale(), Slots(LegacyMale()), Layouts(baseline: false, legacyRev: false), MaleShifted());
        Check(r.Issues.Count == 0 && r.Unverified && Piece(r, C(11)).Global == 44, "before a baseline is declared, behaviour is unchanged (and unverified)");
        // A model the clothing packs don't touch.
        var cop = new ClothingSave { Model = 0x5E3DA4A4, Components = { [11] = new[] { 3, 1 } } };
        var copRuntime = new FakeRuntime(0x5E3DA4A4).Fill("c11", "", 5, 2);
        r = ClothingResolver.Resolve(cop, new[] { C(11) }, layouts, copRuntime);
        Check(Piece(r, C(11)).Raw && Piece(r, C(11)).Global == 3, "untracked ped models keep their numbers");
        Console.WriteLine("PASS: ambiguous old numbers are never put on or saved as something else");
    }

    static void StampedNumbers()
    {
        var layouts = Layouts();
        var now = MaleRepacked();
        // vMenu_tmp_saved_ped: numbers written at death under the running layout.
        var tmp = new ClothingSave { Model = Male, GlobalsLayout = now.LayoutPrint, Components = { [11] = new[] { 51, 1 } } };
        var r = ClothingResolver.Resolve(tmp, new[] { C(11) }, layouts, now);
        Check(Piece(r, C(11)) is { Global: 51, Collection: Dept, Drawable: 1, Texture: 1 }, "numbers stamped with the running layout are current");
        tmp.GlobalsLayout = "0123456789abcdef";
        r = ClothingResolver.Resolve(tmp, new[] { C(11) }, layouts, now);
        Check(Issue(r, C(11)) is { Reason: ClothingIssue.LayoutChanged } && Piece(r, C(11)) == null, "numbers stamped with another layout are not guessed");
        tmp.GlobalsLayout = MaleBefore().LayoutPrint;
        tmp.Components[11] = new[] { 44, 1 };
        r = ClothingResolver.Resolve(tmp, new[] { C(11) }, layouts, now);
        Check(Piece(r, C(11)) is { Drawable: 1, Texture: 2 }, "numbers stamped with the baseline layout are read with the baseline (then mapped)");
        Console.WriteLine("PASS: stamped global numbers");
    }

    static void PackChecks()
    {
        var layouts = Layouts(repack: false);
        var before = MaleBefore();
        var jacket = ClothingResolver.Capture(C(11), 44, 1, layouts, before);
        // The pack changed without a declared revision (counts differ, not just additions).
        var unknown = MaleBefore();
        unknown.Slots["c11"][2] = new KeyValuePair<string, int[]>(Dept, new[] { 1, 2, 4, 2, 1, 1 });
        unknown.Memo.Clear();
        var save = new ClothingSave { Model = Male, Refs = new ClothingRefSet { Model = Male } };
        save.Refs.Set(C(11), jacket);
        var r = ClothingResolver.Resolve(save, new[] { C(11) }, layouts, unknown);
        Check(Issue(r, C(11)) is { Reason: ClothingIssue.PackUnverified } && Piece(r, C(11)) == null, "an undeclared repack: pack items wait for a verified map");
        // Captured under that same unknown pack: it is the same pack, so it goes on.
        var same = ClothingResolver.Capture(C(11), 44, 1, layouts, unknown);
        Check(same.Revision == null && same.PackPrint != null, "unknown pack: no revision, but its print");
        save.Refs.Set(C(11), same);
        r = ClothingResolver.Resolve(save, new[] { C(11) }, layouts, unknown);
        Check(r.Issues.Count == 0 && Piece(r, C(11)).Drawable == 1, "a reference saved with the same pack goes on");
        // Only additions: compatible, so references stay valid.
        var grown = MaleBefore();
        grown.Slots["c11"][2] = new KeyValuePair<string, int[]>(Dept, new[] { 1, 3, 4, 2, 1, 1, 2 });
        grown.Slots["c11"][3] = new KeyValuePair<string, int[]>(Leo, new[] { 2, 1 });
        grown.Memo.Clear();
        Check(ClothingResolver.Pack(layouts, grown).Mode == "compatible", "appended drawables and textures are compatible");
        save.Refs.Set(C(11), jacket);
        r = ClothingResolver.Resolve(save, new[] { C(11) }, layouts, grown);
        Check(r.Issues.Count == 0 && Piece(r, C(11)).Drawable == 1, "references survive additions");
        // Not strict: the owner chose to trust collection references over pack checks.
        r = ClothingResolver.Resolve(save, new[] { C(11) }, Layouts(repack: false, strict: false), unknown);
        Check(r.Issues.Count == 0, "strict off: pack items go on by collection reference");
        // A reference saved before pack revisions were configured: only its layout print.
        var bare = new ClothingRef { Collection = Dept, Drawable = 1, Texture = 0, LayoutPrint = MaleBefore().LayoutPrint };
        save.Refs.Set(C(11), bare);
        r = ClothingResolver.Resolve(save, new[] { C(11) }, Layouts(), MaleRepacked());
        Check(Piece(r, C(11)) is { Drawable: 1, Texture: 1 }, "the baseline layout print means the baseline's pack revision, so the map applies");
        // Missing collection and bad texture.
        var noLeo = MaleBefore();
        noLeo.Slots["c11"].RemoveAt(3);
        noLeo.Memo.Clear();
        save.Refs.Set(C(11), ClothingResolver.Capture(C(11), 49, 0, layouts, before));
        Check(save.Refs.Get(C(11)).Collection == Leo, "leo accessory");
        r = ClothingResolver.Resolve(save, new[] { C(11) }, Layouts(repack: false, strict: false), noLeo);
        Check(Issue(r, C(11)) is { Reason: ClothingIssue.Missing }, "a collection that isn't running is reported, not substituted");
        save.Refs.Set(C(11), new ClothingRef { Collection = "", Drawable = 3, Texture = 7 });
        r = ClothingResolver.Resolve(save, new[] { C(11) }, layouts, before);
        Check(Issue(r, C(11)) is { Reason: ClothingIssue.BadTexture }, "a texture the drawable doesn't have is reported");
        r = ClothingResolver.Resolve(save, new[] { C(11) }, layouts, FemaleBefore());
        Check(Issue(r, C(11)) is { Reason: ClothingIssue.WrongModel } && r.Pieces.Count == 0, "a male save is never put on a female ped");
        Console.WriteLine("PASS: pack revision checks, missing items, model validation");
    }

    static void FemaleFreemode()
    {
        var layouts = Layouts();
        var save = new ClothingSave
        {
            Model = Female,
            Components = { [4] = new[] { 21, 1 }, [11] = new[] { 32, 4 }, [8] = new[] { 3, 1 } },
            Props = { [0] = new[] { 10, 0 }, [1] = new[] { -1, -1 } },
        };
        var r = ClothingResolver.Resolve(save, Slots(save), layouts, FemaleShifted());
        Check(r.Clean, "female old save resolves cleanly: " + string.Join("; ", r.Issues));
        Check(Piece(r, C(11)) is { Collection: "mp_f_deptmerge2", Drawable: 2, Texture: 4, Global: 36 }, "female jacket follows its collection after the shift");
        Check(Piece(r, P(0)) is { Collection: "mp_f_deptmerge2", Drawable: 0, Global: 12 }, "female hat follows its collection");
        Check(Piece(r, P(1)).Clear && Piece(r, C(4)).Global == 21, "empty glasses; unshifted lower body");
        Console.WriteLine("PASS: female freemode");
    }

    static string CharacterJson(ClothingSave s) => new JObject
    {
        ["ModelHash"] = s.Model,
        ["IsMale"] = s.Model == Male,
        ["Version"] = 1,
        ["SaveName"] = "mp_ped_Main",
        ["PedHeadBlendData"] = new JObject { ["FirstFaceShape"] = 21, ["ParentFaceShapePercent"] = 0.35 },
        ["FaceShapeFeatures"] = new JObject { ["features"] = new JObject { ["0"] = -0.123456789, ["5"] = 0.5 } },
        ["PedAppearance"] = new JObject { ["hairStyle"] = 19, ["hairColor"] = 3, ["eyeColor"] = 2 },
        ["PedTatttoos"] = new JObject { ["TorsoTattoos"] = new JArray(new JObject { ["Key"] = "mpbeach_overlays", ["Value"] = "MP_Bea_M_Back_000" }) },
        ["DrawableVariations"] = new JObject { ["clothes"] = new JObject(s.Components.Select(p => new JProperty(p.Key.ToString(), new JObject { ["Key"] = p.Value[0], ["Value"] = p.Value[1] }))
            .Concat(new[] { new JProperty("2", new JObject { ["Key"] = 19, ["Value"] = 0 }) })) },
        ["PropVariations"] = new JObject { ["props"] = new JObject(s.Props.Select(p => new JProperty(p.Key.ToString(), new JObject { ["Key"] = p.Value[0], ["Value"] = p.Value[1] }))) },
        ["Category"] = "Police",
    }.ToString(Formatting.None);

    static void Migration()
    {
        var layouts = Layouts();
        var original = CharacterJson(LegacyMale());
        var result = ClothingSaves.Migrate("mp_ped_Main", original, layouts);
        Check(result.NewJson != null && result.Issues.Count == 0, "an old character converts with the baseline alone");
        var before = JObject.Parse(original);
        var after = JObject.Parse(result.NewJson);
        Check(after["ClothingRefs"] != null && (string)after["ClothingLayout"] == MaleBefore().LayoutPrint, "references added; the old numbers are stamped with the baseline layout");
        after.Remove("ClothingRefs");
        after.Remove("ClothingLayout");
        Check(JToken.DeepEquals(before, after), "nothing else in the save changes (face, hair, tattoos, old numbers for older vMenu)");
        var refs = ClothingSaves.Read(ClothingSaves.Kind.Character, result.NewJson).Refs;
        Check(refs.Get(C(11)) is { Collection: Dept, Drawable: 1, Texture: 2, Revision: "A" } && refs.Get(P(1)).Empty && refs.Get(C(2)) == null, "references: pack jacket, empty glasses, no hair");
        Check(ClothingSaves.Migrate("mp_ped_Main", result.NewJson, layouts).AlreadyCurrent, "running again skips it (safe to retry)");

        // The upgraded save puts on the same thing as the old one on every later layout.
        foreach (var runtime in new[] { MaleShifted(), MaleRepacked() })
        {
            var fromOld = ClothingResolver.Resolve(LegacyMale(), Slots(LegacyMale()), layouts, runtime);
            var upgraded = ClothingSaves.Read(ClothingSaves.Kind.Character, result.NewJson);
            var fromNew = ClothingResolver.Resolve(upgraded, Slots(upgraded), layouts, runtime);
            Check(fromOld.Pieces.Count == fromNew.Pieces.Count && fromOld.Pieces.All(p => Piece(fromNew, p.Slot) is { } q && q.Global == p.Global && q.Texture == p.Texture && q.Clear == p.Clear),
                "upgraded and original saves dress the ped identically");
        }

        // Saved Outfits and Saved Peds.
        var outfit = SavedOutfit.Capture(Male, s => s == 11 ? 44 : 0, s => s == 11 ? 2 : 0, p => p == 0 ? 17 : -1, p => 0);
        var outfitResult = ClothingSaves.Migrate(SavedOutfit.KeyFor("Duty"), outfit.ToJson(), layouts);
        Check(outfitResult.NewJson != null && SavedOutfit.Parse(outfitResult.NewJson).Refs.Get(C(11)).Drawable == 1, "Saved Outfits upgrade");
        var ped = new JObject
        {
            ["version"] = 1, ["model"] = Male, ["isMpPed"] = true,
            ["drawableVariations"] = new JObject(Enumerable.Range(0, 12).Select(i => new JProperty(i.ToString(), i == 11 ? 44 : i == 2 ? 3 : 0))),
            ["drawableVariationTextures"] = new JObject(Enumerable.Range(0, 12).Select(i => new JProperty(i.ToString(), i == 11 ? 2 : 0))),
            ["props"] = new JObject(Enumerable.Range(0, 21).Select(i => new JProperty(i.ToString(), i == 0 ? 17 : -1))),
            ["propTextures"] = new JObject(Enumerable.Range(0, 21).Select(i => new JProperty(i.ToString(), i == 0 ? 0 : -1))),
        };
        var pedResult = ClothingSaves.Migrate("ped_Cop", ped.ToString(), layouts);
        var pedRefs = ClothingSaves.Read(ClothingSaves.Kind.SavedPed, pedResult.NewJson).Refs;
        Check(pedRefs.Get(C(11)).Collection == Dept && pedRefs.Get(C(2)) != null && pedRefs.Get(C(0)) != null && pedRefs.Get(P(0)).Drawable == 2 && pedRefs.Get(P(12)).Empty,
            "Saved Peds keep head and hair too (that feature always restored them)");
        ped["model"] = 0x5E3DA4A4;
        Check(ClothingSaves.Migrate("ped_Other", ped.ToString(), layouts).Skipped, "non-freemode Saved Peds are left alone");
        Check(ClothingSaves.Migrate("vmenu_clothing_backup_mp_ped_Main", original, layouts).Skipped && ClothingSaves.KindOf("vMenu_tmp_saved_ped") == null, "backups and the temporary death save are never migrated");
        Check(ClothingSaves.Migrate("mp_ped_Bad", "{", layouts).Skipped, "unreadable saves are skipped");
        var damaged = ClothingSaves.Migrate(SavedOutfit.KeyFor("Bad"), "{\"model\":1885233650,\"clothes\":{\"3\":[-2,0],\"11\":[44,2]},\"props\":{}}", layouts);
        Check(damaged.NewJson == null && damaged.Issues.Single().Reason == ClothingIssue.Damaged, "a damaged piece blocks the rewrite and is listed");
        var review = JArray.Parse(ClothingSaves.ReviewJson(new[] { damaged, result }));
        Check(review.Count == 1 && (string)review[0]["key"] == "vmenu_outfit_Bad" && (string)review[0]["pieces"][0]["slot"] == "c3", "review log lists the save and piece");
        Console.WriteLine("PASS: migration of characters, outfits and saved peds");
    }

    static void OutfitCodes()
    {
        var layouts = Layouts(validFrom: "2026-11-06T00:00:00Z");
        // Saved Outfits > Share: a code is built from an outfit. Here, one saved before references,
        // upgraded with the baseline.
        var legacy = LegacyMale();
        var outfitJson = new SavedOutfit { Model = Male, Clothes = legacy.Components, Props = legacy.Props }.ToJson();
        var outfit = ClothingSaves.Read(ClothingSaves.Kind.Outfit, ClothingSaves.Migrate(SavedOutfit.KeyFor("Work"), outfitJson, layouts).NewJson);
        var code = ClothingSaves.BuildCode(outfit, layouts, out var unshareable);
        Check(code != null && unshareable.Count == 0, "a code is built from an upgraded outfit");
        Check((int)code["v"] == 2 && (uint)code["Model"] == Male && code["Refs"]["clothes"]["2"] == null && code["Clothes"]["2"] == null && code["Clothes"]["11"]["Item"].Value<int>() == 44,
            "v2 code: model, references, no hair, old numbers kept");
        Check(code["Refs"]["props"]["1"]["empty"].Value<bool>() && (string)code["Layout"] == layouts.LegacyFor(Male).Print(), "an empty prop is shared as empty, and the numbers' layout is named");
        Check(code.ToString(Formatting.None).Length < 8192, "fits the server's size limit");
        // Only what server/oufitCodes.lua accepts (its REF_FIELDS and validRefs).
        var allowed = new HashSet<string> { "col", "drw", "tex", "empty", "rev", "pp", "lp" };
        var refs = (JObject)code["Refs"];
        Check(refs.Properties().Select(p => p.Name).OrderBy(n => n).SequenceEqual(new[] { "clothes", "model", "props", "v" })
            && ((JObject)refs["clothes"]).Properties().Concat(((JObject)refs["props"]).Properties()).All(p => ((JObject)p.Value).Properties().All(f => allowed.Contains(f.Name))),
            "only the fields the server accepts");

        // Share What I'm Wearing: what the ped wears now, captured live.
        var rt = MaleShifted();
        SavedOutfit Wearing(Func<ClothingSlot, int, int, ClothingRef> reference) => SavedOutfit.Capture(Male,
            s => s == 11 ? 51 : 0, s => s == 11 ? 2 : 0, p => p == 0 ? 24 : -1, p => p == 0 ? 0 : -1, reference, rt.LayoutPrint);
        var worn = ClothingSaves.BuildCode(ClothingSaves.Read(ClothingSaves.Kind.Outfit, Wearing((slot, g, t) => ClothingResolver.Capture(slot, g, t, layouts, rt)).ToJson()), layouts, out unshareable);
        Check(worn != null && (string)worn["Refs"]["clothes"]["11"]["col"] == Dept && (int)worn["Refs"]["clothes"]["11"]["drw"] == 1 && (string)worn["Layout"] == rt.LayoutPrint,
            "what you're wearing is shared by collection");
        worn = ClothingSaves.BuildCode(ClothingSaves.Read(ClothingSaves.Kind.Outfit,
            Wearing((slot, g, t) => slot.Equals(C(11)) ? null : ClothingResolver.Capture(slot, g, t, layouts, rt)).ToJson()), layouts, out unshareable);
        Check(worn == null && unshareable.Single().Equals(C(11)), "a piece the game can't place blocks the code, and is named");
        var noBaseline = Layouts(baseline: false);
        Check(ClothingSaves.BuildCode(ClothingSaves.Read(ClothingSaves.Kind.Outfit, outfitJson), noBaseline, out unshareable) == null
            && unshareable.Contains(C(11)) && !unshareable.Contains(P(1)), "an old outfit with no baseline isn't shared as a guess");

        // Through Lua and the database: empty sections come back as [].
        var stored = code.DeepClone();
        stored["Refs"]["props"] = new JArray();
        stored["Props"] = new JArray();
        var read = ClothingSaves.ReadCode(stored.ToString(), Male, 0, out var error);
        Check(read != null && read.Refs.Props.Count == 0, "empty lists from Lua read as empty sections");

        Check(ClothingSaves.ReadCode(code.ToString(), Female, 0, out error) == null && error.Contains("male character"), "a male code is refused on a female character");
        read = ClothingSaves.ReadCode(code.ToString(), Male, 0, out _);
        var r = ClothingResolver.Resolve(read, ClothingSaves.CharacterSlots(read), layouts, MaleRepacked());
        Check(Piece(r, C(11)) is { Drawable: 1, Texture: 0 } && r.Notes.Count == 0, "a v2 code follows the pack map on the new pack");

        // Old codes: no model, only numbers. Code ids are unchanged; they are read with the baseline.
        var oldCode = new JObject { ["Clothes"] = new JObject { ["11"] = new JObject { ["Item"] = 44, ["Texture"] = 2 }, ["2"] = new JObject { ["Item"] = 9, ["Texture"] = 0 } }, ["Props"] = new JObject { ["0"] = new JObject { ["Item"] = 17, ["Texture"] = 0 } } };
        var created = DateTimeOffset.Parse("2026-11-08T10:00:00Z").ToUnixTimeSeconds();
        read = ClothingSaves.ReadCode(oldCode.ToString(), Male, created, out _);
        Check(read.Model == Male && read.Refs == null && !read.Components.ContainsKey(2), "an old code is read for the body it is used on, without hair");
        r = ClothingResolver.Resolve(read, ClothingSaves.CharacterSlots(read), layouts, MaleShifted());
        Check(Piece(r, C(11)) is { Collection: Dept, Drawable: 1, Global: 51 } && Piece(r, P(0)).Global == 24 && r.Notes.Count == 0, "an old code made after the baseline was valid resolves cleanly");
        read = ClothingSaves.ReadCode(oldCode.ToString(), Male, created - 30 * 86400, out _);
        r = ClothingResolver.Resolve(read, ClothingSaves.CharacterSlots(read), layouts, MaleShifted());
        Check(r.Notes.Count == 1 && r.Notes[0].Contains("made before"), "an older code is flagged: it may predate the captured layout");

        // Saved Outfits > Load Outfit Code: a code becomes a Saved Outfit.
        read = ClothingSaves.ReadCode(oldCode.ToString(), Male, created, out _);
        var loaded = ClothingSaves.OutfitFromCode(read, layouts, out var pending);
        Check(pending.Count == 0 && loaded.Model == Male && loaded.Layout == null, "an old code becomes an outfit for this body, without claiming a layout for its numbers");
        Check(loaded.Clothes[11].SequenceEqual(new[] { 44, 2 }) && !loaded.Clothes.ContainsKey(2) && loaded.Refs.Get(C(11)) is { Collection: Dept, Drawable: 1 } && loaded.Refs.Get(C(2)) == null,
            "the code's pieces are kept, each with a reference; hair never");
        Check(loaded.Props[1].SequenceEqual(new[] { -1, -1 }) && loaded.Refs.Get(P(1)).Empty && loaded.Refs.Get(P(0)) is { Collection: Dept },
            "props the code doesn't mention are taken off, as wearing a code always did");
        r = ClothingResolver.Resolve(ClothingSaves.Read(ClothingSaves.Kind.Outfit, loaded.ToJson()), ClothingResolver.OutfitSlots, layouts, MaleRepacked());
        Check(r.Issues.Count == 0 && Piece(r, C(11)).Texture == 0 && Piece(r, P(1)).Clear, "the loaded outfit is worn like any Saved Outfit, following the pack map");

        // No baseline yet: an old code keeps its numbers as pending pieces, never guessed.
        loaded = ClothingSaves.OutfitFromCode(ClothingSaves.ReadCode(oldCode.ToString(), Male, created, out _), noBaseline, out pending);
        Check(pending.Count == 2 && loaded.Refs.Get(C(11)).IsPending && loaded.Refs.Get(C(11)).Legacy.SequenceEqual(new[] { 44, 2 }) && loaded.Refs.Get(P(1)).Empty,
            "without a baseline, the code's numbers are kept as they are");
        r = ClothingResolver.Resolve(ClothingSaves.Read(ClothingSaves.Kind.Outfit, loaded.ToJson()), ClothingResolver.OutfitSlots, noBaseline, MaleBefore());
        Check(Piece(r, C(11)) is { Raw: true, Global: 44 } && r.Issues.Count == 0, "and are put on as they always were");
        Check(ClothingSaves.BuildCode(ClothingSaves.Read(ClothingSaves.Kind.Outfit, loaded.ToJson()), noBaseline, out unshareable) == null && unshareable.Count == 2,
            "an outfit with unmatched pieces isn't shared again as a guess");

        // A v2 code's references are copied exactly, so sharing it on gives the same code.
        loaded = ClothingSaves.OutfitFromCode(ClothingSaves.ReadCode(code.ToString(), Male, 0, out _), layouts, out pending);
        Check(pending.Count == 0 && loaded.Refs.Get(C(11)).PackPrint == (string)code["Refs"]["clothes"]["11"]["pp"] && loaded.Layout == (string)code["Layout"],
            "a v2 code's references are copied exactly");
        var again = ClothingSaves.BuildCode(ClothingSaves.Read(ClothingSaves.Kind.Outfit, loaded.ToJson()), layouts, out _);
        Check(JToken.DeepEquals(again["Refs"]["clothes"], code["Refs"]["clothes"]) && JToken.DeepEquals(again["Clothes"], code["Clothes"])
            && ((JObject)code["Refs"]["props"]).Properties().All(p => JToken.DeepEquals(again["Refs"]["props"][p.Name], p.Value)), "a loaded code shares on unchanged");
        Console.WriteLine("PASS: outfit codes (outfit to code, code to outfit, the direct load, old codes, model check)");
    }

    static void SavedOutfits()
    {
        var layouts = Layouts(repack: false);
        var rt = MaleShifted();
        var outfit = SavedOutfit.Capture(Male, s => s == 11 ? 51 : 0, s => s == 11 ? 2 : 0, p => p == 0 ? 24 : -1, p => p == 0 ? 0 : -1,
            (slot, g, t) => ClothingResolver.Capture(slot, g, t, layouts, rt), rt.LayoutPrint);
        Check(outfit.Refs.Get(C(11)) is { Collection: Dept, Drawable: 1, Texture: 2 } && outfit.Refs.Get(P(1)).Empty && outfit.Layout == rt.LayoutPrint, "capture records references and the layout");
        var parsed = SavedOutfit.Parse(outfit.ToJson());
        Check(parsed.Refs.Get(C(11)).Drawable == 1 && parsed.Clothes[11][0] == 51, "stored with both references and numbers");
        var save = ClothingSaves.Read(ClothingSaves.Kind.Outfit, outfit.ToJson());
        var r = ClothingResolver.Resolve(save, ClothingResolver.OutfitSlots, layouts, MaleBefore());
        Check(Piece(r, C(11)).Global == 44 && r.Clean, "a new outfit worn on the old layout goes on by reference");
        Check(!ClothingResolver.OutfitSlots.Contains(C(0)) && !ClothingResolver.OutfitSlots.Contains(C(2)), "outfits never touch head or hair");
        Console.WriteLine("PASS: Saved Outfits");
    }

    static void StorageAndWiring()
    {
        var layouts = Layouts();
        var store = new Dictionary<string, string>
        {
            ["mp_ped_Main"] = CharacterJson(LegacyMale()),
            ["vmenu_outfit_Odd"] = "{\"model\":1885233650,\"clothes\":{\"11\":[200,0]},\"props\":{}}",
            ["vmenu_last_mp_character_snapshot"] = CharacterJson(LegacyMale()),
        };
        var originals = new Dictionary<string, string>(store);
        string Get(string k) => store.TryGetValue(k, out var v) ? v : null;
        void Set(string k, string v) => store[k] = v;
        var logs = new List<string>();
        var results = ClothingSaves.MigrateAll(store.Keys.ToList(), Get, Set, layouts, logs.Add, out var upgraded);
        Check(upgraded == 2 && results.Count == 3, "two saves upgraded, one held for review");
        Check(store["vmenu_clothing_backup_mp_ped_Main"] == originals["mp_ped_Main"] && store["vmenu_clothing_backup_vmenu_last_mp_character_snapshot"] == originals["vmenu_last_mp_character_snapshot"], "originals backed up first");
        Check(store["vmenu_outfit_Odd"] == originals["vmenu_outfit_Odd"] && !store.ContainsKey("vmenu_clothing_backup_vmenu_outfit_Odd"), "the ambiguous outfit is untouched");
        var upgradedJson = store["mp_ped_Main"];
        results = ClothingSaves.MigrateAll(store.Keys.ToList(), Get, Set, layouts, logs.Add, out upgraded);
        Check(upgraded == 0 && store["mp_ped_Main"] == upgradedJson && results.Count(r => r.AlreadyCurrent) == 2, "running again changes nothing");
        // Restoring the original (vmenu_clothing_restore) and running again keeps the first backup.
        store["mp_ped_Main"] = originals["mp_ped_Main"];
        store["vmenu_clothing_backup_mp_ped_Main"] = originals["mp_ped_Main"];
        ClothingSaves.MigrateAll(new[] { "mp_ped_Main" }, Get, Set, layouts, logs.Add, out upgraded);
        Check(upgraded == 1 && store["mp_ped_Main"] == upgradedJson && store["vmenu_clothing_backup_mp_ped_Main"] == originals["mp_ped_Main"], "a restored save upgrades again the same way");
        // A backup that can't be written leaves the save alone.
        store = new Dictionary<string, string> { ["mp_ped_Main"] = originals["mp_ped_Main"] };
        ClothingSaves.MigrateAll(new[] { "mp_ped_Main" }, Get, (k, v) => { if (!k.StartsWith(ClothingSaves.BackupPrefix)) store[k] = v; }, layouts, logs.Add, out upgraded);
        Check(upgraded == 0 && store["mp_ped_Main"] == originals["mp_ped_Main"] && logs.Any(l => l.Contains("couldn't back up")), "no backup, no upgrade");
        // An existing backup (from an earlier run) is never overwritten.
        store = new Dictionary<string, string> { ["mp_ped_Main"] = originals["mp_ped_Main"], ["vmenu_clothing_backup_mp_ped_Main"] = "first original" };
        ClothingSaves.MigrateAll(new[] { "mp_ped_Main" }, Get, Set, layouts, logs.Add, out upgraded);
        Check(upgraded == 1 && store["vmenu_clothing_backup_mp_ped_Main"] == "first original", "the first original is kept");

        // Every feature goes through the resolver.
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(root, Path.Combine(parts))).Replace("\r\n", "\n");
        var mp = Read("vMenu", "menus", "MpPedCustomization.cs");
        var common = Read("vMenu", "CommonFunctions.cs");
        var service = Read("vMenu", "ClothingService.cs");
        Check(mp.Contains("ClothingService.Resolve(clothingSave, ClothingSaves.CharacterSlots(clothingSave), pedHandle)") && !mp.Contains("SetPedComponentVariation(pedHandle, cd.Key"), "MP characters (spawn, default, last appearance, edit, preview) resolve");
        Check(mp.Contains("currentCharacter = await AppySavedDataToPed(currentCharacter"), "the spawned character keeps its resolved references");
        Check(System.Text.RegularExpressions.Regex.Matches(mp, @"RecordEditedSlot\(ClothingSlot\.(Component|PropSlot)\(").Count == 4, "all four editor handlers record their slot");
        Check(mp.Contains("currentCharacter = ClothingService.CompleteFromPed(currentCharacter, Game.PlayerPed.Handle);"), "saving a character completes its references");
        Check(mp.Contains("character = ReplacePedDataClothing(character, keepUnresolved: true);"), "the after-death snapshot keeps unmatched pieces");
        Check(mp.Contains("ClothingService.Resolve(save, ClothingResolver.OutfitSlots, ped)"), "Saved Outfits resolve");
        Check(common.Contains("ClothingService.Resolve(ClothingService.FromPedInfo(pedCustomizationOptions, modelHash), ClothingResolver.SavedPedSlots, ped)"), "Saved Peds resolve");
        Check(common.Contains("data.clothingRefs = ClothingService.CaptureSet(ped, ClothingResolver.SavedPedSlots);"), "Saved Peds capture references");
        Check(common.Contains("pedInfo.clothingRefs?.Set(ClothingSlot.Component(componentId), null);"), "dpclothing's death patch wins over saved references");
        Check(service.Contains("SetPedCollectionComponentVariation(") && service.Contains("SetPedCollectionPropIndex("), "pieces go on with the collection natives");
        var lua = Read("build", "vMenu", "client", "outfitCodes.lua");
        Check(lua.Contains("vMenuApplyOutfitCode") && !lua.Contains("vMenuImportOutfitCode") && !lua.Contains("vMenuBuildOutfitCode") && !lua.Contains("SetPedComponentVariation")
            && mp.Contains("ClothingService.OutfitFromCode((string)reply[\"code\"]") && mp.Contains("ClothingSaves.BuildCode(save, ClothingService.Layouts, out var unshareable)")
            && service.Contains("ClothingSaves.OutfitFromCode(code, Layouts, out var pending)"),
            "codes are loaded as outfits and made from outfits in C#; the direct load goes through C# too");
        foreach (var side in new[] { "client", "server" })
        {
            Check(Read("build", "vMenu", side, "backups.lua").Contains("'vmenu_clothing_backup_'"), side + " backups include upgrade originals");
        }
        Console.WriteLine("PASS: storage upgrade (backup once, retry) and wiring");
    }

    static void ShippedConfig()
    {
        var path = Path.Combine(RepoRoot(), "build", "vMenu", "config", "clothing-layouts.json");
        var layouts = ClothingLayouts.Parse(File.ReadAllText(path), out var error);
        Check(error == null && layouts.CurrentRevision != null && layouts.Revision(layouts.CurrentRevision) != null, "the shipped layouts file parses: " + error);
        foreach (var c in new[] { "mp_m_deptmerge2", "mp_f_deptmerge2", "mp_m_leoaccessories", "mp_f_leoaccessories" })
        {
            Check(layouts.IsPackCollection(c), "PSRP collection declared: " + c);
        }
        var current = layouts.Revision(layouts.CurrentRevision);
        foreach (var model in new[] { Male, Female })
        {
            var counts = current.Counts[model];
            var status = layouts.MatchPack(model, (col, slot) => counts.TryGetValue(PackRevision.CountKey(col, slot), out var v) ? v : Array.Empty<int>());
            Check(status.Revision == layouts.CurrentRevision && status.Mode == "exact", "the declared pack matches itself");
        }
        if (layouts.Legacy.Count == 0)
        {
            Check(layouts.LegacyRevision == null, "until a capture is merged, no legacy pack is claimed (old saves behave as before)");
        }
        else
        {
            Check(layouts.Legacy.Count == 2 && layouts.Revision(layouts.LegacyRevision) != null, "a merged capture covers both models and names a declared pack");
        }
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "build", "vMenu", "fxmanifest.lua"));
        Check(src.Contains("'config/clothing-layouts.json'"), "the layouts file is in the manifest");
        Console.WriteLine("PASS: shipped config");
    }
}
