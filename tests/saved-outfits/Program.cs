// Run: dotnet run --project tests/saved-outfits
// Drives the production SavedOutfit: what is captured (clothing only, never face or hair), how it
// dresses the ped, damaged storage, and the Saved Outfits menu wiring.
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

        var components = new Dictionary<int, (int, int)>();
        var props = new Dictionary<int, (int, int)>();
        var cleared = new List<int>();
        stored.Apply((slot, d, t) => components[slot] = (d, t), slot => cleared.Add(slot), (slot, d, t) => props[slot] = (d, t));
        Check(components.Count == 10 && !components.ContainsKey(0) && !components.ContainsKey(2) && components[11] == (110, 11), "wearing sets the clothing slots only");
        Check(props.Count == 2 && props[0] == (12, 2) && props[6] == (3, 1), "worn props are put on");
        Check(cleared.OrderBy(s => s).SequenceEqual(new[] { 1, 2, 3, 4, 5, 7 }), "props the outfit doesn't have are taken off");

        var damaged = SavedOutfit.Parse("{\"model\":1,\"clothes\":{\"0\":[5,5],\"2\":[9,9],\"3\":[4],\"4\":null,\"5\":[-2,0],\"6\":[7,1]},\"props\":{\"0\":[1,-5],\"1\":[3]}}");
        components.Clear(); props.Clear(); cleared.Clear();
        damaged.Apply((slot, d, t) => components[slot] = (d, t), slot => cleared.Add(slot), (slot, d, t) => props[slot] = (d, t));
        Check(components.Count == 1 && components[6] == (7, 1), "damaged, negative, face and hair entries are skipped");
        Check(props.Count == 1 && props[0] == (1, 0) && cleared.Count == 0, "a bad prop texture becomes 0; a damaged prop is left alone");
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
        var menu = Regex.Match(mp, @"private void CreateSavedOutfitsMenu\(Menu outfitsMenu\).*?\n        \}\n", RegexOptions.Singleline).Value;
        Check(mp.Contains("CreateSavedOutfitsMenu(savedOutfitsMenu);"), "MP Ped Customization has the menu");
        Check(menu.Contains("if (!IsCurrentPedMpFreemode())") && menu.Contains("outfit.Model == model"), "only for MP characters, and only outfits of the current body type");
        Check(menu.Contains("currentCharacter = ReplacePedDataClothing(currentCharacter);"), "wearing updates the character being edited, so saving keeps it");
        Check(menu.Contains("await GetUserConfirmation(\"Delete Outfit\""), "deleting asks first");
        Check(Regex.IsMatch(menu, @"if \(!string\.IsNullOrEmpty\(GetResourceKvpString\(SavedOutfit\.KeyFor\(name\)\)\)\)\s*\{\s*Notify\.Error"), "saving never overwrites an outfit");
        foreach (var side in new[] { "client", "server" })
        {
            Check(Read("build", "vMenu", side, "backups.lua").Contains("'vmenu_outfit_'"), side + " backups include outfits");
        }
        Console.WriteLine("PASS: Saved Outfits menu and backup wiring");
        return 0;
    }
}
