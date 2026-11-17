"""Release J: vMenu's own keys are FiveM key bindings instead of game controls read every frame.

vMenu Enhanced reads keys like these with key bindings. Release J moves the visor, snowball, finger
point, expanded minimap, drift and teleport-to-waypoint keys out of the every-frame loops
(AnimationsAndInteractions and MiscSettings) into commands with key mappings.

Source checks: the command names (FiveM saves each player's chosen key under them), default keys,
press or release timing, and that nothing polls these keys any more.

Compiled checks: the production handlers from vMenu/FunctionsController.cs run against fake natives
and keep the old conditions (option switched on, permission, in or out of a vehicle, typing, other
scripts disabling the control, menu or pause menu open, controller double tap, waypoint set).
The visor and snowball handlers are covered by tests/frame-costs.

Requires Python and the .NET 9 SDK; no FiveM runtime.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
fc = (root / 'vMenu' / 'FunctionsController.cs').read_text(encoding='utf-8-sig')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


# --- source checks ---------------------------------------------------------------------------
bindings = member(fc, r'^        private void RegisterKeyBindings\(\).*?^        }$')
expected = [
    ('+vMenu:{id}:Visor', 'keyboard', 'F11'), ('+vMenu:{id}:Visor', 'PAD_ANALOGBUTTON', 'LRIGHT_INDEX'),
    ('+vMenu:{id}:Snowball', 'keyboard', 'G'), ('+vMenu:{id}:Snowball', 'PAD_ANALOGBUTTON', 'LRIGHT_INDEX'),
    ('vMenu:{id}:Point', 'keyboard', 'B'), ('vMenu:{id}:PointPad', 'PAD_ANALOGBUTTON', 'R3_INDEX'),
    ('+vMenu:{id}:Minimap', 'keyboard', 'Z'), ('+vMenu:{id}:Minimap', 'PAD_ANALOGBUTTON', 'LDOWN_INDEX'),
    ('+vMenu:{id}:Drift', 'keyboard', 'LSHIFT'), ('+vMenu:{id}:Drift', 'PAD_ANALOGBUTTON', 'RLEFT_INDEX'),
]
for command, device, key in expected:
    assert re.search(r'RegisterKeyMapping\(\$"' + re.escape(command) + r'", "[^"]+", "' + device + r'", "' + key + r'"\);', bindings), \
        f'{command} on {device} defaults to {key}'
assert 'RegisterKeyMapping($"vMenu:{id}:TpToWaypoint", "vMenu Teleport To Waypoint", "keyboard", tpKey);' in bindings
assert '[168] = "F7"' in fc, 'vmenu_teleport_to_wp_keybind_key 168 still means F7'
# Release keys act on release, as the old JustReleased checks did.
assert 'Command($"-vMenu:{id}:Snowball", OnSnowballReleased);' in bindings
assert 'Command($"-vMenu:{id}:Minimap", OnMinimapReleased);' in bindings
assert 'if (keyBindingsRegistered)' in bindings, 'registered once'
misc = member(fc, r'^        private async Task MiscSettings\(\).*?^        }$')
for gone in ('Control.SpecialAbilitySecondary', 'Control.Sprint', 'KbTpToWaypointKey', 'Control.MultiplayerInfo'):
    assert gone not in misc, f'MiscSettings no longer reads {gone} every frame'
for option in ('KbTpToWaypoint', 'KbDriftMode', 'KbPointKeys', 'KbRadarKeys'):
    assert option not in misc, f'{option} no longer keeps MiscSettings running every frame'
assert 'Tick += AnimationsAndInteractions' not in fc
print('PASS: key binding names, default keys, release timing; nothing polls them every frame')

# --- compiled checks -------------------------------------------------------------------------
handlers = ''.join(member(fc, rf'^        private (?:async )?\w+ {name}\(.*?\).*?^        }}$') for name in
                   ('OnPointKey', 'PointingTick', 'OnMinimapReleased', 'SetDriftGrip', 'OnTeleportToWaypointKey'))

program = r'''
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Keys.N;

namespace Keys
{
    public enum Control { SpecialAbilitySecondary, MultiplayerInfo }
    public enum Permission { MSDriftMode, MSTeleportToWp }

    public static class W
    {
        public static bool KbPointKeys, KbRadarKeys, KbDriftMode, KbTpToWaypoint, InVehicle, Pointing, Typing, MenuOpen, Pause, Bigmap,
            Waypoint, FadedIn = true, VehicleExists = true, VehicleDead;
        public static HashSet<Control> Disabled = new HashSet<Control>();
        public static HashSet<Permission> Denied = new HashSet<Permission>();
        public static int Now, PointStarts, PointClears, Signals, GripOn, GripOff, Teleports, Wakes, Sleeps;
        public static List<string> Notes = new List<string>();
        public static void Reset()
        {
            KbPointKeys = KbRadarKeys = KbDriftMode = KbTpToWaypoint = InVehicle = Pointing = Typing = MenuOpen = Pause = Bigmap = Waypoint = VehicleDead = false;
            FadedIn = VehicleExists = true; Disabled.Clear(); Denied.Clear(); Notes.Clear();
            PointStarts = PointClears = Signals = GripOn = GripOff = Teleports = Wakes = Sleeps = 0;
        }
    }
    public class Misc
    {
        public bool KbPointKeys => W.KbPointKeys; public bool KbRadarKeys => W.KbRadarKeys;
        public bool KbDriftMode => W.KbDriftMode; public bool KbTpToWaypoint => W.KbTpToWaypoint;
    }
    public static class MainMenu { public static Misc MiscSettingsMenu = new Misc(); }
    public static class MenuController { public static bool IsAnyMenuOpen() => W.MenuOpen; }
    public static class Fading { public static bool IsFadedIn => W.FadedIn; }
    public static class Debug { public static void WriteLine(string s) => throw new Exception(s); }
    public static class Notify { public static void Success(string s) => W.Notes.Add("ok:" + s); public static void Error(string s) => W.Notes.Add("err:" + s); }
    public class Ped { public int Handle => 5; public bool IsInVehicle() => W.InVehicle; }
    public class Vehicle { public int Handle => 77; public bool IsDead => W.VehicleDead; public bool Exists() => W.VehicleExists; }
    public static class Game
    {
        public static Ped PlayerPed => new Ped();
        public static bool IsWaypointActive => W.Waypoint;
        public static bool IsControlEnabled(int group, Control control) => !W.Disabled.Contains(control);
    }
    public class TickSleeper { public void Wake() => W.Wakes++; public void Sleep() => W.Sleeps++; }
    public static class N
    {
        public static int GetGameTimer() => W.Now;
        public static int UpdateOnscreenKeyboard() => W.Typing ? 0 : 3;
        public static bool IsPedPointing(int ped) => W.Pointing;
        public static void ClearPedSecondaryTask(int ped) { W.PointClears++; W.Pointing = false; }
        public static Task<bool> LoadAnimDictAsync(string dict) => Task.FromResult(true);
        public static void TaskMoveNetwork(int ped, string task, float a, bool b, string dict, int flags) { W.PointStarts++; W.Pointing = true; }
        public static void RemoveAnimDict(string dict) { }
        public static int PlayerPedId() => 5;
        public static bool IsPedInAnyVehicle(int ped, bool atGetIn) => W.InVehicle;
        public static void SetTaskMoveNetworkSignalFloat(int ped, string name, float value) => W.Signals++;
        public static void SetTaskMoveNetworkSignalBool(int ped, string name, bool value) => W.Signals++;
        public static float GetPointingPitch() => 0f; public static float GetPointingHeading() => 0f; public static bool GetPointingIsBlocked() => false;
        public static int GetFollowPedCamViewMode() => 1;
        public static bool IsPauseMenuActive() => W.Pause;
        public static bool IsBigmapActive() => W.Bigmap;
        public static void SetBigmapActive(bool on, bool full) => W.Bigmap = on;
        public static bool DoesEntityExist(int e) => W.VehicleExists;
        public static void SetVehicleReduceGrip(int veh, bool on) { if (on) { W.GripOn++; } else { W.GripOff++; } }
        public static bool IsAllowed(Permission p) => !W.Denied.Contains(p);
        public static Vehicle GetVehicle() => W.InVehicle ? new Vehicle() : null;
        public static bool IsPlayerSwitchInProgress() => false;
        public static void TeleportToWp() => W.Teleports++;
    }

    public class Handlers
    {
        private int lastPadPointPress;
        private int driftVehicle;
        private TickSleeper pointingSleeper = new TickSleeper();
        bool vMenuOpenedBigmap;
        int radarSwitchTimer;
        public bool expandedRadarProfile;
''' + handlers + r'''
        public void Point(bool pad) => OnPointKey(pad);
        public Task Tick() => PointingTick();
        public void Minimap() => OnMinimapReleased();
        public void Drift(bool held) => SetDriftGrip(held);
        public void Teleport() => OnTeleportToWaypointKey();
        public bool OpenedBigmap => vMenuOpenedBigmap;
    }

    public static class Program
    {
        static int failures;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

        public static int Main()
        {
            var h = new Handlers();

            // Finger point
            W.Reset(); h.Point(false);
            Check(W.PointStarts == 0, "point: option off, nothing happens");
            W.KbPointKeys = true; h.Point(false);
            Check(W.PointStarts == 1 && W.Wakes == 1, "point: B starts pointing and wakes the steering loop");
            h.Point(false);
            Check(W.PointClears == 1 && !W.Pointing, "point: B again stops");
            W.InVehicle = true; h.Point(false);
            Check(W.PointStarts == 1, "point: not in a vehicle");
            W.InVehicle = false; W.Typing = true; h.Point(false);
            Check(W.PointStarts == 1, "point: not while typing in the onscreen keyboard");
            W.Typing = false; W.Disabled.Add(Control.SpecialAbilitySecondary); h.Point(false);
            Check(W.PointStarts == 1, "point: not while another script disables the point control");
            W.Disabled.Clear();
            W.Now = 1000; h.Point(true);
            Check(W.PointStarts == 1, "point: one tap of the right stick does nothing");
            W.Now = 1200; h.Point(true);
            Check(W.PointStarts == 2, "point: a double tap within 300 ms toggles");
            W.Now = 2000; h.Point(true); W.Now = 2400; h.Point(true);
            Check(W.PointClears == 1, "point: taps 400 ms apart are not a double tap");

            // Steering loop
            W.Reset(); W.Pointing = true; h.Tick();
            Check(W.Signals == 5 && W.Sleeps == 0, "steering: all five signals every frame while pointing");
            W.InVehicle = true; h.Tick();
            Check(W.PointClears == 1, "steering: getting in a vehicle stops pointing");
            W.InVehicle = false; W.Pointing = false; h.Tick();
            Check(W.Sleeps == 1, "steering: off the tick list once not pointing");

            // Minimap
            W.Reset(); h.Minimap();
            Check(!W.Bigmap, "minimap: option off, nothing happens");
            W.KbRadarKeys = true; h.Minimap();
            Check(W.Bigmap && h.OpenedBigmap, "minimap: opens the expanded radar and remembers vMenu opened it");
            h.Minimap();
            Check(!W.Bigmap && !h.OpenedBigmap, "minimap: closes it again");
            W.MenuOpen = true; h.Minimap(); W.MenuOpen = false;
            W.Pause = true; h.Minimap(); W.Pause = false;
            W.Disabled.Add(Control.MultiplayerInfo); h.Minimap(); W.Disabled.Clear();
            h.expandedRadarProfile = true; h.Minimap(); h.expandedRadarProfile = false;
            Check(!W.Bigmap, "minimap: not with a menu open, the pause menu open, the control disabled or the pause menu's expanded radar on");

            // Drift
            W.Reset(); W.InVehicle = true; h.Drift(true); h.Drift(false);
            Check(W.GripOn == 0 && W.GripOff == 0, "drift: option off, nothing happens");
            W.KbDriftMode = true; h.Drift(true);
            Check(W.GripOn == 1, "drift: holding reduces grip");
            h.Drift(false);
            Check(W.GripOff == 1, "drift: releasing restores it");
            h.Drift(false);
            Check(W.GripOff == 1, "drift: a release without a press does nothing");
            W.Denied.Add(Permission.MSDriftMode); h.Drift(true); W.Denied.Clear();
            W.InVehicle = false; h.Drift(true); W.InVehicle = true;
            W.VehicleDead = true; h.Drift(true); W.VehicleDead = false;
            Check(W.GripOn == 1, "drift: not without permission, on foot or in a wrecked vehicle");
            h.Drift(true); W.VehicleExists = false; h.Drift(false);
            Check(W.GripOff == 1, "drift: a deleted vehicle is not touched on release");

            // Teleport to waypoint
            W.Reset(); W.Waypoint = true; h.Teleport();
            Check(W.Teleports == 0, "teleport: option off, nothing happens");
            W.KbTpToWaypoint = true; h.Teleport();
            Check(W.Teleports == 1 && W.Notes.Contains("ok:Teleported to waypoint."), "teleport: goes to the waypoint");
            W.Waypoint = false; h.Teleport();
            Check(W.Teleports == 1 && W.Notes.Contains("err:You need to set a waypoint first."), "teleport: no waypoint, an error");
            W.Waypoint = true; W.FadedIn = false; h.Teleport(); W.FadedIn = true;
            W.Denied.Add(Permission.MSTeleportToWp); h.Teleport();
            Check(W.Teleports == 1, "teleport: not while faded out or without permission");

            Console.WriteLine(failures == 0 ? "PASS: key binding handlers keep the old conditions" : $"{failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-key-bindings-') as tmp:
    work = Path(tmp)
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable><NoWarn>CS1998;CS0162;CS0414;CS0649;CS0169</NoWarn></PropertyGroup>
</Project>
''', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'), '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
