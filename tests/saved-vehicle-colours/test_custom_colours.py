"""Check how saved vehicles decide between custom RGB paint and a palette colour.

Compiles the production IsSavedColourCustom from vMenu/CommonFunctions.cs and checks new
saves (explicit flag), saves with the -1 "not custom" marker and the oldest saves (game RGB
for every paint). Also checks SaveVehicle writes the flag and spawning uses the helper.

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
common = (root / 'vMenu/CommonFunctions.cs').read_text(encoding='utf-8-sig')

helper = re.search(r'^        internal static bool IsSavedColourCustom\(.*?^        }$', common, re.M | re.S)[0]
assert 'colors.Add("customPrimary", primaryColorIsCustom ? 1 : 0);' in common
assert 'colors.Add("customSecondary", secondaryColorIsCustom ? 1 : 0);' in common
assert 'if (IsSavedColourCustom(vehicleInfo.colors, "customPrimary"))' in common
assert 'if (IsSavedColourCustom(vehicleInfo.colors, "customSecondary"))' in common

program = r'''
using System;
using System.Collections.Generic;

static class Program
{
''' + helper + r'''

    static Dictionary<string, int> Save(int r, int g, int b, int? flag)
    {
        var colors = new Dictionary<string, int> { ["customPrimaryR"] = r, ["customPrimaryG"] = g, ["customPrimaryB"] = b };
        if (flag.HasValue) colors["customPrimary"] = flag.Value;
        return colors;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static int Main()
    {
        // New saves carry the flag.
        Check(IsSavedColourCustom(Save(0, 0, 0, 1), "customPrimary"), "new save: custom black is custom");
        Check(IsSavedColourCustom(Save(255, 0, 0, 1), "customPrimary"), "new save: pure red is custom");
        Check(!IsSavedColourCustom(Save(-1, -1, -1, 0), "customPrimary"), "new save: palette colour is not custom");

        // Saves with the -1 marker but no flag.
        Check(!IsSavedColourCustom(Save(-1, -1, -1, null), "customPrimary"), "-1 marker: not custom");
        Check(IsSavedColourCustom(Save(10, 20, 30, null), "customPrimary"), "-1 era custom colour without zeros: custom");

        // Oldest saves stored the game's RGB for every paint: unchanged behaviour.
        Check(IsSavedColourCustom(Save(12, 12, 12, null), "customPrimary"), "oldest save, all channels > 0: as before (custom)");
        Check(!IsSavedColourCustom(Save(200, 0, 0, null), "customPrimary"), "oldest save with a 0 channel: as before (palette)");

        // Missing keys.
        Check(!IsSavedColourCustom(new Dictionary<string, int>(), "customPrimary"), "no custom keys: palette");
        Check(!IsSavedColourCustom(Save(0, 0, 0, 1), "customSecondary"), "prefixes are independent");
        Console.WriteLine("PASS: custom paint flag, -1 marker and oldest saves");
        return 0;
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-custom-colours-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
