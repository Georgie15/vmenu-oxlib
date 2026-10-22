// Run: dotnet run --project tests/model-shortlists
// Drives the production ModelShortlist behind Favourite and Recent Vehicles and Peds: order,
// capacity, duplicates, damaged storage, and how the spawner, Spawn Peds and backups use it.
using System;
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
        var recent = ModelShortlist.Parse(null, ModelShortlist.MaxRecent);
        for (uint model = 1; model <= 12; model++) recent.Touch(model);
        Check(recent.Models.Count == 10 && recent.Models[0] == 12 && recent.Models[9] == 3, "recent keeps the last 10, newest first");
        recent.Touch(5);
        Check(recent.Models[0] == 5 && recent.Models.Count(m => m == 5) == 1 && recent.Models.Count == 10, "spawning again moves it to the front, no duplicate");
        recent.Touch(0);
        Check(recent.Models[0] == 5, "model 0 is ignored");

        var favourites = ModelShortlist.Parse("", ModelShortlist.MaxFavourites);
        Check(favourites.Add(7) && favourites.Add(9) && !favourites.Add(7), "a favourite is added once");
        Check(favourites.Models.SequenceEqual(new uint[] { 7, 9 }), "favourites keep the order they were added");
        for (uint model = 100; favourites.Models.Count < ModelShortlist.MaxFavourites; model++) favourites.Add(model);
        Check(!favourites.Add(9999) && favourites.Models.Count == 50, "at most 50 favourites");
        Check(favourites.Remove(7) && !favourites.Contains(7) && !favourites.Remove(7), "a favourite can be removed");

        var stored = ModelShortlist.Parse(recent.ToJson(), ModelShortlist.MaxRecent);
        Check(stored.Models.SequenceEqual(recent.Models), "round trips through storage");
        Check(ModelShortlist.Parse("[1,1,2,0]", 10).Models.SequenceEqual(new uint[] { 1, 2 }), "stored duplicates and 0 are dropped");
        Check(ModelShortlist.Parse("[1,2,3]", 2).Models.Count == 2, "a stored list longer than the limit is trimmed");
        foreach (var damaged in new[] { "{", "\"x\"", "[-1]", "{\"a\":1}", "[\"car\"]" })
        {
            Check(ModelShortlist.Parse(damaged, 10).Models.Count == 0, "damaged storage reads as empty: " + damaged);
        }
        Check(ModelShortlist.Parse("[3735928559]", 10).Models[0] == 3735928559u, "full-range model hashes are kept");
        Check(ModelShortlist.VehicleFavouritesKey.StartsWith("vmenu") && ModelShortlist.VehicleRecentKey.StartsWith("vmenu"), "keys survive MainMenu's KVP cleanup");
        Console.WriteLine("PASS: recent order and limit, favourites, storage round trip, damaged storage");

        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(dir, Path.Combine(parts))).Replace("\r\n", "\n");

        var spawner = Read("vMenu", "menus", "VehicleSpawner.cs");
        Check(spawner.Contains("CreateShortlistMenus(favouritesMenu, recentMenu);"), "the spawner has both menus");
        Check(Regex.IsMatch(spawner, @"SpawnFromShortlist\(uint model\)\s*=> SpawnVehicle\(vehicleHash: model,.*saveName: null\);"),
            "favourites and recents spawn through the normal spawn checks");
        Check(spawner.Contains("favouritesMenu.OnMenuOpen += (m) => RebuildFavourites();") && spawner.Contains("recentMenu.OnMenuOpen += (m) => RebuildRecent();"),
            "both menus are rebuilt from storage when opened");
        var common = Read("vMenu", "CommonFunctions.cs");
        Check(Regex.IsMatch(common, @"else\s*\{\s*//[^\n]*\n\s*ApplyVehiclePreset\(vehicle\.Handle\);\s*VehicleSpawner\.RememberRecent\(vehicleHash\);"),
            "only fresh spawns are recorded");
        foreach (var side in new[] { "client", "server" })
        {
            var backups = Read("build", "vMenu", side, "backups.lua");
            Check(backups.Contains("'vmenu_vehicle_favourites'") && backups.Contains("'vmenu_vehicle_recent'"), side + " backups include both lists");
        }
        var appearance = Read("vMenu", "menus", "PlayerAppearance.cs");
        Check(appearance.Contains("CreatePedShortlistMenus(spawnPedsMenu);"), "Spawn Peds has Favourite and Recent Peds");
        Check(Regex.IsMatch(appearance, @"if \(data\.PedModels\.AnimalHashes\.Contains\(model\)\)\s*\{\s*if \(!animalsAllowed\)"),
            "an animal from the lists still needs animals to be allowed");
        Check(Regex.IsMatch(appearance, @"await SetPlayerSkin\(model, new PedInfo\(\) \{ version = -1 \}, true\);\s*\}\s*\n\s*/// <summary>\s*/// Favourite Peds"),
            "other peds go through SetPlayerSkin, so ped pack access still applies");
        Check(Regex.IsMatch(appearance, @"internal static void RememberRecentPed\(uint model\)\s*\{\s*if \(model == \(uint\)GetHashKey\(""mp_m_freemode_01""\) \|\| model == \(uint\)GetHashKey\(""mp_f_freemode_01""\)\)"),
            "MP characters aren't recorded");
        Check(Regex.IsMatch(common, @"SetPlayerModel\(Game\.Player\.Handle, modelHash\);\s*PlayerAppearance\.RememberRecentPed\(modelHash\);"),
            "a ped is recorded only when the model actually changes");
        foreach (var side in new[] { "client", "server" })
        {
            var backups = Read("build", "vMenu", side, "backups.lua");
            Check(backups.Contains("'vmenu_ped_favourites'") && backups.Contains("'vmenu_ped_recent'"), side + " backups include the ped lists");
        }
        Console.WriteLine("PASS: spawner, Spawn Peds, spawn core, ped change and backup wiring");
        return 0;
    }
}
