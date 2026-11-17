"""Release L: the upstream vMenu bug fixes PSRP was still missing, plus a range on Kick Passengers.

- License Plate Type: positions 1-4 set Yellow on Black, Yellow on Blue, Blue on White 2 and 3 under
  the game's labels for Blue on White 2 and 3, Yellow on Blue and Yellow on Black. One table
  (VehicleOptions.PlateStyles, upstream 9e4055ba's order) now drives setting and reading the style.
- Randomize Character saved the eyebrow style as the colour and the colour as the style.
- "↓ Characters ↓", "↓ Vehicles ↓" and the "→→→" labels showed as garbled text.
- The mod menu's toggle-all-doors key looked only at the driver's door (upstream 2c7b9ab5) and
  skipped the all-doors cooldown.
- A saved loadout that couldn't be read stopped the loadouts menu or a respawn; loadouts trusted the
  permission number saved with each weapon, which moves when weapons are added (upstream a6349406).
- Kick Passengers worked from any distance for a modified client (not an upstream fix).

Requires Python and the .NET 9 SDK; no FiveM runtime.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
vo = (root / 'vMenu' / 'menus' / 'VehicleOptions.cs').read_text(encoding='utf-8-sig')
fc = (root / 'vMenu' / 'FunctionsController.cs').read_text(encoding='utf-8-sig')
mp = (root / 'vMenu' / 'menus' / 'MpPedCustomization.cs').read_text(encoding='utf-8-sig')
cf = (root / 'vMenu' / 'CommonFunctions.cs').read_text(encoding='utf-8-sig')
wl = (root / 'vMenu' / 'menus' / 'WeaponLoadouts.cs').read_text(encoding='utf-8-sig')
server = (root / 'vMenuServer' / 'MainServer.cs').read_text(encoding='utf-8-sig')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


# --- plates ------------------------------------------------------------------------------------
labels = re.findall(r'^\s*(GetLabelText\("(CMOD_PLA_\d+)"\)|"North Yankton"),', member(vo, r'var licensePlates = new List<string> \{.*?\};'), re.M)
labels = [key or 'North Yankton' for _, key in labels]
table = member(vo, r'internal static readonly LicensePlateStyle\[\] PlateStyles =\s*\{.*?\};')
styles = re.findall(r'LicensePlateStyle\.(\w+)', table)
# The game's name for each label (CMOD_PLA_0-4 are LS Customs' "Blue on White 1/2/3, Yellow on Blue,
# Yellow on Black"; upstream 9e4055ba and vMenu Enhanced's fallback text agree).
meaning = {'CMOD_PLA_0': 'BlueOnWhite1', 'CMOD_PLA_1': 'BlueOnWhite2', 'CMOD_PLA_2': 'BlueOnWhite3', 'CMOD_PLA_3': 'YellowOnBlue',
           'CMOD_PLA_4': 'YellowOnBlack', 'North Yankton': 'NorthYankton', 'CMOD_PLA_6': 'ECola', 'CMOD_PLA_7': 'LasVenturas',
           'CMOD_PLA_8': 'LibertyCity', 'CMOD_PLA_9': 'LSCarMeet', 'CMOD_PLA_10': 'LSPanic', 'CMOD_PLA_11': 'LSPounders', 'CMOD_PLA_12': 'Sprunk'}
assert len(labels) == len(styles) == 13, (labels, styles)
assert [meaning[label] for label in labels] == styles, 'every label sets the style it names'
assert len(set(styles)) == 13
client = '\n'.join(p.read_text(encoding='utf-8-sig') for p in (root / 'vMenu').rglob('*.cs') if 'bin' not in p.parts and 'obj' not in p.parts)
assert 'case LicensePlateStyle.' not in client and 'switch (veh.Mods.LicensePlateStyle)' not in client, 'no hand-written mapping is left'
assert 'veh.Mods.LicensePlateStyle = PlateStyles[newIndex];' in vo and vo.count('Array.IndexOf(PlateStyles, veh.Mods.LicensePlateStyle)') == 1
assert 'Array.IndexOf(menus.VehicleOptions.PlateStyles, veh.Mods.LicensePlateStyle)' in fc
print('PASS: plate labels and styles line up, through one table')

# --- eyebrows, arrows --------------------------------------------------------------------------
eyebrows = member(mp, r'value = _random\.Next\(eyebrowsStyleList\.Count\);.*?break;')
assert 'eyebrowsStyle = value;' in eyebrows and 'eyebrowsColor = color;' in eyebrows, 'the randomizer saves the style as the style'
garbled = [str(p.relative_to(root)) for p in (root / 'vMenu').rglob('*.cs')
           if 'bin' not in p.parts and 'obj' not in p.parts and re.search('â[€†]|Ã[\u0080-¿]', p.read_text(encoding='utf-8-sig'))]
assert not garbled, f'text decoded twice: {garbled}'
assert mp.count('"↓ Characters ↓"') == 2 and '"↓ Vehicles ↓"' in (root / 'vMenu' / 'menus' / 'SavedVehicles.cs').read_text(encoding='utf-8-sig')
print('PASS: eyebrows randomized into the right fields; no garbled arrows')

# --- kick passengers range ---------------------------------------------------------------------
kick = member(server, r'^        internal void GetOutOfCar\(.*?^        }$')
assert 'private const float KickPassengersRange = 500f;' in server
owns, rng, seats = kick.index('personalVehicles.Owns('), kick.index('if (distance > KickPassengersRange)'), kick.index('for (int i = -1; i < 15; i++)')
assert owns < rng < seats, 'checked after ownership, before anyone is moved'
assert 'GetPlayerPed(source.Handle)' in kick and 'float.MaxValue' in kick and 'RequestGuard.Malformed(source, request,' in kick[rng:seats]
print('PASS: Kick Passengers only within 500 m of the owner')

# --- compiled: doors and loadouts --------------------------------------------------------------
doors = member(vo, r'^        internal static void ToggleAllVehicleDoors\(int vehicle\).*?^        }$')
key = member(vo, r'MenuKeys\.Add\(VehicleModMenu, "togglemodmenudoors".*?"Toggle Vehicle Doors"\);')
assert 'TryStartVehicleAllDoorsCooldown()' in key and 'ToggleAllVehicleDoors(veh.Handle);' in key and 'GetVehicleDoorAngleRatio' not in key
read = member(cf, r'^        internal static List<ValidWeapon> ReadWeaponLoadout\(.*?^        }$')
perm = member(cf, r'^        internal static Permission WeaponPermission\(ValidWeapon weapon\) =>.*?;$')
loadout_sources = cf + wl + fc
assert re.findall(r'DeserializeObject<List<ValidWeapon>>\((\w+)\)', loadout_sources) == ['json'], 'saved loadouts are only parsed by ReadWeaponLoadout'
assert 'IsAllowed(wp.Perm)' not in cf and 'IsAllowed(w.Perm)' not in cf and cf.count('IsAllowed(WeaponPermission(') == 2
assert wl.count('ReadWeaponLoadout(GetResourceKvpString(') == 2

program = r'''
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Fixes
{
    public enum Permission { WPSpawn, WPPistol, WPKnife, WLMenu }
    public struct ValidWeapon
    {
        public uint Hash;
        public string Name;
        public Dictionary<string, uint> Components;
        public Permission Perm;
        public string SpawnName;
        public int CurrentAmmo;
        public int CurrentTint;
    }
    public static class ValidWeapons
    {
        public static readonly Dictionary<string, Permission> weaponPermissions = new() { ["weapon_pistol"] = Permission.WPPistol, ["weapon_knife"] = Permission.WPKnife };
    }
    public static class Car
    {
        public static bool[] Damaged = new bool[8];
        public static float[] Angle = new float[8];
        public static List<string> Calls = new List<string>();
    }
    public static class Code
    {
        public static List<string> Logged = new List<string>();
        static void Log(string s) => Logged.Add(s);
        static bool IsVehicleDoorDamaged(int v, int d) => Car.Damaged[d];
        static float GetVehicleDoorAngleRatio(int v, int d) => Car.Angle[d];
        static void SetVehicleDoorShut(int v, int d, bool instant) { Car.Calls.Add("shut" + d); Car.Angle[d] = 0; }
        static void SetVehicleDoorOpen(int v, int d, bool loose, bool instant) { Car.Calls.Add("open" + d); Car.Angle[d] = 1; }
''' + doors + '\n' + read + '\n' + perm + r'''
    }

    public static class Program
    {
        static int failures;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

        public static int Main()
        {
            // A bike: no driver's door (0), only... and a car with only the boot open.
            Car.Damaged = new[] { true, true, true, true, false, false, true, true };
            Code.ToggleAllVehicleDoors(1);
            Check(string.Join(",", Car.Calls) == "open4,open5", "no driver's door: the doors it has open");
            Car.Calls.Clear(); Code.ToggleAllVehicleDoors(1);
            Check(string.Join(",", Car.Calls) == "shut4,shut5", "and close again");
            Car.Damaged = new bool[8]; Car.Angle = new float[8]; Car.Angle[5] = 1; Car.Calls.Clear();
            Code.ToggleAllVehicleDoors(1);
            Check(Car.Calls.Count == 8 && Car.Calls.TrueForAll(c => c.StartsWith("shut")), "only the boot open: everything closes (it used to open every door)");
            Car.Damaged[2] = true; Car.Angle[2] = 1; Car.Calls.Clear();
            Code.ToggleAllVehicleDoors(1);
            Check(Car.Calls.Count == 7 && !Car.Calls.Contains("open2") && !Car.Calls.Contains("shut2"), "a broken door that hangs open is left alone and doesn't count");

            Check(Code.ReadWeaponLoadout(null, "x").Count == 0 && Code.ReadWeaponLoadout("", "x").Count == 0 && Code.Logged.Count == 0, "no save: an empty loadout, quietly");
            Check(Code.ReadWeaponLoadout("{}", "the temp loadout").Count == 0 && Code.Logged.Count == 1, "'{}' (what a missing temp loadout read as): empty, logged, no exception");
            Check(Code.ReadWeaponLoadout("[{\"Hash\":1,", "a cut-off save").Count == 0 && Code.Logged.Count == 2, "a cut-off save: empty, logged");
            Check(Code.ReadWeaponLoadout("null", "x").Count == 0, "null: empty");
            var read = Code.ReadWeaponLoadout("[{\"Hash\":0},{\"Hash\":453432689,\"SpawnName\":\"weapon_pistol\",\"Perm\":3,\"Components\":null,\"GetMaxAmmo\":250}]", "a save");
            Check(read.Count == 1 && read[0].Components != null && read[0].Components.Count == 0, "entries without a weapon dropped; missing components read as none");
            Check(Code.WeaponPermission(read[0]) == Permission.WPPistol, "the pistol saved with permission number 3 (now WLMenu) is checked against WPPistol, by name");
            Check(Code.WeaponPermission(new ValidWeapon { SpawnName = "WEAPON_KNIFE", Perm = Permission.WPPistol }) == Permission.WPKnife, "names match whatever their case");
            Check(Code.WeaponPermission(new ValidWeapon { SpawnName = "weapon_removed", Perm = Permission.WPPistol }) == Permission.WPSpawn
                && Code.WeaponPermission(new ValidWeapon()) == Permission.WPSpawn, "a weapon this build doesn't list needs spawn-by-name");
            Console.WriteLine(failures == 0 ? "PASS: door toggle and saved loadouts" : $"{failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
'''

newtonsoft = root / 'dependencies' / 'shared' / 'Newtonsoft.Json.dll'
with tempfile.TemporaryDirectory(prefix='vmenu-upstream-fixes-') as tmp:
    work = Path(tmp)
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'Tests.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable><NoWarn>CS1998;CS0162;CS0414;CS0649;CS0169</NoWarn></PropertyGroup>
  <ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>{newtonsoft}</HintPath></Reference></ItemGroup>
</Project>
''', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'), '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
