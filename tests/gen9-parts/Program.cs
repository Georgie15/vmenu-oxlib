// Run: dotnet run --project tests/gen9-parts
// FiveM Legacy (game build 2699+) lists clothing and vehicle upgrades made only for GTA V
// Enhanced, then refuses to fit them. Drives the production Gen9Skip and checks that the
// vehicle upgrade lists and both clothing menus use it.
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
        // --- Kept: the upgrade list -----------------------------------------------------------
        Check(Gen9Skip.Kept(5, _ => false).SequenceEqual(new[] { 0, 1, 2, 3, 4 }), "nothing gen9: every upgrade is kept");
        Check(Gen9Skip.Kept(6, i => i == 2 || i == 5).SequenceEqual(new[] { 0, 1, 3, 4 }), "gen9 upgrades are left out, the rest keep their game index");
        Check(Gen9Skip.Kept(3, _ => true).Count == 0, "all gen9: only Stock is left");
        Check(Gen9Skip.Kept(0, _ => throw new Exception("asked")).Count == 0, "no upgrades: the game isn't asked");

        // --- Step: clothing lists -------------------------------------------------------------
        bool Gen9(int i) => i == 3 || i == 4 || i == 9;
        Check(Gen9Skip.Step(1, 2, 10, Gen9) == 2, "a fittable piece is worn as picked");
        Check(Gen9Skip.Step(2, 3, 10, Gen9) == 5, "right: steps past gen9 pieces to the next fittable one");
        Check(Gen9Skip.Step(6, 5, 10, Gen9) == 5, "left onto a fittable piece stays");
        Check(Gen9Skip.Step(5, 4, 10, Gen9) == 2, "left: steps past gen9 pieces the other way");
        Check(Gen9Skip.Step(8, 9, 10, Gen9) == 0, "right past the last piece wraps to the first");
        Check(Gen9Skip.Step(0, 9, 10, Gen9) == 8, "left past the first piece wraps to the last");
        Check(Gen9Skip.Step(0, 1, 2, i => i == 1) == 0, "only gen9 pieces left: stays on the current one");
        Check(Gen9Skip.Step(0, 1, 4, i => i != 0) == 0, "every other piece gen9: stays on the current one");
        Check(Gen9Skip.Step(0, 0, 1, _ => true) == 0, "a one-piece list is left alone");
        Check(Gen9Skip.Step(0, 7, 5, _ => throw new Exception("asked")) == 7, "an out-of-range index is left to the menu");
        var asked = 0;
        Check(Gen9Skip.Step(0, 1, 100, i => { asked++; return i < 99 && i > 0; }) == 99 && asked <= 99, "a long run of gen9 pieces is crossed once");

        // --- wiring ---------------------------------------------------------------------------
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repository root found");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(dir!, Path.Combine(parts))).Replace("\r\n", "\n");

        var parts = Read("vMenu", "Gen9Parts.cs");
        Check(parts.Contains("(Hash)0x00834EAC4A96E010"), "the vehicle check is IS_VEHICLE_MOD_GEN9_EXCLUSIVE");
        Check(Regex.IsMatch(parts, @"catch \(Exception e\)\s*\{\s*(//[^\n]*\n\s*)*vehicleCheckMissing = true;"),
            "a game build without the vehicle native turns the check off instead of breaking the mod menu");
        Check(parts.Contains("IsPedComponentVariationGen9Exclusive(ped, component, drawable)"), "the clothing check is FiveM's gen9 native");
        Check(!Regex.IsMatch(parts, @"catch[^{]*\{[^}]*await"), "no await inside catch (FiveM's Mono refuses it)");

        var vehicle = Read("vMenu", "menus", "VehicleOptions.cs");
        var update = Regex.Match(vehicle, @"public void UpdateMods\(int selectedIndex = 0\).*?#endregion", RegexOptions.Singleline).Value;
        Check(update.Contains("modListUpgrades.Clear();"), "the row-to-upgrade map is rebuilt with the menu");
        Check(Regex.IsMatch(update, @"Gen9Skip\.Kept\(mod\.ModCount, x => Gen9Parts\.IsVehicleMod\(modHandle, modTypeId, x\)\)"),
            "the upgrade lists leave out gen9 upgrades");
        Check(Regex.IsMatch(update, @"for \(var position = 0; position < fittable\.Count; position\+\+\)\s*\{\s*var x = fittable\[position\];"),
            "each row names the upgrade it fits");
        Check(update.Contains("var currIndex = fittable.IndexOf(GetVehicleMod(veh.Handle, modTypeId)) + 1;"), "the list opens on the fitted upgrade");
        Check(update.Contains("modListUpgrades[modTypeListItem] = fittable;"), "each list remembers its upgrades");
        Check(Regex.IsMatch(vehicle, @"var selectedUpgrade = item2\.ListIndex - 1;\s*if \(selectedUpgrade >= 0 && modListUpgrades\.TryGetValue\(item2, out var upgrades\)\)\s*\{\s*selectedUpgrade = selectedUpgrade < upgrades\.Count \? upgrades\[selectedUpgrade\] : -1;\s*\}"),
            "picking a row fits that row's upgrade, not the row number");

        var mp = Read("vMenu", "menus", "MpPedCustomization.cs");
        var mpHandler = Regex.Match(mp, @"clothesMenu\.OnListIndexChange \+=.*?\n            \};", RegexOptions.Singleline).Value;
        Check(Regex.IsMatch(mpHandler, @"var fittable = Gen9Skip\.Step\(oldSelectionIndex, newSelectionIndex, listItem\.ItemsCount,\s*drawable => Gen9Parts\.IsClothing\(ped, componentIndex, drawable\)\);\s*if \(fittable != newSelectionIndex\)\s*\{\s*newSelectionIndex = fittable;\s*listItem\.ListIndex = fittable;\s*\}"),
            "the character creator's clothes step past gen9 pieces");
        Check(mpHandler.IndexOf("Gen9Skip.Step") >= 0 && mpHandler.IndexOf("Gen9Skip.Step") < mpHandler.IndexOf("SetPedComponentVariation"), "the skip happens before the piece is put on");
        Check(mpHandler.Contains("KeyValuePair<int, int>(newSelectionIndex, newTextureIndex)"), "the character saves the piece actually worn");

        var appearance = Read("vMenu", "menus", "PlayerAppearance.cs");
        var apHandler = Regex.Match(appearance, @"pedCustomizationMenu\.OnListIndexChange \+=.*?\n            \};", RegexOptions.Singleline).Value;
        Check(Regex.IsMatch(apHandler, @"var fittable = Gen9Skip\.Step\(oldListIndex, newListIndex, item\.ItemsCount,\s*drawable => Gen9Parts\.IsClothing\(ped, drawableID, drawable\)\);\s*if \(fittable != newListIndex\)\s*\{\s*newListIndex = fittable;\s*item\.ListIndex = fittable;\s*\}\s*SetPedComponentVariation\(Game\.PlayerPed\.Handle, drawableID, newListIndex, 0, 0\);"),
            "Customize Ped's clothes step past gen9 pieces");

        Console.WriteLine("gen9-parts: all checks passed");
        return 0;
    }
}
