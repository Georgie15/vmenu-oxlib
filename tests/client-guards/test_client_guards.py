"""Release E client guards: ped pack access, unlimited ammo, expanded radar, death notifications, tidy-ups.

Compiles the production ped pack check (PlayerAppearance.IsLockedPedPackModel) and the unlimited
ammo block (FunctionsController.WeaponOptions, with the real OverrideLatch) against fakes, then
checks the wiring of the other fixes.

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
read = lambda rel: (root / rel).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
common = read('vMenu/CommonFunctions.cs')
appearance = read('vMenu/menus/PlayerAppearance.cs')
events = read('vMenu/EventManager.cs')
fc = read('vMenu/FunctionsController.cs')
cfg = read('build/vMenu/config/permissions.cfg')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, pattern
    return match[0]


# --- fix 1: ped pack access, however the ped change is reached --------------------------------
skin = member(common, r'^        public static async Task SetPlayerSkin\(uint modelHash.*?^        }$')
assert skin.index('IsModelAPed(modelHash)') < skin.index('PlayerAppearance.IsLockedPedPackModel(modelHash)') < skin.index('LoadModelAsync(modelHash)'), \
    'SetPlayerSkin refuses a locked ped pack model before loading it'
pack = member(events, r'^        private void SetPedPackPeds\(\).*?^        }$')
assert 'PlayerAppearance.PedPackModelHashes.Clear();' in pack and 'PlayerAppearance.PedPackModelHashes.Add((uint)GetHashKey(modelName));' in pack
assert re.search(r'if \(item == spawnByNameBtn\).*?await SetPlayerSkin\(model,', appearance, re.S), 'Spawn By Name goes through SetPlayerSkin'
print('PASS: ped pack wiring (SetPlayerSkin, pedpack.json hashes, Spawn By Name)')

# --- fix 4: expanded radar --------------------------------------------------------------------
misc = member(fc, r'^        private async Task MiscSettings\(\).*?^        }$')
assert 'if (vMenuOpenedBigmap && (!IsBigmapActive() || GetGameTimer() - radarSwitchTimer > 8000))' in misc, \
    'only an expanded radar vMenu opened is closed after 8 s'
assert misc.count('vMenuOpenedBigmap = true;') == 1 and misc.count('vMenuOpenedBigmap = false;') == 2
assert misc.count('SetBigmapActive(false, false);') == 2, 'no other path closes the expanded radar'

# --- fix 5 and 6 --------------------------------------------------------------------------------
deaths = member(fc, r'^        private async Task DeathNotifications\(\).*?^        }$')
assert deaths.rstrip().endswith('await Delay(250);\n        }') and 'Task.FromResult' not in deaths, 'death notifications check 4 times a second'
options = member(fc, r'^        private async Task PlayerOptions\(\).*?^        }$')
assert 'if (MainMenu.PlayerOptionsMenu.PlayerInvisible && IsAllowed(Permission.POInvisible) && GetSettingsBool(Setting.vmenu_handle_invisibility))' in options, \
    'the convar is only read while invisibility is on'
for convar in ('vmenu_disable_ai', 'vmenu_auto_ban_cheaters', 'vmenu_log_ban_actions', 'vmenu_log_kick_actions', 'vmenu_default_ban_message_information'):
    assert convar not in cfg, f'{convar} is gone from permissions.cfg (nothing reads it)'
assert '.InfiniteAmmo =' not in fc, 'unlimited ammo is not written every frame'
print('PASS: expanded radar, death notification rate, convar order, dead convars')

# --- compiled: ped pack check and unlimited ammo ----------------------------------------------
locked = member(appearance, r'^        internal static bool IsLockedPedPackModel\(uint model\).*?^        }$')
weapons = member(fc, r'^        private async Task WeaponOptions\(\).*?^        }$')
ammo = member(weapons, r'^            // Unlimited ammo:.*?^            }\n            if \(applyAmmo\)\n            \{\n.*?\n            }\n')
latch = (root / 'vMenu/OverrideLatch.cs').read_text(encoding='utf-8-sig')

program = r'''
using System;
using System.Collections.Generic;
using vMenuClient;

enum Permission { PAPedPack, WPUnlimitedAmmo }
enum WeaponHash : uint { Unarmed = 0xA2719263 }
class Ped { public int Handle = 7; }
static class Game { public static Ped PlayerPed = new Ped(); }
class WeaponMenu { public bool UnlimitedAmmo; }
static class MainMenu { public static WeaponMenu WeaponOptionsMenu = new WeaponMenu(); }

static class Program
{
    static readonly HashSet<Permission> allowed = new HashSet<Permission>();
    static bool IsAllowed(Permission permission) => allowed.Contains(permission);
    static int prompts;
    static void ShowPedPackStorePrompt() { prompts++; }
    public static readonly HashSet<uint> PedPackModelHashes = new HashSet<uint>();

    static int selected;
    static readonly List<string> calls = new List<string>();
    static int GetSelectedPedWeapon(int ped) => selected;
    static void SetPedInfiniteAmmo(int ped, bool on, uint weapon) { calls.Add($"{weapon}:{on}"); }
    static readonly OverrideLatch unlimitedAmmoLatch = new OverrideLatch();
    static void Frame()
    {
''' + ammo + r'''
    }

    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }

    static void Main()
    {
        PedPackModelHashes.Add(111);
        Check(IsLockedPedPackModel(111) && prompts == 1, "a pack ped without the pack is refused, with the store prompt");
        Check(!IsLockedPedPackModel(222) && prompts == 1, "other peds are not affected");
        allowed.Add(Permission.PAPedPack);
        Check(!IsLockedPedPackModel(111) && prompts == 1, "with the pack it spawns");
        Console.WriteLine("PASS: ped pack check");

        allowed.Add(Permission.WPUnlimitedAmmo);
        selected = 500;
        Frame();
        Check(calls.Count == 0, "option off: never touched (other scripts' infinite ammo survives)");
        MainMenu.WeaponOptionsMenu.UnlimitedAmmo = true;
        Frame(); Frame();
        Check(string.Join(",", calls) == "500:True,500:True", "option on: the weapon in hand is kept infinite");
        calls.Clear();
        selected = 600;
        Frame();
        Check(string.Join(",", calls) == "500:False,600:True", "switching weapon puts the last one back once");
        calls.Clear();
        selected = unchecked((int)WeaponHash.Unarmed);
        Frame();
        Check(calls.Count == 0, "unarmed: nothing to apply");
        MainMenu.WeaponOptionsMenu.UnlimitedAmmo = false;
        Frame(); Frame();
        Check(string.Join(",", calls) == "600:False", "option off: the last weapon is put back once, then left alone");
        calls.Clear();
        MainMenu.WeaponOptionsMenu.UnlimitedAmmo = true;
        allowed.Remove(Permission.WPUnlimitedAmmo);
        selected = 700;
        Frame();
        Check(calls.Count == 0, "without the permission nothing is applied");
        Console.WriteLine("PASS: unlimited ammo is enforced while on and put back once");
    }
''' + locked + '\n}\n'

with tempfile.TemporaryDirectory(prefix='vmenu-client-guards-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'OverrideLatch.cs').write_text(latch, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
