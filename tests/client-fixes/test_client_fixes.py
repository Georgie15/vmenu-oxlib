"""Release C client fixes: the streaming timeout helper, plus source checks for the other fixes.

Compiles the production WaitForStreamingAsync / LoadAnimDictAsync / LoadModelAsync from
vMenu/CommonFunctions.cs against fake natives, then checks that the fixed call sites use them and
that the Extras menu, teleport, spawner, clothing glow and blip changes are wired as intended.

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
read = lambda rel: (root / rel).read_text(encoding='utf-8-sig')
common = read('vMenu/CommonFunctions.cs')
fc = read('vMenu/FunctionsController.cs')
mp = read('vMenu/menus/MpPedCustomization.cs')
vo = read('vMenu/menus/VehicleOptions.cs')
vs = read('vMenu/menus/VehicleSpawner.cs')


def member(source, pattern):
    return re.search(pattern, source, re.M | re.S)[0]


# --- wiring -----------------------------------------------------------------------------------
assert common.count('while (!HasAnimDictLoaded(') == 0, 'CommonFunctions: anim dictionary waits use LoadAnimDictAsync'
assert common.count('while (!HasModelLoaded(') == 1, 'CommonFunctions: only the vehicle LoadModel (already timed) polls directly'
skin = member(common, r'^        public static async Task SetPlayerSkin\(uint modelHash.*?^        }$')
assert skin.index('LoadModelAsync(modelHash)') < skin.index('vMenu:PedChange:Before'), 'SetPlayerSkin loads the model before touching weapons'
assert fc.count('while (!HasAnimDictLoaded(') == 0, 'FunctionsController: anim dictionary waits use LoadAnimDictAsync'
assert mp.count('while (!HasModelLoaded(') == 0, 'MP characters: model waits use LoadModelAsync'
assert 'GetGameTimer() - ammoTimer < 1000' in common, 'loadout ammo loop is bounded'

assert vo.count('VehicleComponentsMenu.OnItemSelect +=') == 1, 'Extras Go Back handler registered once, not on every open'

teleport = member(common, r'^        private static async Task TeleportToCoordsCore\(.*?^        }$')
loop = teleport.index('for (var zz = 950.0f;')
assert teleport.count('NewLoadSceneStart(') == 1 and teleport.index('NewLoadSceneStart(') < loop, 'teleport loads the scene once, before probing heights'

assert 'void PopulateClass()' in vs and vs.index('void PopulateClass()') < vs.index('foreach (var veh in VehicleData.Vehicles.VehicleClasses[className])'), \
    'vehicle spawner builds each class on first open'
assert '[vehClass]' not in vs.split('void PopulateClass()')[1].split('vehicleClassMenu.ShowVehicleStatsPanel')[0], \
    'the deferred builder uses the copied class index, not the loop variable'

assert 'private void DrawMiscSettingsText()' in fc, 'misc settings text is no longer an async void every frame'
assert 'DecorSetInt(Game.PlayerPed.Handle, clothingAnimationDecor' not in fc, 'own clothing decorator only written on change'
blips = member(fc, r'^        private async Task PlayerBlipsControl\(.*?^        }$')
assert blips.count('SetBlipNameToPlayerName(') == 1 and blips.index('if (style.Sprite != blipSprite)') < blips.index('SetBlipNameToPlayerName('), \
    'blip name/colour/category set when the sprite is new, not every frame'
print('PASS: wiring (load timeouts, Extras menu, teleport, spawner, glow, blips)')

# PSRP_DeathScript's fake death: a dead ped is alive again ~1.5 s after the fall, so the after-death
# restore (which swaps the model) must also wait for the `dead` state bag to clear.
restore = member(fc, r'^        private async Task RestorePlayerAfterBeingDead\(.*?^        }$')
assert 'while (Game.PlayerPed.IsDead || IsLogicallyDead() ||' in restore, 'after-death restore waits for the logical death'
logical = member(fc, r'^        private static bool IsLogicallyDead\(.*?^        }$')
assert 'Game.Player.State["dead"]' in logical and 'is bool flag' in logical and '== 1' in logical, \
    'IsLogicallyDead reads the dead state bag as true or 1'
print('PASS: after-death restore waits for PSRP_DeathScript')

# --- streaming helper behaviour ---------------------------------------------------------------
helper = member(common, r'^        internal static async Task<bool> WaitForStreamingAsync\(.*?^        }$')
wrappers = '\n'.join(re.findall(r'^        internal static Task<bool> Load(?:AnimDict|Model)Async\(.*?;$', common, re.M | re.S))
assert 'LoadAnimDictAsync' in wrappers and 'LoadModelAsync' in wrappers

program = r'''
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

static class Program
{
    static int now, requests, frames;
    static int readyAt = int.MaxValue;
    static readonly List<string> logged = new List<string>();
    static int GetGameTimer() => now;
    static Task Delay(int ms) { frames++; now += ms == 0 ? 16 : ms; return Task.CompletedTask; }
    static class Debug { public static void WriteLine(string text) { logged.Add(text); } }
    static bool HasAnimDictLoaded(string dict) => now >= readyAt;
    static void RequestAnimDict(string dict) { requests++; }
    static bool HasModelLoaded(uint model) => now >= readyAt;
    static void RequestModel(uint model) { requests++; }

    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }
    static void Reset(int ready) { now = 0; requests = 0; frames = 0; readyAt = ready; logged.Clear(); }

    static async Task Main()
    {
        Reset(int.MaxValue);
        Check(!await LoadAnimDictAsync("never"), "a dictionary that never loads gives up");
        Check(now >= 5000 && now < 5100 && requests == 1 && logged.Count == 1, "anim timeout is 5 s, requested once, logged");

        Reset(int.MaxValue);
        Check(!await LoadModelAsync(1), "a model that never loads gives up");
        Check(now >= 15000 && now < 15100, "model timeout is 15 s");

        Reset(300);
        Check(await LoadAnimDictAsync("later") && now >= 300 && logged.Count == 0, "loads once streaming finishes");

        Reset(0);
        Check(await LoadModelAsync(2) && requests == 0 && frames == 0, "already loaded: no request, no wait");

        Reset(int.MaxValue);
        Check(!await LoadModelAsync(3, 5000) && now < 5100, "custom timeout respected");
        Console.WriteLine("PASS: streaming helper (timeout, late load, already loaded, custom timeout)");
    }
''' + helper + '\n' + wrappers + '\n}'

with tempfile.TemporaryDirectory(prefix='vmenu-client-fixes-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
