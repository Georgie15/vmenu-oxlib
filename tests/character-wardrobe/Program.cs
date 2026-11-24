// Run: dotnet run --project tests/character-wardrobe
// Drives the production CharacterWardrobe (a saved character's own outfits and hair & makeup
// styles in their own KVPs), HairMakeupStyle and the Department Outfits client side (names,
// publishable outfits, reading the server's replies), then checks the menu, backup and server
// wiring in the source.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using vMenuClient;

static class Program
{
    const uint Male = 0x705E61F2;
    const uint Female = 0x9C9EFFD8;

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    // A KVP store with prefix search, as StartFindKvp/FindKvp give it.
    sealed class Store
    {
        public readonly Dictionary<string, string> Kvps = new(StringComparer.Ordinal);
        public bool FailWrites;
        public IEnumerable<string> Find(string prefix) => Kvps.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        public string Get(string key) => Kvps.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) { if (!FailWrites) Kvps[key] = value; }
        public void Delete(string key) => Kvps.Remove(key);
        public int Transfer(string from, string to, bool keep) => CharacterWardrobe.Transfer(from, to, keep, Find, Get, Set, Delete);
        public int DeleteAll(string character) => CharacterWardrobe.DeleteAll(character, Find, Delete);
    }

    static int Main()
    {
        Keys();
        Lifecycle();
        Styles();
        Outfits();
        Departments();
        Wiring();
        return 0;
    }

    static void Keys()
    {
        var O = CharacterWardrobe.Kind.Outfit;
        var S = CharacterWardrobe.Kind.Style;
        Check(CharacterWardrobe.KeyFor(O, "mp_ped_Bob", "Work") == "vmenu_character_outfit_Bob|Work", "outfit key: " + CharacterWardrobe.KeyFor(O, "mp_ped_Bob", "Work"));
        Check(CharacterWardrobe.KeyFor(S, "Bob", "Night out") == "vmenu_character_style_Bob|Night out", "style key, from the bare name too");
        Check(CharacterWardrobe.CharacterName("mp_ped_mp_ped_Old") == "mp_ped_Old", "only the first mp_ped_ is the prefix, as GetAllMpCharacterNames reads it");
        foreach (var prefix in new[] { CharacterWardrobe.OutfitPrefix, CharacterWardrobe.StylePrefix })
        {
            Check(prefix.StartsWith("vmenu"), "survives MainMenu's one-off KVP cleanup: " + prefix);
            Check(!prefix.StartsWith(SavedOutfit.KeyPrefix) && !prefix.StartsWith("mp_ped_") && !prefix.StartsWith("ped_"), "never read as a Saved Outfit, a character or a Saved Ped: " + prefix);
            Check(ClothingSaves.KindOf(prefix + "Bob|Work") == null, "not picked up by the clothing save upgrade (always written with references)");
        }

        // A character called "A|B" and one called "A": neither sees the other's entries.
        var keys = new[] { "vmenu_character_outfit_A|Work", "vmenu_character_outfit_A|B|Gala", "vmenu_character_outfit_A|", "vmenu_character_outfit_Ab|Gym", "vmenu_character_outfit_A|Beach" };
        Check(string.Join(",", CharacterWardrobe.Names(O, "mp_ped_A", keys)) == "Beach,Work", "A's outfits: " + string.Join(",", CharacterWardrobe.Names(O, "mp_ped_A", keys)));
        Check(string.Join(",", CharacterWardrobe.Names(O, "mp_ped_A|B", keys)) == "Gala", "A|B's outfits");
        Check(CharacterWardrobe.Names(S, "mp_ped_A", keys).Count == 0 && CharacterWardrobe.Names(O, "mp_ped_A", null).Count == 0, "styles are separate; nothing found is empty");

        Check(CharacterWardrobe.CleanName("  Work  ", out _) == "Work", "names are trimmed");
        foreach (var bad in new[] { null, "", "   ", new string('a', 31), "A|B", "~r~Red", "Line\nbreak" })
        {
            Check(CharacterWardrobe.CleanName(bad, out var error) == null && !string.IsNullOrEmpty(error), "refused name: " + bad);
        }
        Check(CharacterWardrobe.CleanName(new string('a', 30), out _) != null && CharacterWardrobe.CleanName("Night out (2) - É", out _) != null, "30 characters and any other text are fine");
        Console.WriteLine("PASS: keys per character and entry, names never cross characters, name checks");
    }

    static void Lifecycle()
    {
        var store = new Store();
        store.Kvps["mp_ped_Bob"] = "{\"SaveName\":\"mp_ped_Bob\"}";
        store.Kvps["vmenu_character_outfit_Bob|Work"] = "work";
        store.Kvps["vmenu_character_outfit_Bob|Gym"] = "gym";
        store.Kvps["vmenu_character_style_Bob|Bun"] = "bun";
        store.Kvps["vmenu_character_outfit_Bobby|Work"] = "bobby work";
        store.Kvps["vmenu_character_outfit_Bob|X|Y"] = "belongs to Bob|X";
        store.Kvps["vmenu_outfit_Work"] = "shared outfit";

        // Rename Bob to Robert: everything of Bob's moves, nothing else does.
        Check(store.Transfer("mp_ped_Bob", "mp_ped_Robert", keep: false) == 3, "three entries moved");
        Check(store.Get("vmenu_character_outfit_Robert|Work") == "work" && store.Get("vmenu_character_outfit_Robert|Gym") == "gym" && store.Get("vmenu_character_style_Robert|Bun") == "bun", "outfits and styles carried to the new name");
        Check(store.Find("vmenu_character_outfit_Bob|").SequenceEqual(new[] { "vmenu_character_outfit_Bob|X|Y" }) && store.Find("vmenu_character_style_Bob|").Count() == 0, "the old name keeps nothing of Bob's");
        Check(store.Get("vmenu_character_outfit_Bobby|Work") == "bobby work" && store.Get("vmenu_outfit_Work") == "shared outfit" && store.Get("mp_ped_Bob") != null, "other characters, Saved Outfits and the character JSON are untouched");

        // Clone Robert as Rob: copies, the source keeps its own.
        Check(store.Transfer("mp_ped_Robert", "mp_ped_Rob", keep: true) == 3, "clone copies all three");
        Check(store.Get("vmenu_character_outfit_Rob|Work") == "work" && store.Get("vmenu_character_outfit_Robert|Work") == "work", "both have them after a clone");

        // Never overwrites what the new name already has; that entry stays with its source.
        store.Kvps["vmenu_character_style_Bert|Bun"] = "bert's own bun";
        Check(store.Transfer("mp_ped_Robert", "mp_ped_Bert", keep: false) == 2, "two moved, one kept back");
        Check(store.Get("vmenu_character_style_Bert|Bun") == "bert's own bun" && store.Get("vmenu_character_style_Robert|Bun") == "bun", "an existing entry is never overwritten, and the source stays");

        // A write that doesn't stick never deletes the source.
        store.FailWrites = true;
        Check(store.Transfer("mp_ped_Rob", "mp_ped_Robin", keep: false) == 0 && store.Find("vmenu_character_outfit_Rob|").Count() == 2, "failed writes leave the source in place");
        store.FailWrites = false;
        Check(store.Transfer("mp_ped_Rob", "Rob", keep: false) == 0, "renaming to the same character does nothing");

        // Deleting a character removes its entries only.
        Check(store.DeleteAll("mp_ped_Rob") == 3, "Rob's three entries deleted");
        Check(!store.Find("vmenu_character_").Any(k => k.StartsWith("vmenu_character_outfit_Rob|") || k.StartsWith("vmenu_character_style_Rob|")), "nothing of Rob's left");
        Check(store.Get("vmenu_character_outfit_Robert|Gym") == null && store.Get("vmenu_character_outfit_Bert|Gym") == "gym" && store.Get("vmenu_character_outfit_Bobby|Work") == "bobby work" && store.Get("vmenu_character_outfit_Bob|X|Y") != null,
            "other characters keep theirs");
        Console.WriteLine("PASS: rename moves, clone copies, delete removes; never overwrites, never crosses characters");
    }

    static void Styles()
    {
        var layers = new Dictionary<int, StyleLayer>
        {
            [1] = new StyleLayer { Style = 255, Opacity = 0.7f, Color = 3 },          // no beard
            [2] = new StyleLayer { Style = 12, Opacity = 0.9f, Color = 61 },
            [4] = new StyleLayer { Style = 7, Opacity = 1.4f, Color = 99 },           // out of range
            [5] = null,                                                            // couldn't be read
            [8] = new StyleLayer { Style = 3, Opacity = 0.333333f, Color = 5 },
        };
        var style = HairMakeupStyle.Capture(Male, 21, 4, 70, new[] { "mpbusiness_overlays", "FM_Bus_M_Hair_001_a" }, id => layers.TryGetValue(id, out var l) ? l : new StyleLayer { Style = 1, Opacity = 1, Color = 1 });
        Check(style.Model == Male && style.Hair == 21 && style.HairColor == 4 && style.HairHighlight == 63, "hair, colour, and the highlight held to the game's range");
        Check(style.HairOverlay.SequenceEqual(new[] { "mpbusiness_overlays", "FM_Bus_M_Hair_001_a" }), "the hair fade is kept");
        Check(style.Overlays.Keys.OrderBy(k => k).SequenceEqual(new[] { 1, 2, 4, 8 }), "beard, eyebrows, makeup, lipstick; an unreadable layer is left out");
        Check(style.Overlays[1].Style == 0 && style.Overlays[1].Opacity == 0f, "no overlay (255) is style 0 at opacity 0, as the creator keeps it");
        Check(style.Overlays[4].Opacity == 1f && style.Overlays[4].Color == 63 && Math.Abs(style.Overlays[8].Opacity - 0.33f) < 1e-6, "opacity 0 to 1, colours in range");
        Check(HairMakeupStyle.OverlayIds.SequenceEqual(new[] { 1, 2, 4, 5, 8 }), "beard, eyebrows, makeup, blush, lipstick");

        var stored = HairMakeupStyle.Parse(style.ToJson());
        Check(stored != null && stored.ToJson() == style.ToJson(), "round trips through storage");
        Check(HairMakeupStyle.Capture(Female, 3, 0, 0, new[] { "", "x" }, _ => null).HairOverlay == null, "a damaged fade is dropped");
        foreach (var bad in new[] { null, "", "{", "[]", "{\"v\":1,\"model\":0}", "{\"v\":2,\"model\":5}", "{\"v\":1,\"model\":5,\"hair\":-1}" })
        {
            Check(HairMakeupStyle.Parse(bad) == null, "unreadable style ignored: " + bad);
        }
        var cleaned = HairMakeupStyle.Parse("{\"v\":1,\"model\":1885233650,\"hair\":4,\"hairColor\":-3,\"hairHighlight\":80,\"hairOverlay\":[\"a\"],\"overlays\":{\"1\":{\"style\":2,\"opacity\":5,\"color\":-1},\"3\":{\"style\":1,\"opacity\":1,\"color\":1},\"8\":null}}");
        Check(cleaned != null && cleaned.HairColor == 0 && cleaned.HairHighlight == 63 && cleaned.HairOverlay == null, "stored values are held to range");
        Check(cleaned.Overlays.Count == 1 && cleaned.Overlays[1].Opacity == 1f && cleaned.Overlays[1].Color == 0, "only style layers are kept (ageing and nulls dropped)");
        Check(HairMakeupStyle.Parse("{\"v\":1,\"model\":1885233650,\"hair\":4}").Overlays.Count == 0, "missing layers read as none");

        // Colour types match how the character creator and spawning set each layer.
        var mp = Read("vMenu", "menus", "MpPedCustomization.cs");
        foreach (var id in HairMakeupStyle.OverlayIds)
        {
            Check(mp.Contains($"SetPedHeadOverlayColor(pedHandle, {id}, {HairMakeupStyle.ColorType(id)}, "), $"colour type of overlay {id} matches AppySavedDataToPed");
        }
        Console.WriteLine("PASS: hair & makeup styles capture, normalize, store and read back");
    }

    static void Outfits()
    {
        // A character's outfit is a Saved Outfit with references, and is worn like one.
        var game = new FakeRuntime(Male);
        foreach (var slot in ClothingSlot.AllLayoutSlots) game.Fill(slot.Key, "", 40, 4).Fill(slot.Key, "mp_m_deptmerge2", 30, 6);
        var layouts = ClothingLayouts.Empty();
        var outfit = SavedOutfit.Capture(Male, slot => slot == 11 ? 45 : slot, slot => slot == 11 ? 5 : 0, slot => slot == 0 ? 2 : -1, slot => 1,
            (slot, global, texture) => ClothingResolver.Capture(slot, global, texture, layouts, game), game.LayoutPrint);
        Check(DepartmentOutfits.MissingRefs(outfit).Count == 0, "a captured outfit has a reference for every slot");
        var worn = ClothingResolver.Resolve(ClothingSaves.Read(ClothingSaves.Kind.Outfit, outfit.ToJson()), ClothingResolver.OutfitSlots, layouts, game);
        Check(worn.Issues.Count == 0 && worn.Pieces.Single(p => !p.Slot.Prop && p.Slot.Index == 11) is { Collection: "mp_m_deptmerge2", Drawable: 5, Texture: 5 }, "worn by collection, like Saved Outfits");

        // What the server's validator accepts (build/vMenu/server/deptOutfits.lua).
        var json = JObject.Parse(outfit.ToJson());
        var lua = Read("build", "vMenu", "server", "deptOutfits.lua");
        HashSet<string> Fields(string name) => new(Regex.Match(lua, "local " + name + @" = \{([^}]*)\}").Groups[1].Value.Split(',').Select(f => f.Split('=')[0].Trim()).Where(f => f != ""));
        Check(json.Properties().All(p => Fields("PAYLOAD_FIELDS").Contains(p.Name)), "outfit fields are the ones the server accepts");
        Check(((JObject)json["refs"]).Properties().All(p => Fields("REFS_FIELDS").Contains(p.Name)), "reference set fields are the ones the server accepts");
        Check(json["refs"]["clothes"].Concat(json["refs"]["props"]).Cast<JProperty>().All(p => ((JObject)p.Value).Properties().All(f => Fields("REF_FIELDS").Contains(f.Name))), "reference fields are the ones the server accepts");
        Check(outfit.ToJson().Length < 8192, "a full outfit fits the 8 KB payload limit (" + outfit.ToJson().Length + " bytes)");
        // tests/dept-outfits checks the server accepts this exact JSON (and hands it back unchanged).
        var fixture = Regex.Match(Read("tests", "dept-outfits", "test_dept_outfits.lua"), @"local CSHARP_PAYLOAD = \[\[(.*?)\]\]", RegexOptions.Singleline).Groups[1].Value;
        Check(fixture == outfit.ToJson(), "the server test's payload is what SavedOutfit writes today");

        var missing = SavedOutfit.Parse(outfit.ToJson());
        missing.Refs.Set(ClothingSlot.Component(4), null);
        missing.Refs.Set(ClothingSlot.PropSlot(6), ClothingRef.Pending(3, 1, "unmatched"));
        Check(DepartmentOutfits.MissingRefs(missing).Select(s => s.Key).SequenceEqual(new[] { "c4", "p6" }), "unplaced and pending pieces are named: " + string.Join(",", DepartmentOutfits.MissingRefs(missing)));
        Check(DepartmentOutfits.MissingRefs(new SavedOutfit { Model = Male }).Count == ClothingResolver.OutfitSlots.Length, "an outfit without references can't be published");
        Console.WriteLine("PASS: characters' and departments' outfits are Saved Outfits with references, within the server's limits");
    }

    static void Departments()
    {
        Check(DepartmentOutfits.CleanName("  Class   A ", out _) == "Class A", "spaces are collapsed like the server does");
        Check(DepartmentOutfits.CleanName("SWAT (Night) - K9 & Co. #2/3 + O'Neil, 1", out _) != null, "the server's allowed punctuation");
        foreach (var bad in new[] { null, "", " ", new string('a', 41), "~r~Red", "semi;colon", "Ünïcode", "a<b>" })
        {
            Check(DepartmentOutfits.CleanName(bad, out var error) == null && error != null, "refused department outfit name: " + bad);
        }

        var reply = DepartmentOutfitReply.Parse(@"{""ok"":true,""dutyAvailable"":true,
            ""duty"":{""id"":""bcso"",""label"":""Blaine County Sheriff's Office""},
            ""manage"":[{""id"":""lspd"",""label"":""Los Santos Police Department""},{""id"":""bcso"",""label"":""BCSO""},{""id"":""lspd""},{""label"":""no id""}],
            ""outfits"":[{""id"":4,""org"":""bcso"",""name"":""Patrol"",""updated"":""2026-11-17"",""by"":""Sgt X"",""manage"":false},
                         {""id"":2.0,""org"":""lspd"",""name"":""class b"",""manage"":true},
                         {""id"":1,""org"":""lspd"",""name"":""Class A"",""manage"":true},
                         {""org"":""lspd"",""name"":""no id""},{""id"":9,""org"":""lspd""}]}");
        Check(reply.Ok && reply.Message == null, "a good reply");
        var listing = DepartmentOutfitListing.From(reply);
        Check(listing.Duty.Id == "bcso" && listing.DutyAvailable, "duty org");
        Check(listing.Manage.Select(m => m.Id).SequenceEqual(new[] { "lspd", "bcso" }), "managed orgs, each once, damaged ones skipped");
        Check(listing.Sections().Select(o => o.Id).SequenceEqual(new[] { "bcso", "lspd" }), "duty org first, then the others you manage");
        Check(listing.OutfitsOf("lspd").Select(o => o.Name).SequenceEqual(new[] { "Class A", "class b" }) && listing.OutfitsOf("lspd")[1].Id == 2, "outfits per org, sorted, damaged entries skipped");
        var patrol = listing.OutfitsOf("bcso").Single();
        Check(patrol.Id == 4 && patrol.By == "Sgt X" && patrol.Updated == "2026-11-17" && !patrol.CanManage && listing.OutfitsOf("lspd").All(o => o.CanManage), "who, when and whether you manage it");
        Check(listing.LabelOf("lspd") == "Los Santos Police Department" && listing.LabelOf("bcso") == "Blaine County Sheriff's Office" && listing.LabelOf("fire") == "fire", "labels");

        // Lua's json writes empty lists as {} (or []), and leaves out a nil duty.
        listing = DepartmentOutfitListing.From(DepartmentOutfitReply.Parse("{\"ok\":true,\"dutyAvailable\":false,\"manage\":{},\"outfits\":{}}"));
        Check(listing.Duty == null && !listing.DutyAvailable && listing.Manage.Count == 0 && listing.Outfits.Count == 0 && listing.Sections().Count == 0, "an empty reply");
        listing = DepartmentOutfitListing.From(DepartmentOutfitReply.Parse("{\"ok\":true,\"duty\":{\"id\":\"ghost\"},\"manage\":[],\"outfits\":[]}"));
        Check(listing.Duty.Label == "ghost" && listing.Sections().Single().Id == "ghost", "a duty org without a label is named by its id");

        foreach (var bad in new[] { null, "", "nope", "[1]", "{\"ok\":" })
        {
            var r = DepartmentOutfitReply.Parse(bad);
            Check(!r.Ok && r.Message != null, "an unreadable reply is a failure with a message: " + bad);
        }
        var refused = DepartmentOutfitReply.Parse("{\"ok\":false,\"message\":\"Go on duty with LSPD to wear its outfits.\"}");
        Check(!refused.Ok && refused.Message.StartsWith("Go on duty") && refused.Text("payload") == null, "a refusal keeps the server's message");
        Check(DepartmentOutfitReply.Parse("{\"ok\":\"yes\"}").Ok == false && DepartmentOutfitReply.Parse("{\"ok\":true,\"payload\":5}").Text("payload") == null, "only a real true is ok, only text is a payload");
        Console.WriteLine("PASS: department outfit names and server replies");
    }

    static string root;
    static string Read(params string[] parts)
    {
        if (root == null)
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
            Check(dir != null, "repo root found");
            root = dir;
        }
        return File.ReadAllText(Path.Combine(root, Path.Combine(parts))).Replace("\r\n", "\n");
    }

    static string Member(string source, string pattern)
    {
        var match = Regex.Match(source, pattern, RegexOptions.Singleline | RegexOptions.Multiline);
        Check(match.Success, "found in source: " + pattern);
        return match.Value;
    }

    static void Wiring()
    {
        var mp = Read("vMenu", "menus", "MpPedCustomization.cs");

        // One capture and one wear path for every kind of outfit.
        var wear = Member(mp, @"^        private bool WearOutfit\(ClothingSave save, string what\).*?^        }$");
        Check(wear.Contains("save.Model != (uint)GetEntityModel(ped)") && wear.Contains("ClothingService.Resolve(save, ClothingResolver.OutfitSlots, ped)")
            && wear.Contains("ClothingService.Apply(resolution, ped, 0);") && wear.Contains("currentCharacter = ReplacePedDataClothing(currentCharacter, keepUnresolved: true);")
            && wear.Contains("ClothingService.Report(what, resolution, true);"), "WearOutfit: body check, resolver, apply, character record, report");
        var capture = Member(mp, @"^        private static SavedOutfit CaptureCurrentOutfit\(\).*?^        }$");
        Check(capture.Contains("(slot, global, texture) => ClothingResolver.Capture(slot, global, texture, ClothingService.Layouts, runtime)") && capture.Contains("runtime.LayoutPrint"), "CaptureCurrentOutfit records references and the layout");
        Check(Regex.Matches(mp, @"SavedOutfit\.Capture\(").Count == 1, "outfits are captured in one place");

        // Department Outfits.
        var gate = Member(mp, @"^            if \(DepartmentOutfitsBridge\.Enabled\)\n            \{\n.*?^            }$");
        Check(gate.Contains("CreateDepartmentOutfitsMenu(deptOutfitsMenu);") && Regex.Matches(mp, @"CreateDepartmentOutfitsMenu\(").Count == 2, "the menu exists only with vmenu_dept_outfits on");
        var dept = Member(mp, @"^        private void CreateDepartmentOutfitsMenu\(Menu deptMenu\).*?^        }$");
        Check(dept.Contains("deptMenu.OnMenuOpen += async (m) => await Reload();"), "the list is refreshed each time it opens");
        Check(dept.Contains("WearOutfit(save, $\"Department outfit {outfit.Name}\")") && dept.Contains("ClothingSaves.Read(ClothingSaves.Kind.Outfit, reply.Text(\"payload\"))"), "presets are worn through the shared path");
        Check(dept.Contains("[\"model\"] = (long)(uint)GetEntityModel(Game.PlayerPed.Handle)") && dept.Contains("if (!IsCurrentPedMpFreemode())"), "listed for the current MP body");
        Check(dept.Contains("GetUserConfirmation(\"Replace Department Outfit\"") && dept.Contains("GetUserConfirmation(\"Delete Department Outfit\""), "replace and delete ask first");
        Check(dept.Contains("DepartmentOutfits.MissingRefs(outfit)") && dept.Contains("outfit = CaptureCurrentOutfit();"), "only fully referenced outfits are published");
        var lua = Read("build", "vMenu", "server", "deptOutfits.lua");
        var serverActions = Regex.Matches(lua, @"^function actions\.(\w+)\(", RegexOptions.Multiline).Select(m => m.Groups[1].Value).OrderBy(a => a).ToList();
        var clientActions = Regex.Matches(dept, @"DepartmentOutfitsBridge\.Request\(""(\w+)""").Select(m => m.Groups[1].Value).Distinct().OrderBy(a => a).ToList();
        Check(serverActions.SequenceEqual(clientActions) && serverActions.Count == 6, "client requests match the server's actions: " + string.Join(",", clientActions) + " / " + string.Join(",", serverActions));
        var bridge = Read("vMenu", "DepartmentOutfitsBridge.cs");
        Check(bridge.Contains("TriggerServerEvent(DepartmentOutfits.RequestEvent, request, action,") && bridge.Contains("EventHandlers[DepartmentOutfits.ReplyEvent]"), "the bridge uses the shared event names");
        Check(bridge.Contains("await Delay(PollMs);") && bridge.Contains("private const int PollMs = 50;") && !bridge.Contains("Delay(0)"), "the bridge checks for the answer every 50 ms, not every frame");
        var listLimit = Regex.Match(lua, @"local COOLDOWN_MS = \{ list = (\d+), wear = (\d+),");
        var spacing = Regex.Match(bridge, @"SpacingMs = new\(\) \{ \[""list""\] = (\d+), \[""wear""\] = (\d+) \};");
        Check(listLimit.Success && spacing.Success && int.Parse(spacing.Groups[1].Value) > int.Parse(listLimit.Groups[1].Value)
            && int.Parse(spacing.Groups[2].Value) > int.Parse(listLimit.Groups[2].Value), "list and wear requests are spaced past the server's limits, so reopening the menu waits instead of failing");
        Check(dept.Contains("Notify.Error(\"Department outfits are only for MP characters.\");") && dept.Contains("Notify.Error(\"This outfit couldn't be read."),
            "an outfit that can't be worn says why");
        Check(lua.Contains("VMenuGuard.netEvent('vMenu:DeptOutfits:Request'") && lua.Contains("'vMenu:DeptOutfits:Reply'") && lua.Contains("GetConvarBool('vmenu_dept_outfits', false)"), "the server uses the same events and convar");
        Check(!dept.Contains("IsAllowed(") && !Regex.IsMatch(dept, @"\[""org""\] = listing\.Duty"), "the client never decides access or claims its duty org");

        // A character's own styles (Saved Characters > a character), and its own outfits (in Saved
        // Outfits, next to the shared ones: tests/saved-outfits checks that menu).
        Check(mp.Contains("CreateCharacterStylesMenu(characterStylesMenu);") && !mp.Contains("characterOutfitsMenu"), "the manage character menu has the styles page; outfits are in Saved Outfits");
        var wardrobe = Member(mp, @"^        private void CreateCharacterStylesMenu\(Menu stylesMenu\).*?^        }$");
        Check(wardrobe.Contains("stylesMenu.OnMenuOpen += (m) => Rebuild();") && wardrobe.Contains("CharacterWardrobe.KeyFor(kind, CharacterKey(), name)") && wardrobe.Contains("const CharacterWardrobe.Kind kind = CharacterWardrobe.Kind.Style;"),
            "rebuilt for the selected character each time it opens");
        var outfits = Member(mp, @"^        private void CreateSavedOutfitsMenu\(Menu outfitsMenu\).*?^        }$");
        Check(outfits.Contains("const CharacterWardrobe.Kind kind = CharacterWardrobe.Kind.Outfit;") && outfits.Contains("ClothingSaves.Read(ClothingSaves.Kind.Outfit, json)")
            && outfits.Contains("WearOutfit(save, $\"Outfit {name}\")") && outfits.Contains("StillPlaying(entry.CharacterKey)"), "a character's outfits are worn through the shared path, while you play that character");
        Check(wardrobe.Contains("ApplyHairMakeupStyle(style, out var problem)") && wardrobe.Contains("CaptureHairMakeupStyle(character)") && wardrobe.Contains("await EnsureWearingCharacter(characterKey)"), "styles are captured and applied, as that character");
        Check(wardrobe.Contains("GetUserConfirmation(\"Delete Style\"") && wardrobe.Contains("GetUserConfirmation(\"Update Style\""), "delete and update ask first");
        Check(Regex.Matches(wardrobe, @"SetResourceKvp\(").Count == 3 && !wardrobe.Contains("SetResourceKvp(characterKey") && !wardrobe.Contains("SetResourceKvp(\"mp_ped_")
            && Regex.Matches(outfits, @"SetResourceKvp\(").Count == 1 && outfits.Contains("SetResourceKvp(entry.Key, json);"), "entries are written to their own keys, never into the character's");
        var rename = Member(mp, @"^        private void RenameMpCharacterReferences\(string oldPedKey, string newPedKey\).*?^        }$");
        Check(rename.Contains("CharacterWardrobe.Transfer(oldPedKey, newPedKey, keepSource: false,"), "renaming a character carries its outfits and styles");
        var clear = Member(mp, @"^        private void ClearMpCharacterReferences\(string pedKey\).*?^        }$");
        Check(clear.Contains("CharacterWardrobe.DeleteAll(pedKey, KvpKeys, DeleteResourceKvp);"), "deleting a character removes them");
        Check(Regex.Matches(mp, @"DeleteResourceKvp\(pedKey\);\s*ClearMpCharacterReferences\(pedKey\);").Count == 2, "both ways of deleting a character clear its references");
        Check(mp.Contains("CharacterWardrobe.Transfer(\"mp_ped_\" + selectedSavedCharacterManageName, \"mp_ped_\" + name, keepSource: true,"), "a clone gets copies");
        var apply = Member(mp, @"^        private bool ApplyHairMakeupStyle\(HairMakeupStyle style, out string problem\).*?^        }$");
        Check(apply.Contains("style.Hair < GetNumberOfPedDrawableVariations(ped, 2)") && apply.Contains("ClearPedFacialDecorations(ped);") && apply.Contains("RefreshCurrentCharacterSnapshot();"), "styles: haircut checked, fade replaced, worn character kept");

        foreach (var side in new[] { "client", "server" })
        {
            var backups = Read("build", "vMenu", side, "backups.lua");
            Check(backups.Contains("'vmenu_character_outfit_'") && backups.Contains("'vmenu_character_style_'"), side + " backups include characters' outfits and styles");
        }
        Console.WriteLine("PASS: shared outfit path, Department Outfits, character outfits and styles, backups");
    }
}
