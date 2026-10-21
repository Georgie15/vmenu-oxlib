"""Release D client features: noclip controls, personal-vehicle keys, one-box teleport, backup items.

Compiles the production noclip key hint and the Teleport To Coords parser against fakes, then checks
that each feature is wired as intended. Backup and preset logic have their own tests
(tests/save-backups, tests/vehicle-presets).

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
read = lambda rel: (root / rel).read_text(encoding='utf-8-sig')
noclip = read('vMenu/Noclip.cs')
common = read('vMenu/CommonFunctions.cs')
pv = read('vMenu/menus/PersonalVehicle.cs')
fc = read('vMenu/FunctionsController.cs')
misc = read('vMenu/menus/MiscSettings.cs')
exports_lua = read('build/vMenu/client/exports.lua')
backups_lua = read('build/vMenu/client/backups.lua')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, pattern
    return match[0]


def joaat(text):
    h = 0
    for ch in text.lower().encode():
        h = (h + ch) & 0xFFFFFFFF
        h = (h + (h << 10)) & 0xFFFFFFFF
        h ^= h >> 6
    h = (h + (h << 3)) & 0xFFFFFFFF
    h ^= h >> 11
    return (h + (h << 15)) & 0xFFFFFFFF


assert joaat('adder') == 0xB779A091

# --- noclip -------------------------------------------------------------------------------------
handler = member(noclip, r'^        private async Task NoClipHandler\(\).*?^        }$')
assert noclip.count('"CLEAR_ALL"') == 1 and 'BuildHelpPanel();' in handler
assert re.search(r'if \(panelState != helpPanelState\)\s*\{\s*helpPanelState = panelState;\s*BuildHelpPanel\(\);', handler), \
    'help panel rebuilt only when speed or camera mode changes'
assert 'SET_DATA_SLOT' not in handler, 'no per-frame scaleform rebuild'
assert 'xoff * (moveSpeed + 0.3f)' in handler and re.search(r'if \(FollowCamMode\)\s*\{\s*xoff = -0\.5f;', handler), \
    'A/D strafe in follow-cam'
wheel = member(handler, r'if \(!MenuController\.IsAnyMenuOpen\(\)\).*?\n                    }\n')
assert 'CursorScrollUp' in wheel and 'CursorScrollDown' in wheel and 'Math.Min(MovingSpeed + 1, speeds.Count - 1)' in wheel \
    and 'Math.Max(MovingSpeed - 1, 0)' in wheel, 'mouse wheel speed, clamped, only with the menu closed'
for control in ('WeaponWheelNext', 'WeaponWheelPrev', 'SelectNextWeapon', 'SelectPrevWeapon'):
    assert f'DisableControlThisFrame(0, Control.{control})' in handler, f'{control} disabled while noclipping'
key_hint = member(noclip, r'^        private static string KeyMappingButton\(.*?;$')
print('PASS: noclip help panel, strafe and mouse-wheel wiring')

# --- personal vehicle keys and waypoint ---------------------------------------------------------
for command, permission, label in (('PVLock', 'PVLockDoors', 'vMenu PV Lock/Unlock'), ('PVEngine', 'PVToggleEngine', 'vMenu PV Engine')):
    block = member(pv, r'if \(IsAllowed\(Permission\.' + permission + r'\)\)\s*\{\s*RegisterCommand\(\$"vMenu:\{keyMappingId\}:' + command + r'".*?\n            }')
    assert f'RegisterKeyMapping($"vMenu:{{keyMappingId}}:{command}", "{label}", "keyboard", "");' in block, \
        f'{label} is unbound by default and needs {permission}'
assert 'LockOrUnlockDoors(veh, !GetVehicleDoorsLockedForPlayer(veh.Handle, Game.PlayerPed.Handle));' in pv, 'lock key toggles'
assert pv.count('ToggleEngine(') >= 3, 'menu item and key share the engine toggle'
assert 'menu.AddMenuItem(waypointToVehicle);' in pv and 'SetNewWaypoint(LastKnownPosition.Value.X, LastKnownPosition.Value.Y);' in pv
assert re.search(r'personalVehicleMenu\.LastKnownPosition = personalVehicle\.Position;', member(fc, r'^        private async Task AutoPVBlipOnExitTick\(\).*?^        }$')), \
    'last known position kept up to date while the vehicle is in range'
print('PASS: personal vehicle key bindings and waypoint wiring')

# --- teleport and backups -----------------------------------------------------------------------
teleport = member(misc, r'else if \(item == tpToCoord\).*?\n                    }\n')
assert 'await GetUserCoords()' in teleport and 'GetUserInput' not in teleport, 'one dialog for X, Y and Z'
dialog = member(exports_lua, r'exports\("coordsDialog".*?\nend\)')
assert dialog.count('field("') == 3 and 'GetEntityCoords(cache.ped)' in dialog and 'type = "number"' in dialog
assert 'Export/Import Data' not in misc and 'exportData' not in misc, 'the stub is gone'
assert re.search(r'if \(GetConvar\("vmenu_backups", "false"\) == "true"\)\s*\{\s*menu\.AddMenuItem\(backupSaves\);\s*menu\.AddMenuItem\(restoreSaves\);', misc)
for event in ('vMenu:backups:backup', 'vMenu:backups:restore'):
    assert f'"{event}"' in misc and f"AddEventHandler('{event}'" in backups_lua, event
print('PASS: teleport dialog and backup menu wiring')

# --- Vehicle Liveries menu: its handlers used to be added on every open ------------------------
options = read('vMenu/menus/VehicleOptions.cs')
assert options.count('VehicleLiveriesMenu.OnListIndexChange +=') == 1 and options.count('VehicleLiveriesMenu.OnItemSelect +=') == 1, \
    'Liveries handlers registered once'
liveries_open = member(options, r'if \(item == liveriesMenuBtn\).*?\n                }\n')
assert '+=' not in liveries_open, 'nothing subscribed when the Liveries menu opens'
print('PASS: Liveries menu handlers registered once')

# --- compiled: key hint and coordinates parser --------------------------------------------------
coords = member(common, r'^        public static async Task<Vector3\?> GetUserCoords\(\).*?^        }$')
expected = 'INPUT_%X' % (joaat('vMenu:Default:NoClip') | 0x80000000)
program = r'''
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

struct Vector3 { public float X, Y, Z; public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; } }

static class Program
{
    static int GetHashKey(string s)
    {
        uint h = 0;
        foreach (var ch in s.ToLowerInvariant()) { h += ch; h += h << 10; h ^= h >> 6; }
        h += h << 3; h ^= h >> 11; h += h << 15;
        return unchecked((int)h);
    }

    class FakeExternal { public string Reply; public Task<string> GetCoordsInput() => Task.FromResult(Reply); }
    static readonly FakeExternal External = new FakeExternal();

    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }

    static async Task Main()
    {
        Check(KeyMappingButton("vMenu:Default:NoClip") == "~EXPECTED~", "key hint: " + KeyMappingButton("vMenu:Default:NoClip"));

        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // a comma-decimal locale
        External.Reply = "123.456,-78.5,30.000";
        var c = await GetUserCoords();
        Check(c.HasValue && c.Value.X == 123.456f && c.Value.Y == -78.5f && c.Value.Z == 30f, "parses the dialog's x,y,z in any locale");
        foreach (var bad in new[] { "", null, "1,2", "1,2,3,4", "a,b,c" })
        {
            External.Reply = bad;
            Check(await GetUserCoords() == null, "rejects '" + bad + "'");
        }
        Console.WriteLine("PASS: noclip key hint and coordinates parser");
    }
'''.replace('EXPECTED', expected) + key_hint + '\n' + coords + '\n}'

with tempfile.TemporaryDirectory(prefix='vmenu-client-features-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><InvariantGlobalization>false</InvariantGlobalization></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
