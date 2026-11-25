// Run: dotnet run --project tests/vehicle-pearlescent
// Players couldn't keep a pearlescent: it was only in Secondary Colors, every primary colour change
// reset it, and the rows always opened on Black. Drives the production PaintPearl and checks how
// Vehicle Colors uses it.
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
        // --- PaintPearl -----------------------------------------------------------------------
        var pearl = new PaintPearl();
        const int car = 1001, other = 2002, red = 27, blue = 64, purple = 145, black = 0;

        Check(pearl.AfterPrimaryChange(car, purple, red, metallic: true, chameleon: false) == red, "nothing picked: Metallic matches the pearlescent to the paint");
        Check(pearl.AfterPrimaryChange(car, purple, red, metallic: false, chameleon: false) == black, "nothing picked: other paint has no pearlescent");

        pearl.Keep(car, purple);
        Check(pearl.IsKept(car, purple), "a picked pearlescent is kept");
        Check(pearl.AfterPrimaryChange(car, purple, red, metallic: true, chameleon: false) == purple, "Metallic keeps a picked pearlescent");
        Check(pearl.AfterPrimaryChange(car, purple, blue, metallic: false, chameleon: false) == purple, "Classic, Matte and the rest keep it too");
        Check(pearl.AfterPrimaryChange(car, purple, 161, metallic: false, chameleon: true) == black, "chameleon paint still takes none");
        Check(pearl.AfterPrimaryChange(other, purple, red, metallic: true, chameleon: false) == red, "only on the vehicle it was picked on");
        Check(!pearl.IsKept(car, red) && pearl.AfterPrimaryChange(car, red, blue, metallic: true, chameleon: false) == blue,
            "once something else changes the pearlescent (a preset), it is automatic again");
        Check(!pearl.IsKept(0, 0), "no vehicle, nothing kept");
        pearl.Keep(other, blue);
        Check(!pearl.IsKept(car, purple) && pearl.IsKept(other, blue), "the last pick wins");

        Check(PaintPearl.IsOwnChoice(red, purple), "a save with a different pearlescent was a choice");
        Check(!PaintPearl.IsOwnChoice(red, red) && !PaintPearl.IsOwnChoice(red, black), "a matching or no pearlescent is what the paint sets");

        var ids = new[] { 0, 147, 1, 11 };
        Check(PaintPearl.RowOf(ids, 1) == 2, "a colour opens on its own row");
        Check(PaintPearl.RowOf(ids, 99) == 0, "a colour the list hasn't got opens on the first row");

        // --- wiring ---------------------------------------------------------------------------
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repository root found");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(dir!, Path.Combine(parts))).Replace("\r\n", "\n");
        var options = Read("vMenu", "menus", "VehicleOptions.cs");
        var common = Read("vMenu", "CommonFunctions.cs");

        var primaryAdds = Regex.Matches(options, @"primaryColorsMenu\.AddMenuItem\((\w+)\)").Select(m => m.Groups[1].Value).ToList();
        var secondaryAdds = Regex.Matches(options, @"secondaryColorsMenu\.AddMenuItem\((\w+)\)").Select(m => m.Groups[1].Value).ToList();
        Check(primaryAdds.IndexOf("pearlescentList") == primaryAdds.IndexOf("metallicList") + 1, "Primary Colors has Pearlescent right under Metallic");
        Check(secondaryAdds.Contains("pearlescentList"), "Secondary Colors keeps its Pearlescent row");

        var loop = Regex.Match(options, @"for \(var i = 0; i < 2; i\+\+\)\s*\{\s*var customColour.*?\n            \}\n", RegexOptions.Singleline).Value;
        Check(loop.Length > 0, "paint row loop found");
        foreach (Match list in Regex.Matches(loop, @"var (\w+List) = new MenuListItem\("))
        {
            Check(Regex.IsMatch(loop, @"paintRows\[" + list.Groups[1].Value + @"\] = PaintRow\.\w+;"), list.Groups[1].Value + " is registered as a paint row");
        }
        Check(loop.Contains("Changing the paint colour keeps the pearlescent you pick here."), "the Pearlescent row says the pick is kept");

        var handler = Regex.Match(options, @"void HandleListIndexChanges\(.*?\n            \}\n", RegexOptions.Singleline).Value;
        Check(handler.Length > 0, "colour handler found");
        Check(!Regex.IsMatch(handler, @"itemIndex\s*==|case \d+:"), "rows are known by name, not by their position in the menu");
        Check(Regex.IsMatch(handler, @"if \(row == PaintRow\.Pearlescent\)\s*\{[^}]*pearlColor = colour;\s*KeptPearlescent\.Keep\(veh\.Handle, pearlColor\);"),
            "picking a pearlescent remembers it");
        var primaryBranch = Regex.Match(handler, @"else if \(primary\)\s*\{.*?SetVehicleColours\(veh\.Handle, primaryColor, secondaryColor\);", RegexOptions.Singleline).Value;
        Check(primaryBranch.Contains("pearlColor = KeptPearlescent.AfterPrimaryChange(veh.Handle, pearlColor, primaryColor,"), "a primary colour change goes through PaintPearl");
        Check(primaryBranch.Contains("row == PaintRow.Metallic, row == PaintRow.Chameleon"), "Metallic and chameleon keep their own pearlescent rules");
        Check(handler.Contains("SetVehicleExtraColours(veh.Handle, pearlColor, wheelColor);"), "the pearlescent is written back");

        Check(options.Contains("primaryColorsMenu.OnMenuOpen += (_) => ShowCurrentPaint(primaryPaintRows, true);")
            && options.Contains("secondaryColorsMenu.OnMenuOpen += (_) => ShowCurrentPaint(secondaryPaintRows, false);"), "both colour menus open on the vehicle's colours");
        var show = Regex.Match(options, @"void ShowCurrentPaint\(.*?\n            \}\n", RegexOptions.Singleline).Value;
        Check(show.Contains("paintRow.Value == PaintRow.Pearlescent ? pearlColor : primary ? primaryColor : secondaryColor"), "the Pearlescent rows show the pearlescent");
        Check(!show.Contains("SetVehicle"), "opening the menu doesn't repaint anything");
        Check(Regex.IsMatch(options, @"GetVehicleInteriorColour\(veh\.Handle, ref dashColor\);\s*GetVehicleDashboardColour\(veh\.Handle, ref intColor\);"),
            "dashboard and interior rows read back with the natives that set them");
        Check(options.Contains("wheelColorsList.ListIndex = wheelColor == 156 || wheelRow < 0 ? 0 : wheelRow + 1;"), "the wheel row opens on the wheel colour");

        Check(Regex.IsMatch(common, @"SetVehicleExtraColours\(vehicle\.Handle, vehicleInfo\.colors\[""pearlescent""\], vehicleInfo\.colors\[""wheels""\]\);\s*// [^\n]*\n\s*if \(vehicleInfo\.colors\.TryGetValue\(""primary"", out var savedPrimary\)\s*&& PaintPearl\.IsOwnChoice\(savedPrimary, vehicleInfo\.colors\[""pearlescent""\]\)\)\s*\{\s*VehicleOptions\.KeptPearlescent\.Keep\(vehicle\.Handle, vehicleInfo\.colors\[""pearlescent""\]\);"),
            "a saved vehicle's own pearlescent is kept too");

        Console.WriteLine("vehicle-pearlescent: all checks passed");
        return 0;
    }
}
