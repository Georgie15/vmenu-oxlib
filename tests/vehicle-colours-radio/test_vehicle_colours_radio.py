"""Check hex paint codes, roof liveries in saved vehicles, the radio settings and their wiring.

Compiles the production VehicleStyle and VehicleRadioSettings, the VehicleInfo struct from
vMenu/CommonFunctions.cs (with Newtonsoft, as saved vehicles are stored) and
IsAllowedWithFallback from SharedClasses/PermissionsManager.cs, then checks: hex codes are
validated, old saves without a roof livery load unchanged and new ones round trip, radio choices
and blocked stations parse and carry over from the old setting, and new permissions fall back
while the server doesn't know them. Also checks the radio is no longer set on spawn and is only
applied from the existing vehicle-entry detection.

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[2]


def read(path):
    return (root / path).read_text(encoding='utf-8-sig').replace('\r\n', '\n')


common = read('vMenu/CommonFunctions.cs')
options = read('vMenu/menus/VehicleOptions.cs')
functions = read('vMenu/FunctionsController.cs')
radio = read('vMenu/VehicleRadio.cs')
radio_settings = read('vMenu/VehicleRadioSettings.cs')
perms = read('SharedClasses/PermissionsManager.cs')
cfg = read('build/vMenu/config/permissions.cfg')


def block(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


# --- Radio wiring -------------------------------------------------------------------------------
# Spawning no longer touches the radio (it forced Los Santos Rock Radio on every spawn).
spawn = block(common, r'^        private static async Task<int> SpawnVehicleCore\(.*?^        }$')
assert 'RadioStation' not in spawn and 'VehicleDefaultRadio' not in spawn, 'spawning must not set the radio'
radio_calls = re.compile(r'\.RadioStation\s*=|SetVehRadioStation|SetRadioToStation|SetVehicleRadioEnabled|LockRadioStation|SetRadioStationIsVisible|SetRadioStationDisabled')
for path in sorted((root / 'vMenu').rglob('*.cs')):
    if path.name == 'VehicleRadio.cs' or 'bin' in path.parts or 'obj' in path.parts:
        continue
    text = path.read_text(encoding='utf-8-sig')
    assert not radio_calls.search(text), f'{path.relative_to(root)} changes the radio; only VehicleRadio may'

# Applied from the existing 100 ms vehicle check each time the player becomes a driver (getting in,
# getting back into the same vehicle, or moving over from a passenger seat), and nothing new per frame.
general = block(functions, r'^        private async Task GeneralTasks\(\).*?^        }$')
assert re.search(r'var driving = seated \? GetVehiclePedIsIn\(ped, false\) : 0;\s*'
                 r'if \(driving != 0 && GetPedInVehicleSeat\(driving, -1\) == ped\)\s*\{\s*'
                 r'if \(driving != radioAppliedVehicle\)\s*\{\s*radioAppliedVehicle = driving;\s*VehicleRadio\.ApplyTo\(driving\);\s*\}\s*\}\s*'
                 r'else\s*\{\s*radioAppliedVehicle = 0;\s*\}', general), 'radio applied once each time the player becomes the driver'
assert general.index('VehicleRadio.ApplyTo(driving);') < general.index('await Rest(StateRestMs);')
assert functions.count('VehicleRadio.') == 1, 'FunctionsController makes a single call into VehicleRadio'
assert 'Tick' not in radio and 'while (true)' not in radio, 'VehicleRadio adds no per-frame work'
tune = block(radio, r'^        private static async Task TuneAsync\(.*?^        }$')
assert 'GetGameTimer() < giveUpAt' in tune, 'waiting for the game to finish tuning is bounded'
assert 'GetPedInVehicleSeat(vehicle, -1) != PlayerPedId()' in tune, 'only the driver tunes'
apply_to = block(radio, r'^        internal static void ApplyTo\(.*?^        }$')
assert '!Allowed' in apply_to and 'StationOnEntry(' in apply_to
# The old setting is carried over once, and stations are read before any are blocked.
initialize = block(radio, r'^        internal static void Initialize\(\).*?^        }$')
assert initialize.index('Stations();') < initialize.index('ApplyBlocked('), 'station list read before blocking'
assert 'TakeLegacyVehicleDefaultRadio()' in initialize
assert 'VehicleRadio.Initialize();' in options

# --- Menu wiring ----------------------------------------------------------------------------------
# HandleListIndexChanges picks the paint list by position, so the hex item must come after them.
primary_adds = re.findall(r'primaryColorsMenu\.AddMenuItem\((\w+)\)', options)
secondary_adds = re.findall(r'secondaryColorsMenu\.AddMenuItem\((\w+)\)', options)
assert primary_adds[-1] == 'hexColour' and primary_adds.index('hexColour') > primary_adds.index('chameleonList'), primary_adds
assert secondary_adds[-1] == 'hexColour' and secondary_adds[:2] == ['customColour', 'pearlescentList'], secondary_adds
assert 'VehicleStyle.TryParseColourCode(chosenColour, out var r, out var g, out var b)' in options, 'both colour inputs are validated'
assert 'Convert.ToInt32(chosenColour' not in options, 'the unvalidated parse is gone'
assert 'new MenuSliderItem("Paint Fade"' in options and 'Vehicle Enveff Scale' not in options, 'enveff scale is presented as paint fade'
preset = block(options, r'^            void ChangeVehiclePresetColor\(int index\).*?^            }$')
assert preset.index('ClearVehicleCustomPrimaryColour') < preset.index('SetVehicleColourCombination'), 'factory colours replace custom paint'
assert 'SetVehicleRoofLivery(veh.Handle, VehicleStyle.RoofLiveryFromListIndex(newIndex))' in options
assert 'IsAllowedWithFallback(Permission.VORoofLivery, Permission.VOLiveries)' in options
assert 'IsAllowedWithFallback(Permission.VOHexColors, Permission.VOColors)' in options
assert 'IsAllowedWithFallback(Permission.VORadio, Permission.VOMenu)' in radio

# --- Saved vehicles --------------------------------------------------------------------------------
capture = block(common, r'^        internal static VehicleInfo CaptureVehicleInfo\(.*?^        }$')
assert 'roofLivery = GetVehicleRoofLiveryCount(veh.Handle) > 0 ? GetVehicleRoofLivery(veh.Handle) : (int?)null' in capture
apply_mods = block(common, r'^        private static async void ApplyVehicleModsDelayed\(.*?^        }$')
assert 'VehicleStyle.ShouldRestoreRoofLivery(vehicleInfo.roofLivery, GetVehicleRoofLiveryCount(vehicle.Handle))' in apply_mods

# --- Permissions -----------------------------------------------------------------------------------
enum = block(perms, r'^        public enum Permission\s*\{.*?^        \}')
for name, ace in (('VORoofLivery', 'RoofLivery'), ('VOHexColors', 'HexColors'), ('VORadio', 'Radio')):
    assert re.search(rf'^\s*{name},$', enum, re.M), f'{name} in the Permission enum'
    assert f'add_ace builtin.everyone "vMenu.VehicleOptions.{ace}" allow' in cfg.split('\n'), f'{ace} granted in permissions.cfg'
fallback = block(perms, r'^        public static bool IsAllowedWithFallback\(.*?;$')

# Comments in the legacy table name CitizenFX's RadioStation enum values, in order (0-20).
legacy_enum = ['LosSantosRockRadio', 'NonStopPopFM', 'RadioLosSantos', 'ChannelX', 'WestCoastTalkRadio', 'RebelRadio',
               'SoulwaxFM', 'EastLosFM', 'WestCoastClassics', 'BlaineCountyRadio', 'TheBlueArk', 'WorldWideFM', 'FlyloFM',
               'TheLowdown', 'RadioMirrorPark', 'Space', 'VinewoodBoulevardRadio', 'SelfRadio', 'TheLab', 'BlondedLosSantos',
               'LosSantosUndergroundRadio']
table = block(radio_settings, r'LegacyStations =\s*\{.*?\};')
assert re.findall(r'//\s*(\w+)', table) == legacy_enum, 'legacy stations follow the RadioStation enum order'
print('PASS: radio, menu, save and permission wiring')

program = r'''
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using vMenuClient;

static class Program
{
''' + block(common, r'^        public struct VehicleInfo.*?^        };') + '\n' + enum + r'''

    static Dictionary<Permission, bool> Permissions = new Dictionary<Permission, bool>();
    static HashSet<Permission> granted = new HashSet<Permission>();
    static bool IsAllowed(Permission permission, bool checkAnyway = false) => granted.Contains(permission);
''' + fallback + r'''

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static bool Hex(string text, int r, int g, int b)
    {
        return VehicleStyle.TryParseColourCode(text, out var red, out var green, out var blue) && red == r && green == g && blue == b;
    }

    static int Main()
    {
        // Hex colour codes.
        Check(Hex("#FF8800", 255, 136, 0), "#RRGGBB");
        Check(Hex("ff8800", 255, 136, 0), "no #, lower case");
        Check(Hex("  #Ff8800 ", 255, 136, 0), "trimmed, mixed case");
        Check(Hex("#F80", 255, 136, 0), "#RGB short form");
        Check(Hex("#000000", 0, 0, 0) && Hex("#FFFFFF", 255, 255, 255), "black and white");
        foreach (var bad in new[] { null, "", "#", "#12345", "#1234567", "#FF880080", "#GG0000", "red", "0x00FF00", "# FF8800", "#FF 880" })
        {
            Check(!VehicleStyle.TryParseColourCode(bad, out _, out _, out _), "refused: " + (bad ?? "null"));
        }
        Check(VehicleStyle.FormatColourCode(255, 136, 0) == "#FF8800", "format");
        Check(VehicleStyle.FormatColourCode(-4, 300, 7) == "#00FF07", "format clamps channels");
        for (var c = 0; c < 256; c++)
        {
            Check(Hex(VehicleStyle.FormatColourCode(c, 255 - c, c / 2), c, 255 - c, c / 2), "round trip " + c);
        }
        Console.WriteLine("PASS: hex colour codes");

        // Roof liveries.
        Check(!VehicleStyle.ShouldRestoreRoofLivery(null, 5), "old saves: nothing to restore");
        Check(!VehicleStyle.ShouldRestoreRoofLivery(-1, 5), "none saved: left as spawned");
        Check(VehicleStyle.ShouldRestoreRoofLivery(0, 5) && VehicleStyle.ShouldRestoreRoofLivery(4, 5), "saved livery in range");
        Check(!VehicleStyle.ShouldRestoreRoofLivery(5, 5) && !VehicleStyle.ShouldRestoreRoofLivery(2, 0) && !VehicleStyle.ShouldRestoreRoofLivery(2, -1), "out of range or no roof liveries");
        Check(VehicleStyle.RoofLiveryListIndex(-1, 3) == 0 && VehicleStyle.RoofLiveryListIndex(0, 3) == 1 && VehicleStyle.RoofLiveryListIndex(2, 3) == 3, "list starts with None");
        Check(VehicleStyle.RoofLiveryListIndex(9, 3) == 3 && VehicleStyle.RoofLiveryListIndex(-7, 3) == 0, "list index clamped");
        Check(VehicleStyle.RoofLiveryFromListIndex(0) == -1 && VehicleStyle.RoofLiveryFromListIndex(3) == 2, "None is -1");

        // Saved vehicles: an old save has no roofLivery and loads (and re-saves) unchanged.
        var oldJson = "{\"colors\":{\"primary\":1},\"livery\":2,\"model\":123,\"plateText\":\"OLD\",\"enveffScale\":0.5,\"Category\":\"Uncategorized\"}";
        var old = JsonConvert.DeserializeObject<VehicleInfo>(oldJson);
        Check(old.roofLivery == null && old.livery == 2 && old.plateText == "OLD", "old save loads, no roof livery");
        Check(!VehicleStyle.ShouldRestoreRoofLivery(old.roofLivery, 4), "old save on a roof-livery vehicle spawns as before");
        Check(!JsonConvert.SerializeObject(old).Contains("roofLivery"), "re-saving an old save adds no roofLivery");
        var withRoof = old;
        withRoof.roofLivery = 2;
        var json = JsonConvert.SerializeObject(withRoof);
        Check(json.Contains("\"roofLivery\":2"), "roof livery saved");
        Check(JsonConvert.DeserializeObject<VehicleInfo>(json).roofLivery == 2, "roof livery round trips");
        withRoof.roofLivery = -1;
        Check(JsonConvert.DeserializeObject<VehicleInfo>(JsonConvert.SerializeObject(withRoof)).roofLivery == -1, "none (-1) round trips");
        Console.WriteLine("PASS: roof liveries, old and new saves");

        // Radio: what happens on getting in.
        var none = new HashSet<string>();
        var blockedPop = VehicleRadioSettings.ParseBlocked("RADIO_02_POP");
        Check(VehicleRadioSettings.StationOnEntry("", none) == null && VehicleRadioSettings.StationOnEntry(null, none) == null && VehicleRadioSettings.StationOnEntry("  ", none) == null, "unset: radio left alone");
        Check(VehicleRadioSettings.StationOnEntry("OFF", blockedPop) == "OFF", "radio off");
        Check(VehicleRadioSettings.StationOnEntry("RADIO_02_POP", none) == "RADIO_02_POP", "default station");
        Check(VehicleRadioSettings.StationOnEntry("RADIO_02_POP", blockedPop) == null, "a blocked default station is not tuned");

        // Carrying over the old "Default radio station" (CitizenFX RadioStation values).
        Check(VehicleRadioSettings.MigrateLegacyDefault(0) == null, "0 (also unset) is not carried over");
        Check(VehicleRadioSettings.MigrateLegacyDefault(255) == "OFF", "RadioOff");
        Check(VehicleRadioSettings.MigrateLegacyDefault(1) == "RADIO_02_POP" && VehicleRadioSettings.MigrateLegacyDefault(13) == "RADIO_15_MOTOWN"
            && VehicleRadioSettings.MigrateLegacyDefault(20) == "RADIO_22_DLC_BATTLE_MIX1_RADIO", "stations by enum value");
        Check(VehicleRadioSettings.MigrateLegacyDefault(21) == null && VehicleRadioSettings.MigrateLegacyDefault(-1) == null, "unknown values ignored");

        // Blocked stations.
        Check(VehicleRadioSettings.ParseBlocked(null).Count == 0 && VehicleRadioSettings.ParseBlocked(" , ,").Count == 0, "nothing blocked");
        var parsed = VehicleRadioSettings.ParseBlocked("B, A,,A,OFF");
        Check(parsed.Count == 2 && parsed.Contains("A") && parsed.Contains("B"), "parsed, trimmed, no repeats or OFF");
        Check(VehicleRadioSettings.FormatBlocked(new[] { "B", "A", " ", "OFF", "A", null, "X,Y" }) == "A,B", "formatted sorted, without blanks, OFF or separators");
        Check(VehicleRadioSettings.FormatBlocked(VehicleRadioSettings.ParseBlocked("RADIO_13_JAZZ,RADIO_02_POP")) == "RADIO_02_POP,RADIO_13_JAZZ", "round trip");

        // Station lists.
        var stations = VehicleRadioSettings.StationList(new[] { "RADIO_01", "", null, "OFF", "RADIO_01", "HIDDEN_RADIO_AMBIENT", "RADIO_02" }, new[] { "RADIO_99", "RADIO_02" });
        Check(stations.SequenceEqual(new[] { "RADIO_01", "RADIO_02", "RADIO_99" }), "game order, cleaned, blocked-but-unlisted kept: " + string.Join(",", stations));
        var entry = VehicleRadioSettings.EntryOptions(new[] { "RADIO_01", "RADIO_02" }, "");
        Check(entry.SequenceEqual(new[] { "", "OFF", "RADIO_01", "RADIO_02" }), "leave alone, off, then stations");
        Check(VehicleRadioSettings.EntryIndex(entry, "") == 0 && VehicleRadioSettings.EntryIndex(entry, "OFF") == 1 && VehicleRadioSettings.EntryIndex(entry, "RADIO_02") == 3, "entry index");
        var kept = VehicleRadioSettings.EntryOptions(new[] { "RADIO_01" }, "RADIO_77");
        Check(kept.Last() == "RADIO_77" && VehicleRadioSettings.EntryIndex(kept, "RADIO_77") == kept.Count - 1, "a saved station this game doesn't list is kept");
        Check(VehicleRadioSettings.EntryIndex(entry, "RADIO_GONE") == 0, "unknown falls back to leave alone");
        Console.WriteLine("PASS: radio choices, carry-over and blocked stations");

        // New permissions while the server doesn't know them yet.
        granted = new HashSet<Permission> { Permission.VOMenu, Permission.VOLiveries };
        Permissions = new Dictionary<Permission, bool> { [Permission.VOMenu] = true, [Permission.VOLiveries] = true };
        Check(IsAllowedWithFallback(Permission.VORadio, Permission.VOMenu), "old server: radio follows Vehicle Options");
        Check(IsAllowedWithFallback(Permission.VORoofLivery, Permission.VOLiveries), "old server: roof livery follows Liveries");
        Check(!IsAllowedWithFallback(Permission.VOHexColors, Permission.VOColors), "old server: hex follows Colors (denied here)");
        Permissions[Permission.VORadio] = false;
        Check(!IsAllowedWithFallback(Permission.VORadio, Permission.VOMenu), "updated server: its answer counts");
        Permissions[Permission.VOHexColors] = true;
        granted.Add(Permission.VOHexColors);
        Check(IsAllowedWithFallback(Permission.VOHexColors, Permission.VOColors), "updated server: granted");
        Console.WriteLine("PASS: permission fallback for an older server");
        return 0;
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-colours-radio-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    reference = escape(str(root / 'dependencies/shared/Newtonsoft.Json.dll'))
    (work / 'Tests.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                                       '<TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup>'
                                       '<ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>' + reference +
                                       '</HintPath></Reference></ItemGroup></Project>', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    for name in ('VehicleStyle.cs', 'VehicleRadioSettings.cs'):
        (work / name).write_bytes((root / 'vMenu' / name).read_bytes())
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
