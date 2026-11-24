// Run: dotnet run --project tests/saved-outfits
// Drives the production SavedOutfit: what is captured (clothing only, never face or hair), how it
// dresses the ped, damaged storage, and the outfits page wiring (in Saved Characters): one list with
// a character's own outfits and the shared ones, where outfit codes are loaded and made.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vMenuClient;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static int Main()
    {
        // A worn outfit: component n has drawable n*10 and texture n; a hat (prop 0) and a watch (prop 6).
        var outfit = SavedOutfit.Capture(0x705E61F2, slot => slot * 10, slot => slot,
            slot => slot == 0 ? 12 : slot == 6 ? 3 : -1, slot => slot == 0 ? 2 : 1);
        Check(outfit.Clothes.Keys.OrderBy(k => k).SequenceEqual(new[] { 1, 3, 4, 5, 6, 7, 8, 9, 10, 11 }), "clothing slots only: no face (0) or hair (2)");
        Check(outfit.Clothes[11].SequenceEqual(new[] { 110, 11 }), "drawable and texture are kept");
        Check(outfit.Props[0].SequenceEqual(new[] { 12, 2 }) && outfit.Props[1].SequenceEqual(new[] { -1, -1 }), "props, with -1 for nothing worn");

        var stored = SavedOutfit.Parse(outfit.ToJson());
        Check(stored != null && stored.Model == 0x705E61F2 && stored.Clothes[4][0] == 40 && stored.Props[6][0] == 3, "round trips through storage");

        // Wearing goes through the shared resolver (tests/clothing-refs covers it in depth). On a
        // game where every slot is the base collection, an outfit puts on exactly its numbers.
        var game = new FakeRuntime(0x705E61F2);
        foreach (var slot in ClothingSlot.AllLayoutSlots) game.Fill(slot.Key, "", 120, 12);
        var layouts = ClothingLayouts.Empty();
        var worn = ClothingResolver.Resolve(ClothingSaves.Read(ClothingSaves.Kind.Outfit, stored.ToJson()), ClothingResolver.OutfitSlots, layouts, game);
        var components = worn.Pieces.Where(p => !p.Slot.Prop).ToDictionary(p => p.Slot.Index, p => (p.Global, p.Texture));
        var props = worn.Pieces.Where(p => p.Slot.Prop && !p.Clear).ToDictionary(p => p.Slot.Index, p => (p.Global, p.Texture));
        var cleared = worn.Pieces.Where(p => p.Slot.Prop && p.Clear).Select(p => p.Slot.Index).ToList();
        Check(components.Count == 10 && !components.ContainsKey(0) && !components.ContainsKey(2) && components[11] == (110, 11), "wearing sets the clothing slots only");
        Check(props.Count == 2 && props[0] == (12, 2) && props[6] == (3, 1), "worn props are put on");
        Check(cleared.OrderBy(s => s).SequenceEqual(new[] { 1, 2, 3, 4, 5, 7 }), "props the outfit doesn't have are taken off");

        var damaged = ClothingSaves.Read(ClothingSaves.Kind.Outfit, "{\"model\":1885233650,\"clothes\":{\"0\":[5,5],\"2\":[9,9],\"3\":[4],\"4\":null,\"5\":[-2,0],\"6\":[7,1]},\"props\":{\"0\":[1,-5],\"1\":[3]}}");
        worn = ClothingResolver.Resolve(damaged, ClothingResolver.OutfitSlots, layouts, game);
        Check(worn.Pieces.Count(p => !p.Slot.Prop) == 1 && worn.Pieces.Single(p => !p.Slot.Prop).Slot.Index == 6, "damaged, negative, face and hair entries are skipped");
        Check(worn.Pieces.Count(p => p.Slot.Prop) == 1 && worn.Pieces.Single(p => p.Slot.Prop) is { Global: 1, Texture: 0 }, "a bad prop texture becomes 0; a damaged prop is left alone");
        Check(worn.Issues.Single().Slot.Index == 5, "the damaged piece is reported");
        foreach (var bad in new[] { null, "", "{", "{\"clothes\":{}}", "[]", "{\"model\":0}" })
        {
            Check(SavedOutfit.Parse(bad) == null, "unreadable or model-less storage is ignored: " + bad);
        }
        Check(SavedOutfit.Parse("{\"model\":5,\"clothes\":null,\"props\":null}") is { Clothes: not null, Props: not null }, "null sections read as empty");
        Check(SavedOutfit.KeyFor("Work") == "vmenu_outfit_Work" && SavedOutfit.KeyPrefix.StartsWith("vmenu"), "stored per name; survives MainMenu's KVP cleanup");
        Console.WriteLine("PASS: capture, wear, damaged storage, key");

        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(dir, Path.Combine(parts))).Replace("\r\n", "\n");
        var mp = Read("vMenu", "menus", "MpPedCustomization.cs");
        var menu = Regex.Match(mp, @"private void CreateOutfitsMenu\(Menu outfitsMenu, Func<string> characterFor\).*?\n        \}\n", RegexOptions.Singleline).Value;
        // Saving and wearing go through the capture and wear shared with characters' own outfits and
        // department outfits (tests/character-wardrobe checks those too).
        var wear = Regex.Match(mp, @"private bool WearOutfit\(ClothingSave save, string what\).*?\n        \}\n", RegexOptions.Singleline).Value;
        var capture = Regex.Match(mp, @"private static SavedOutfit CaptureCurrentOutfit\(\).*?\n        \}\n", RegexOptions.Singleline).Value;
        // Outfits are part of Saved Characters: its top row is the character you're playing, and
        // each character's page has Outfits. No Saved Outfits row of its own.
        Check(mp.Contains("CreateOutfitsMenu(playingOutfitsMenu, PlayingCharacterKey);") && mp.Contains("MenuController.BindMenuItem(savedCharactersMenu, playingOutfitsMenu, playingOutfitsRow);")
            && mp.Contains("CreateOutfitsMenu(characterOutfitsMenu, () => \"mp_ped_\" + selectedSavedCharacterManageName);") && !mp.Contains("new MenuItem(\"Saved Outfits\""),
            "outfits are in Saved Characters: the played character's at the top, and each character's on its page");
        var update = Regex.Match(mp, @"private void UpdateSavedPedsMenu\(\).*?\n        \}\n", RegexOptions.Singleline).Value;
        Check(update.IndexOf("savedCharactersMenu.AddMenuItem(playingOutfitsRow);") >= 0
            && update.IndexOf("savedCharactersMenu.AddMenuItem(playingOutfitsRow);") < update.IndexOf("savedCharactersMenu.AddMenuItem(createCategoryBtn);")
            && mp.Contains("savedCharactersMenu.OnMenuOpen += (m) => RefreshPlayingOutfitsRow();") && mp.Contains("if (item == playingOutfitsRow)\n                {\n                    return;"),
            "the outfits row comes first, is named after the character you play each time, and never starts a new category");
        Check(mp.Contains("return key != \"\" && IsWearingCharacter(key) && !string.IsNullOrEmpty(GetResourceKvpString(key)) ? key : null;"),
            "the top row is the saved character you're playing, or none");
        Check(menu.Contains("else if (IsCurrentPedMpFreemode())") && menu.Contains("forModel = character.ModelHash;") && menu.Contains("outfit.Model == model") && wear.Contains("save.Model != (uint)GetEntityModel(ped)"),
            "a page lists outfits for its character's body type; with no character, only for MP characters");
        Check(menu.Contains("if (!WearOutfit(save, $\"Outfit {name}\"))") && wear.Contains("currentCharacter = ReplacePedDataClothing(currentCharacter, keepUnresolved: true);"), "wearing updates the character being worn, so Update Character Clothing keeps it");
        Check(wear.Contains("ClothingService.Resolve(save, ClothingResolver.OutfitSlots, ped)") && wear.Contains("ClothingService.Report(what, resolution, true);"), "wearing resolves by collection and reports pieces left off");
        Check(Regex.Matches(menu, @"CaptureCurrentOutfit\(\)\.ToJson\(\)").Count == 3 && capture.Contains("(slot, global, texture) => ClothingResolver.Capture(slot, global, texture, ClothingService.Layouts, runtime)"),
            "saving, updating and sharing what you wear record collection references");
        Check(menu.Contains("await GetUserConfirmation(\"Delete Outfit\"") && menu.Contains("await GetUserConfirmation(\"Update Outfit\""), "deleting and updating ask first");
        Check(Regex.Matches(menu, @"Taken\((entry|renamed)\) \|\| !Store\(").Count == 3 && menu.Contains("!OnBody() || Taken(entry) || !Store(entry, CaptureCurrentOutfit().ToJson())"),
            "saving, loading a code and renaming never overwrite an outfit");

        // One list: the character's own outfits, then the ones its body type shares.
        var entry = Regex.Match(mp, @"private sealed class OutfitEntry.*?\n        \}\n", RegexOptions.Singleline).Value;
        Check(entry.Contains("SavedOutfit.KeyFor(Name)") && entry.Contains("CharacterWardrobe.KeyFor(CharacterWardrobe.Kind.Outfit, CharacterKey, Name)"),
            "both kinds keep their storage: vmenu_outfit_<name> and vmenu_character_outfit_<character>|<name>");
        Check(menu.Contains("Section(forKey, $\"{Who(forKey)}'s Outfits\"") && menu.Contains("Section(null, Everyone(),")
            && menu.IndexOf("Section(forKey,") < menu.IndexOf("Section(null,") && menu.Contains("GetSpacerMenuItem($\"↓ {title} ↓\", description)"),
            "the character's own outfits come first, then the shared ones");
        Check(menu.Contains("var scopes = forKey == null ? new[] { (string)null } : new[] { forKey, null };") && menu.Contains("SetScopes(saveCurrent, \"Saves") && menu.Contains("SetScopes(loadCode, \"Loads"),
            "saving and loading a code choose who it's for: the page's character first, or the whole body type");
        Check(menu.Contains("Names(characterKey).Count < CharacterWardrobe.MaxEntries") && menu.Contains("if (!OnBody() || Full(characterKey))") && menu.Contains("if (Full(characterKey))"), "a character keeps its 50-outfit limit");
        Check(menu.Contains("CharacterWardrobe.CleanName(") && !menu.Contains("GetUserInput(\"Outfit name\", 30)"), "both kinds follow the same name rules");
        Check(menu.Contains("if (forKey != null && !await EnsureWearingCharacter(forKey))") && Regex.Matches(menu, @"await Wear\(json, name\)").Count == 2,
            "wearing one (or a loaded code) spawns the page's character first if you're someone else");
        Check(Regex.Matches(menu, @"OnBody\(\)").Count >= 4 && menu.Contains("if (IsCurrentPedMpFreemode() && PedModel() == forModel)"),
            "saving and updating need the character's body type, since they take what you're wearing");
        Check(!mp.Contains("CreateCharacterWardrobeMenu") && !mp.Contains("CreateSavedOutfitsMenu"), "one outfits page; the old wardrobe and Saved Outfits menus are gone");

        // Outfit codes live here.
        Check(Regex.IsMatch(menu, @"if \(OutfitCodesEnabled\)\s*\{\s*SetScopes\(loadCode, ""Loads.*?outfitsMenu\.AddMenuItem\(loadCode\);\s*outfitsMenu\.AddMenuItem\(shareCurrent\);", RegexOptions.Singleline)
            && menu.Contains("List<string> Actions() => OutfitCodesEnabled"), "Load Outfit Code, Share What I'm Wearing and Share only while outfit codes are on");
        var load = Regex.Match(menu, @"async Task LoadCode\(string characterKey\).*?\n            \}\n", RegexOptions.Singleline).Value;
        Check(load.Contains("await RequestOutfitCode(new JArray(Names(characterKey)).ToString(Formatting.None))") && load.Contains("ClothingService.OutfitFromCode((string)reply[\"code\"], (long?)reply[\"created\"] ?? 0, forModel, out error)")
            && load.IndexOf("Store(entry, json)") < load.IndexOf("await Wear(json, name)") && load.Contains("(bool?)reply[\"wear\"] == true"),
            "a code is fetched, checked for this body, saved as an outfit, then worn if asked");
        Check(load.Contains("if (!string.IsNullOrEmpty((string)reply[\"message\"]))") && load.Contains("Notify.Error(error);"), "refusals say why; closing the dialog says nothing");
        var share = Regex.Match(menu, @"async Task Share\(ClothingSave save, string what, bool wearing\).*?\n            \}\n", RegexOptions.Singleline).Value;
        Check(share.Contains("ClothingSaves.BuildCode(save, ClothingService.Layouts, out var unshareable)") && share.Contains("unshareable.Select(s => s.DisplayName)")
            && share.Contains("await ShareOutfitCode(code.ToString(Formatting.None))") && share.Contains("Notify.Error((string)reply[\"message\"]"),
            "sharing builds the code in C#, names pieces that can't be shared, and shows the server's reason");
        Check(menu.Contains("await Share(ClothingSaves.Read(ClothingSaves.Kind.Outfit, CaptureCurrentOutfit().ToJson()), \"what you're wearing\", true);")
            && menu.Contains("await Share(ClothingSaves.Read(ClothingSaves.Kind.Outfit, json), name, false);"), "share what you're wearing, or any saved outfit");
        var common = Read("vMenu", "CommonFunctions.cs");
        Check(common.Contains("await Exports[\"vMenu\"].requestOutfitCode(takenNamesJson)") && common.Contains("await Exports[\"vMenu\"].shareOutfitCode(codeJson)")
            && !common.Contains("loadSharedOutfit"), "the menu talks to client/outfitCodes.lua's exports");
        var lua = Read("build", "vMenu", "client", "outfitCodes.lua");
        foreach (var export in new[] { "requestOutfitCode", "shareOutfitCode" })
        {
            var body = Regex.Match(lua, "exports\\(\"" + export + "\", function\\(\\w+\\)\\n(.*?)\\nend\\)", RegexOptions.Singleline).Groups[1].Value.Replace("\r", "");
            Check(body.TrimStart().StartsWith("Wait(0)"), export + " yields first, so vMenu can always await it");
        }

        // The character creator: no outfit-code buttons, and the expression row found by reference.
        Check(!mp.Contains("Load Shared Outfit") && !mp.Contains("Generate Outfit Code") && !mp.Contains("vMenu:Outfits:GenerateCode"), "the creator has no outfit-code buttons");
        Check(!mp.Contains("createCharacterMenu.GetMenuItems().ElementAt(") && mp.Contains("faceExpressionList.ListIndex = index < 0 ? 0 : index;"),
            "editing a character shows its saved facial expression");
        foreach (var side in new[] { "client", "server" })
        {
            Check(Read("build", "vMenu", side, "backups.lua").Contains("'vmenu_outfit_'"), side + " backups include outfits");
        }
        Console.WriteLine("PASS: outfits pages in Saved Characters (both kinds of outfit, outfit codes), the creator, and backup wiring");
        return 0;
    }
}
