"""Releases L and M: vMenu's loops sleep while they have nothing to do, as vMenu Enhanced's do.

After release J, two vMenu loops still ran every frame with every menu closed, and about 15 more woke
4 to 10 times a second only to find their options still off. Release L:

- puts the menu watcher (MainMenu.OnTick) to sleep while every menu is closed. MenuAPI's
  MenuTicks.Changed, raised when its menu loops start, wakes it, and does the first open frame's
  work right away, so the character creator's back button guard is set before MenuAPI reads input;
- starts the death save from the game's damage event (CEventNetworkEntityDamage) instead of checking
  every frame, with a 100 ms fallback check (vMenu Enhanced's rate) for deaths no event reports;
- gave loops whose only reasons to run are menu options a 2 s rest while those are all off and
  every menu is closed.

Release M takes those 17 loops off the tick list instead (TickSleeper), as vMenu Enhanced's loops stop
while their feature is off, so a sleeping loop costs nothing at all. Anything that ends the idle rests
(IdleRest.WakeAll: a menu opening, new permissions, the player blips and names integration events, a
new personal vehicle, the minimap key, the pause menu's expanded radar) raises IdleRest.Woken, which
puts every sleeping loop back; and GeneralTasks, every 100 ms, wakes each one whose own "wanted" test
(cheap reads of options and permissions) now says it has work.

Compiled checks run the production watcher and death code against a fake game. Source checks cover
the rests and the wake-ups. tests/idle-rests still compares the main loops frame by frame with
build-2026-11-17-3, and tests/key-bindings checks the minimap key's wake-up.

Requires Python and the .NET 9 SDK; no FiveM runtime.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
fc = (root / 'vMenu' / 'FunctionsController.cs').read_text(encoding='utf-8-sig')
mm = (root / 'vMenu' / 'MainMenu.cs').read_text(encoding='utf-8-sig')
sleeper = (root / 'vMenu' / 'TickSleeper.cs').read_text(encoding='utf-8-sig')
idle = (root / 'vMenu' / 'IdleRest.cs').read_text(encoding='utf-8-sig')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


def method(source, name):
    return member(source, r'^        (?:private|internal|public)[^\n(]*\b' + name + r'\([^)]*\)\s*\n        \{.*?^        \}$')


# --- source checks: which loops sleep, and what wakes them --------------------------------------
# Each loop, the field holding its sleeper and the "wanted" test it is registered with.
SLEEPERS = {
    'PlayerOptions': ('playerOptionsSleeper', 'PlayerOptionsWanted'),
    'DoPlayerAndVehicleChecks': ('protectionsSleeper', 'ProtectionsWanted'),
    'WeatherOptions': ('weatherOptionsSleeper', '() => AnyMenuOpen'),
    'HudOverlayTick': ('hudSleeper', 'HudWanted'),
    'UpdateLocation': ('locationSleeper', '() => MainMenu.MiscSettingsMenu?.ShowLocation == true'),
    'MiscSettings': ('miscSettingsSleeper', 'MiscSettingsWanted'),
    'MiscRecordingKeybinds': ('recordingKeysSleeper', '() => MainMenu.MiscSettingsMenu?.KbRecordKeys == true'),
    'DeathNotifications': ('deathNotificationsSleeper', '() => MainMenu.MiscSettingsMenu?.DeathNotifications == true'),
    'WeaponOptions': ('weaponOptionsSleeper', 'WeaponOptionsWanted'),
    'ManageCamera': ('cameraSleeper', '() => camera != null || AnyMenuOpen'),
    'ModelDrawDimensions': ('outlinesDrawSleeper', 'OutlinesWanted'),
    'SlowMiscTick': ('outlinesScanSleeper', 'OutlinesWanted'),
    'PlayerOverheadNamesControl': ('namesSleeper', '() => MainMenu.MiscSettingsMenu?.MiscShowOverheadNames == true'),
    'SnowballPickupHelpMessageTask': ('snowballSleeper', '() => EventManager.IsSnowEnabled'),
    'PersonalVehicleAutoLockTick': ('pvAutoLockSleeper', 'PersonalVehicleWanted'),
    'AutoPVBlipOnExitTick': ('pvBlipSleeper', 'PersonalVehicleWanted'),
    'PersonalVehicleOptions': ('pvOptionsSleeper', 'PersonalVehicleWanted'),
}
assert 'SleepRestMs' not in fc, 'no long rests are left (release M sleeps instead)'
WS = r'\s*'
GUARDS = {
    'PlayerOptions': r'if \(AnyMenuOpen \|\| \(MainMenu\.PlayerOptionsMenu\.PlayerSuperJump && IsAllowed\(Permission\.POSuperjump\)\)\)\s*\{\s*return;\s*\}\s*'
                     r'if \(PlayerOptionsWanted\(\)\)\s*\{\s*await Rest\(IdleRestMs\);\s*\}\s*else\s*\{\s*playerOptionsSleeper\.Sleep\(\);',
    'DoPlayerAndVehicleChecks': r'if \(!AnyMenuOpen && !ProtectionsWanted\(\)\)\s*\{\s*protectionsSleeper\.Sleep\(\);',
    'WeatherOptions': r'if \(!AnyMenuOpen\)\s*\{\s*weatherOptionsSleeper\.Sleep\(\);\s*return;',
    'HudOverlayTick': r'if \(!AnyMenuOpen\)\s*\{\s*hudSleeper\.Sleep\(\);',
    'UpdateLocation': r'if \(AnyMenuOpen\)\s*\{\s*await Delay\(1000\);\s*\}\s*else\s*\{\s*locationSleeper\.Sleep\(\);',
    # The expanded radar is held 4 times a second, not left to sleep.
    'MiscSettings': r'if \(AnyMenuOpen \|\| misc\.HideRadar \|\| misc\.LockCameraX \|\| misc\.LockCameraY \|\| hideStreetName\)\s*\{\s*return;\s*\}\s*'
                    r'if \(bothSpeeds\)\s*\{\s*await Rest\(StateRestMs\);\s*\}\s*'
                    r'else if \(expandedRadarProfile \|\| vMenuOpenedBigmap\)\s*\{\s*await Rest\(IdleRestMs\);\s*\}\s*else\s*\{\s*miscSettingsSleeper\.Sleep\(\);',
    'MiscRecordingKeybinds': r'if \(!AnyMenuOpen\)\s*\{\s*recordingKeysSleeper\.Sleep\(\);',
    'DeathNotifications': r'if \(MainMenu\.MiscSettingsMenu\.DeathNotifications \|\| AnyMenuOpen\)\s*\{\s*await Delay\(250\);\s*\}\s*else\s*\{\s*deathNotificationsSleeper\.Sleep\(\);',
    'WeaponOptions': r'if \(AnyMenuOpen \|\| unlimitedAmmo\)\s*\{\s*return;\s*\}\s*'
                     r'if \(WeaponOptionsWanted\(\)\)\s*\{\s*await Rest\(IdleRestMs\);\s*\}\s*else\s*\{\s*weaponOptionsSleeper\.Sleep\(\);',
    'ManageCamera': r'if \(!AnyMenuOpen && camera == null\)\s*\{\s*cameraSleeper\.Sleep\(\);',
    'ModelDrawDimensions': r'if \(!AnyMenuOpen\)\s*\{\s*outlinesDrawSleeper\.Sleep\(\);',
    'SlowMiscTick': r'if \(!AnyMenuOpen\)\s*\{\s*outlinesScanSleeper\.Sleep\(\);',
    'PlayerOverheadNamesControl': r'if \(MainMenu\.MiscSettingsMenu != null && !MainMenu\.MiscSettingsMenu\.MiscShowOverheadNames && !AnyMenuOpen\)\s*\{\s*namesSleeper\.Sleep\(\);',
    'SnowballPickupHelpMessageTask': r'if \(EventManager\.IsSnowEnabled\)\s*\{\s*await Delay\(100\);\s*\}\s*else\s*\{\s*snowballSleeper\.Sleep\(\);',
    'PersonalVehicleAutoLockTick': r'if \(personalVehicleMenu\?\.CurrentPersonalVehicle == null && !AnyMenuOpen\)\s*\{\s*pvAutoLockSleeper\.Sleep\(\);\s*return;',
    'AutoPVBlipOnExitTick': r'if \(personalVehicle == null\)\s*\{\s*// SetCurrentPersonalVehicle wakes it\.\s*pvBlipSleeper\.Sleep\(\);',
    'PersonalVehicleOptions': r'if \(veh == null && !AnyMenuOpen\)\s*\{\s*pvOptionsSleeper\.Sleep\(\);',
}
assert set(GUARDS) == set(SLEEPERS)
setup = method(fc, 'SetupTickFunctions')
for name, (field, wanted) in SLEEPERS.items():
    assert f'{field} = AddSleepingLoop(nameof({name}), {name}, {wanted});' in setup, f'{name} is registered as a sleeping loop'
    assert f'Tick += {name};' not in setup, f'{name} is registered once'
    body = method(fc, name)
    assert body.count(f'{field}.Sleep();') == (2 if name == 'AutoPVBlipOnExitTick' else 1), f'{name} sleeps in one place'
    # The exact guard each loop sleeps behind. Never while a menu is open: options change there, and
    # every loop must see them on the next frame. Two have no option a menu could switch on while they
    # sleep, snow (the server's weather) and the personal vehicle; GeneralTasks' 100 ms check wakes
    # them when either appears.
    assert re.search(GUARDS[name], body), f'{name} sleeps only behind its guard'
sleeping = {m.group(1) for m in re.finditer(r'^        private async Task (\w+)\(\)\s*\n        \{(?:(?!^        \}$).)*?Sleeper\.Sleep\(\)', fc, re.M | re.S)}
assert sleeping == set(SLEEPERS), f'only these loops sleep: {sorted(sleeping ^ set(SLEEPERS))}'
# These watch the game, not just options, so they keep their shorter rests.
assert 'await Rest(IdleRestMs);' in method(fc, 'RadarSettings') and 'await Rest(IdleRestMs);' in method(fc, 'VoiceChat')
assert 'await Delay(500);' in method(fc, 'PlayerBlipsControl') and 'Sleep()' not in method(fc, 'PerFrameChecks')
assert 'await Rest(StateRestMs);' in method(fc, 'GeneralTasks')

# What wakes them.
assert 'public static void MenuOpened() => WakeAll();' in idle and 'public static event System.Action Woken;' in idle
wake_all = method(idle, 'WakeAll')
assert wake_all.index('Woken?.Invoke();') < wake_all.index('if (resting.Count == 0)'), 'Woken is raised even with no rest in progress'
assert 'IdleRest.Woken += () => WakeSleepingLoops(true);' in method(fc, 'WireSleepingLoops')
assert setup.index('WireSleepingLoops();') < setup.index('AddSleepingLoop('), 'wired before the first loop'
general = method(fc, 'GeneralTasks')
assert 'WakeSleepingLoops(false);' in general, 'GeneralTasks wakes the loops with work every 100 ms'
perms = method(mm, 'SetPermissions')
assert perms.index('vMenuShared.PermissionsManager.SetPermissions(permissionsList);') < perms.index('IdleRest.WakeAll();'), 'new permissions wake them'
integrations = (root / 'vMenu' / 'Integrations.cs').read_text(encoding='utf-8-sig')
assert integrations.count('IdleRest.WakeAll();') == 2, 'both integration events wake them'
pv = method((root / 'vMenu' / 'menus' / 'PersonalVehicle.cs').read_text(encoding='utf-8-sig'), 'SetCurrentPersonalVehicle')
assert pv.rstrip().endswith('IdleRest.WakeAll();\n        }'), 'a new personal vehicle wakes the personal vehicle loops'
radar = method(fc, 'RadarSettings')
assert re.search(r'if \(expanded && !expandedRadarProfile\)\s*\{[^}]*IdleRest\.WakeAll\(\);', radar), 'the pause menu expanded radar wakes MiscSettings'
assert 'IdleRest.WakeAll();' in method(fc, 'OnMinimapReleased')
# DoPlayerAndVehicleChecks lost its six spacing waits.
checks = method(fc, 'DoPlayerAndVehicleChecks')
assert checks.count('await Delay(100);') == 0 and 'await Delay(1000);' in checks
assert 'RegisterCommand("vmenu_ticks"' in method(fc, 'WireSleepingLoops'), 'vmenu_ticks lists which loops are asleep'
# Review fixes: set up once (a second run would register every loop twice, and a sleep removes one copy).
once = re.search(r'if \(tickFunctionsSetUp\)\s*\{\s*return;\s*\}\s*tickFunctionsSetUp = true;', setup)
assert once and once.start() < setup.index('RegisterKeyBindings();'), 'SetupTickFunctions runs once'
# Noclip ending: god mode is not dropped, an invisible player on foot stays invisible, and the loops holding
# vMenu's options check again on the next frame rather than after their 250 ms rest.
noclip = (root / 'vMenu' / 'Noclip.cs').read_text(encoding='utf-8-sig')
handler = method(noclip, 'NoClipHandler')
assert re.search(r'if \(!\(onFoot && playerOptions != null && playerOptions\.PlayerGodMode\s*&& vMenuShared\.PermissionsManager\.IsAllowed\(vMenuShared\.PermissionsManager\.Permission\.POGod\)\)\)\s*\{\s*SetEntityInvincible\(noclipEntity, false\);', handler)
assert 'if (playerOptions == null || !playerOptions.PlayerInvisible || !onFoot' in handler, 'the visibility reset matches its comment'
assert handler.index('while (NoclipActive)') < handler.index('IdleRest.WakeAll();') < handler.index('await Task.FromResult(0);'), 'noclip ending wakes the option loops'
print(f'PASS: {len(SLEEPERS)} option-only loops sleep off the tick list; a menu, permissions, integrations, a personal vehicle, '
      'the radar and GeneralTasks\' 100 ms check wake them')

# --- source checks: the watcher and the death save ---------------------------------------------
assert 'menuWatcher = new TickSleeper(OnTick, t => Tick += t, t => Tick -= t);' in mm and 'MenuTicks.Changed += OnMenuTicksChanged;' in mm
assert 'MainMenu.EnsureMenuWatcherAwake();' in method(fc, 'GeneralTasks'), 'a missed MenuAPI event is caught within 100 ms'
menuapi = (root / 'build' / 'vMenu' / 'MenuAPI.dll').read_bytes()
assert b'MenuTicks' in menuapi and b'add_Changed' in menuapi, 'the shipped MenuAPI has MenuTicks.Changed'
assert 'RegisterDeathEvents();\n                Tick += RestorePlayerAfterBeingDead;' in fc.replace('\r\n', '\n')
assert 'EventHandlers["gameEventTriggered"] += new Action<string, List<object>>(OnGameEvent);' in method(fc, 'RegisterDeathEvents')
restore = method(fc, 'SaveAndRestoreAroundDeath')
save_weapons = restore.index('SaveWeaponLoadout("vmenu_temp_weapons_loadout_before_respawn")')
wait_dead = restore.index('while (!Game.PlayerPed.IsDead && !IsLogicallyDead() && GetGameTimer() < deadBy)')
wait_alive = restore.index('while (Game.PlayerPed.IsDead || IsLogicallyDead() || IsScreenFadedOut() || IsScreenFadingOut())')
assert save_weapons < wait_dead < wait_alive, 'saved first, then waits for death (from "dying"), then for the respawn'
assert 'if (!Game.PlayerPed.IsDead && !IsLogicallyDead())\n                {\n                    return;' in restore.replace('\r\n', '\n'), 'restores nothing if the ped never died'
print('PASS: the watcher sleeps on MenuAPI\'s event with a 100 ms safety net; the death save starts from the damage event')

# --- compiled checks ---------------------------------------------------------------------------
watcher_fields = member(mm, r'^        private static bool _wasMenuOpen = false;$') + '\n' + member(mm, r'^        private static TickSleeper menuWatcher;$')
watcher = '\n'.join(method(mm, n) for n in ('OnTick', 'OnMenuTicksChanged', 'EnsureMenuWatcherAwake', 'WatchMenus'))
death = '\n'.join(member(fc, p) for p in (
    r'^        private const int DeathCheckMs = 100;$', r'^        private bool deathInProgress;$'))
death += '\n' + '\n'.join(method(fc, n) for n in ('OnGameEvent', 'HandlePlayerDeathSafely', 'RestorePlayerAfterBeingDead', 'HandlePlayerDeath'))
sleeper_class = sleeper[sleeper.index('    internal sealed class TickSleeper'):sleeper.rindex('}')]
registry = fc[fc.index('        private sealed class SleepingLoop'):fc.index('        // What each sleeping loop needs to run for with every menu closed.')]
idle_rest = idle

program = r'''
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using vMenuClient;

namespace Sleep
{
''' + sleeper_class + r'''
    public class Menu { public bool Visible; }
    public static class W
    {
        public static Menu Current;
        public static bool Dead, Dying, BackReleased;
        public static int Closed, Opened, KeyFrames, Offers, Saves, Delays;
        public static List<string> Events = new List<string>(), Alerts = new List<string>();
        public static TaskCompletionSource<bool> SaveRunning;
    }
    public static class MenuController
    {
        public static bool IsAnyMenuOpen() => W.Current != null;
        public static void CloseAllMenus() { if (W.Current != null) W.Current.Visible = false; W.Current = null; W.Closed++; }
    }
    public static class IdleRest { public static void MenuOpened() => W.Opened++; }
    public static class MenuKeys { public static void OnFrame(Menu m) => W.KeyFrames++; }
    public enum Control { PhoneCancel }
    public static class Game { public static bool IsDisabledControlJustReleased(int g, Control c) => W.BackReleased; }
    public static class Notify { public static void Alert(string s) => W.Alerts.Add(s); }
    public static class Debug { public static void WriteLine(string s) => throw new Exception(s); }
    public class MpMenus
    {
        public Menu appearanceMenu = new Menu(), faceShapeMenu = new Menu(), createCharacterMenu = new Menu(), inheritanceMenu = new Menu(),
            propsMenu = new Menu(), clothesMenu = new Menu(), tattoosMenu = new Menu();
    }
    public static class MpPedCustomization { public static bool DisableBackButton, DontCloseMenus; }

    public class MainMenu
    {
        public static bool ConfigOptionsSetupComplete;
        public static MpMenus MpPedCustomizationMenu = new MpMenus();
        public static object MiscSettingsMenu = new object();
        static int PlayerPedId() => 7;
        static bool IsEntityDead(int ped) => W.Dead;
        static Menu GetOpenMenu() => W.Current;
        static void TriggerEvent(string name, params object[] args) => W.Events.Add(name + ":" + args[0]);
        static void OfferToUpdateSavedVehicle() => W.Offers++;
        static Task Delay(int ms) { W.Delays++; return Task.CompletedTask; }
''' + watcher_fields + '\n' + watcher + r'''
        public List<Func<Task>> Ticks = new List<Func<Task>>();
        public void Start()
        {
            menuWatcher = new TickSleeper(OnTick, t => Ticks.Add(t), t => Ticks.Remove(t));
            Ticks.Add(OnTick);
        }
        public void Frame() { foreach (var t in Ticks.ToArray()) if (!t().Wait(2000)) throw new Exception("FAIL a frame never finished"); }
        public static void Changed() => OnMenuTicksChanged();
        public static void SafetyNet() => EnsureMenuWatcherAwake();
        public static bool Asleep => menuWatcher.Asleep;
    }

    public class Deaths
    {
        static int PlayerPedId() => 7;
        static bool IsEntityDead(int ped) => W.Dead;
        static bool IsPedDeadOrDying(int ped, bool p1) => W.Dead || W.Dying;
        static Task Delay(int ms) { W.Delays++; return Task.CompletedTask; }
        Task SaveAndRestoreAroundDeath() { W.Saves++; W.SaveRunning = new TaskCompletionSource<bool>(); return W.SaveRunning.Task; }
''' + death + r'''
        public void Event(string name, params object[] args) => OnGameEvent(name, new List<object>(args));
        public Task Fallback() => RestorePlayerAfterBeingDead();
        public bool InProgress => deathInProgress;
    }

}

// FunctionsController's sleeping loops, with the real TickSleeper and IdleRest, on a fake tick list.
// Its own namespace, so IdleRest is the real one (Sleep has a fake for the watcher).
namespace SleepLoops
{
    using TickSleeper = Sleep.TickSleeper;
    using vMenuClient;

    public class Loops
    {
        public event Func<Task> Tick;
        public int Registered => Tick?.GetInvocationList().Length ?? 0;
        public readonly Dictionary<string, Action> Commands = new Dictionary<string, Action>();
        void RegisterCommand(string name, Action<int, List<object>, string> handler, bool restricted) => Commands[name] = () => handler(0, new List<object>(), name);
        public static readonly List<string> Printed = new List<string>();
        static class Debug { public static void WriteLine(string s) => Printed.Add(s); }
''' + registry + r'''
        public bool Wanted;
        public int Runs;
        TickSleeper sleeper;
        Task Loop() { Runs++; if (!Wanted) { sleeper.Sleep(); } return Task.CompletedTask; }
        Task Other() => Task.CompletedTask;
        public void Start()
        {
            WireSleepingLoops();
            WireSleepingLoops();
            sleeper = AddSleepingLoop("Loop", Loop, () => Wanted);
            AddSleepingLoop("Other", Other, () => false);
        }
        public void Frame() { foreach (Func<Task> t in Tick?.GetInvocationList() ?? new Delegate[0]) { t(); } }
        public void Check100ms() => WakeSleepingLoops(false);
        internal TickSleeper First => sleeper;
    }
}

namespace Sleep
{
    public static class Program
    {
        static int failures;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

        public static int Main()
        {
            var m = new MainMenu();
            m.Start();
            m.Frame(); m.Frame();
            Check(m.Ticks.Count == 1 && !MainMenu.Asleep, "before setup: the watcher keeps running");

            MainMenu.ConfigOptionsSetupComplete = true;
            m.Frame();
            Check(m.Ticks.Count == 0 && MainMenu.Asleep && W.Events.Count == 0, "setup done, every menu closed: it sleeps");
            var keyFrames = W.KeyFrames;
            for (var i = 0; i < 100; i++) m.Frame();
            Check(W.KeyFrames == keyFrames, "asleep: nothing runs, frame after frame");

            MainMenu.Changed();
            Check(MainMenu.Asleep, "MenuAPI's event with no menu open: still asleep");

            var main = new Menu { Visible = true };
            W.Current = main;
            MainMenu.Changed();
            Check(!MainMenu.Asleep && m.Ticks.Count == 1 && W.Opened == 1 && W.Events[W.Events.Count - 1] == "vMenu:MenuStateChanged:True",
                "a menu opens: the event wakes it and does the open frame at once (rests end, state event sent)");
            m.Frame(); m.Frame();
            Check(!MainMenu.Asleep && W.Opened == 1 && W.KeyFrames == keyFrames + 3, "while a menu is open it runs every frame, and announces the open once");

            var creator = MainMenu.MpPedCustomizationMenu.createCharacterMenu;
            main.Visible = false; creator.Visible = true; W.Current = creator;
            m.Frame();
            Check(MpPedCustomization.DisableBackButton && MpPedCustomization.DontCloseMenus, "in the character creator: the back button guard is on");
            W.BackReleased = true; m.Frame(); W.BackReleased = false;
            Check(W.Alerts.Count == 1, "the back key in the creator shows the save-first alert");

            // The "exit without saving" prompt closes the menus, then the creator comes back.
            creator.Visible = false; W.Current = null;
            m.Frame();
            Check(MainMenu.Asleep && !MpPedCustomization.DisableBackButton && W.Offers == 1 && W.Events[W.Events.Count - 1] == "vMenu:MenuStateChanged:False",
                "every menu closed: the close frame is handled (guard off, saved vehicle offer, state event), then it sleeps");
            creator.Visible = true; W.Current = creator;
            MainMenu.Changed();
            Check(MpPedCustomization.DisableBackButton && MpPedCustomization.DontCloseMenus && W.Opened == 2,
                "the creator comes back: its guard is set by MenuAPI's event, before any of MenuAPI's input runs");

            creator.Visible = false; W.Current = null; m.Frame();
            W.Current = main; main.Visible = true;
            for (var i = 0; i < 5; i++) m.Frame();
            Check(MainMenu.Asleep && W.Opened == 2, "if MenuAPI's event never came, nothing runs...");
            MainMenu.SafetyNet();
            Check(!MainMenu.Asleep && W.Opened == 3, "...until the 100 ms safety net wakes it");
            MainMenu.SafetyNet();
            Check(W.Opened == 3, "the safety net does nothing while it is awake");

            W.Dead = true; m.Frame(); W.Dead = false;
            Check(W.Closed == 1 && W.Current == null, "dying with a menu open closes it");
            m.Frame();
            Check(MainMenu.Asleep, "and then it sleeps");

            // --- deaths ---
            var d = new Deaths();
            d.Event("CEventNetworkEntityDamage", 99, 7, 0, 1);
            W.Dead = true; d.Event("CEventNetworkEntityDamage", 99, 7, 0, 1); W.Dead = false;
            Check(W.Saves == 0, "damage to another entity: nothing");
            d.Event("CEventNetworkEntityDamage", 7, 99, 0, 0);
            Check(W.Saves == 0, "the player hurt but alive: nothing");
            d.Event("CEventNetworkEntityShot", 7);
            Check(W.Saves == 0, "other game events: nothing");
            d.Event("CEventNetworkEntityDamage", "not a number");
            d.Event("CEventNetworkEntityDamage");
            Check(W.Saves == 0, "an odd argument list: nothing, no error");

            W.Dying = true;
            d.Event("CEventNetworkEntityDamage", 7, 99, 0, 1);
            Check(W.Saves == 1 && d.InProgress, "the player dying: saved from the damage event, on that frame");
            W.Dead = true;
            d.Event("CEventNetworkEntityDamage", 7, 99, 0, 1);
            var during = d.Fallback();
            Check(W.Saves == 1 && during.IsCompleted, "one save per death: the next damage event and the fallback check don't start another");
            if (W.Saves != 1) { Console.WriteLine($"{failures} check(s) failed"); return 1; } // the extra saves would never finish
            W.SaveRunning.SetResult(true);
            Check(!d.InProgress, "done once the restore finishes");
            W.Dead = false; W.Dying = false;

            var delays = W.Delays;
            Check(d.Fallback().IsCompleted && W.Saves == 1 && W.Delays == delays + 1, "alive: the fallback check just waits 100 ms");
            W.Dead = true;
            var fallback = d.Fallback();
            Check(W.Saves == 2, "a death no damage event reported (a script set health to 0): the fallback check saves");
            W.SaveRunning.SetResult(true);
            Check(fallback.Wait(2000) && !d.InProgress, "and finishes with the restore");
            W.Dead = false;

            // --- the sleeping loops registry ---
            var loops = new SleepLoops.Loops();
            loops.Start();
            Check(loops.Registered == 2, "both loops start on the tick list");
            loops.Frame();
            Check(loops.Registered == 1 && loops.First.Asleep && loops.Runs == 1, "nothing to do: the loop takes itself off the tick list");
            loops.Frame(); loops.Frame();
            Check(loops.Runs == 1, "asleep: it never runs");
            loops.Check100ms();
            Check(loops.First.Asleep, "the 100 ms check leaves it asleep while it has nothing to do");
            loops.Wanted = true;
            loops.Check100ms();
            Check(!loops.First.Asleep && loops.Registered == 2, "the 100 ms check puts it back once its test says it has work");
            loops.Frame();
            Check(loops.Runs == 2 && !loops.First.Asleep, "and it runs, staying on while it has work");
            loops.Wanted = false; loops.Frame();
            Check(loops.First.Asleep, "asleep again");
            vMenuClient.IdleRest.WakeAll();
            Check(!loops.First.Asleep && loops.Registered == 2, "IdleRest.WakeAll (a menu opening, permissions...) puts every sleeping loop back, with no rest in progress");
            loops.Frame();
            Check(loops.First.Asleep && loops.Runs == 4, "it runs once and sleeps again when still idle");
            vMenuClient.IdleRest.WakeAll();
            Check(loops.Registered == 2, "woken once, not twice (WireSleepingLoops wires once)");
            loops.Frame();
            SleepLoops.Loops.Printed.Clear();
            loops.Commands["vmenu_ticks"]();
            var printed = SleepLoops.Loops.Printed;
            Check(printed.Count == 4 && printed[0].Contains("2 loops") && printed[0].Contains("1 asleep") && printed[1].Contains("Loop: asleep") && printed[2].Contains("Other: running"),
                "vmenu_ticks lists each sleeping loop and whether it is asleep: " + string.Join(" | ", printed));

            Console.WriteLine(failures == 0 ? "PASS: the watcher sleeps while menus are closed, the death save runs once per death, and sleeping loops wake by event or by their own test" : $"{failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-sleeping-loops-') as tmp:
    work = Path(tmp)
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'IdleRest.cs').write_text(idle_rest, encoding='utf-8')
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable><NoWarn>CS1998;CS0162;CS0414;CS0649;CS0169;CS8321</NoWarn></PropertyGroup>
</Project>
''', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'), '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
