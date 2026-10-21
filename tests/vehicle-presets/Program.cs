// Run: dotnet run --project tests/vehicle-presets
// Drives the production VehiclePreset with a fake vehicle: what is captured, what is applied (only
// extras the vehicle has, liveries within range), storage round trip, and where presets are used.
using System;
using System.Collections.Generic;
using System.IO;
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
        // A police car with extras 1, 2 and 15 (15 was never shown by the old 0-13 menu loop).
        var extras = new Dictionary<int, bool> { [1] = true, [2] = false, [15] = true };
        var preset = VehiclePreset.Capture(extras.ContainsKey, e => extras[e], livery: 3, modLivery: -1);
        Check(preset.Extras.Count == 3 && preset.Extras[1] && !preset.Extras[2] && preset.Extras[15], "captures every extra 0-19 the vehicle has");
        Check(preset.Livery == 3 && preset.ModLivery == -1, "captures the livery, -1 for none");
        Check(VehiclePreset.Capture(e => true, e => true, -5, -2).Extras.Count == VehiclePreset.ExtraCount, "never reads beyond extra 19");
        Check(VehiclePreset.Capture(e => true, e => true, -5, -2) is { Livery: -1, ModLivery: -1 }, "negative liveries are stored as none");

        var stored = VehiclePreset.Parse(preset.ToJson());
        Check(stored != null && stored.Extras.Count == 3 && stored.Extras[15] && stored.Livery == 3, "round trips through storage");
        Check(VehiclePreset.Parse(null) == null && VehiclePreset.Parse("") == null && VehiclePreset.Parse("{not json") == null, "missing or corrupt presets are ignored");
        Check(VehiclePreset.Parse("{}") is { IsEmpty: true }, "an empty preset is recognised");
        Check(VehiclePreset.Parse("{\"extras\":null}")?.Extras != null, "null extras are treated as none");

        // Applying to the same model in a newer version that dropped extra 15 and has fewer liveries.
        var set = new Dictionary<int, bool>();
        var liveries = new List<int>();
        var modLiveries = new List<int>();
        stored.Apply(e => e != 15, (e, on) => set[e] = on, 4, liveries.Add, 0, modLiveries.Add);
        Check(set.Count == 2 && set[1] && !set[2], "applies only extras the vehicle has");
        Check(liveries.Count == 1 && liveries[0] == 3 && modLiveries.Count == 0, "applies a livery in range, skips a missing kind");
        liveries.Clear();
        stored.Apply(e => true, (e, on) => { }, 3, liveries.Add, 0, modLiveries.Add);
        Check(liveries.Count == 0, "a livery index the vehicle doesn't have is skipped");
        var odd = VehiclePreset.Parse("{\"extras\":{\"-1\":true,\"25\":true,\"4\":true}}");
        set.Clear();
        odd.Apply(e => true, (e, on) => set[e] = on, 0, liveries.Add, 0, modLiveries.Add);
        Check(set.Count == 1 && set.ContainsKey(4), "out-of-range extras from a stored preset are ignored");

        Check(VehiclePreset.KeyFor(0x79FBB0C5u) == "vmenu_vehicle_preset_2046537925", "stored per model hash");
        Check(VehiclePreset.KeyFor(1).StartsWith("vmenu"), "the key survives MainMenu's one-off KVP cleanup");
        Console.WriteLine("PASS: capture, apply (missing extras, livery range), storage round trip and key");

        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(dir, Path.Combine(parts))).Replace("\r\n", "\n");

        var common = Read("vMenu", "CommonFunctions.cs");
        var spawnTail = common.Substring(common.IndexOf("if (saveName != null)\n", StringComparison.Ordinal));
        Check(Regex.IsMatch(spawnTail, @"^if \(saveName != null\)\s*\{\s*ApplyVehicleModsDelayed\(.*?\}\s*else\s*\{\s*//[^\n]*\n\s*ApplyVehiclePreset\(vehicle\.Handle\);", RegexOptions.Singleline),
            "SpawnVehicleCore applies the preset only when no saved setup is given");
        var bridge = Read("vMenu", "LeoSpawncodeBridge.cs");
        Check(bridge.IndexOf("ApplyVehiclePreset(vehicleHandle)", StringComparison.Ordinal) < bridge.IndexOf("SetVehicleExtra(vehicleHandle, extra, false)", StringComparison.Ordinal)
            && bridge.Contains("if (!ApplyVehiclePreset(vehicleHandle))"), "LEO spawn codes use the preset instead of every extra on");
        var options = Read("vMenu", "menus", "VehicleOptions.cs");
        Check(options.Contains("extra < VehiclePreset.ExtraCount") && !options.Contains("extra < 14"), "the Extras menu lists extras 0-19 like saves and presets");
        Check(Regex.Matches(options, @"VehicleComponentsMenu\.OnItemSelect \+=").Count == 1, "preset items share the single Extras handler");
        foreach (var lua in new[] { "client", "server" })
        {
            Check(Read("build", "vMenu", lua, "backups.lua").Contains("'vmenu_vehicle_preset_'"), $"{lua} backups include presets");
        }
        Console.WriteLine("PASS: spawn, LEO bridge, Extras menu and backup wiring");
        return 0;
    }
}
