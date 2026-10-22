"""Release G: the per-frame cost cuts keep vMenu's behaviour.

Compiles the production code from vMenu/FunctionsController.cs against fake natives and runs it
frame by frame on a small scheduler that behaves like FiveM's C# runtime (a tick starts again
only after its last run finished; Delay(ms) resumes on the first frame at least ms later):

- player blips: the throttled refresh against the same code with the throttle off (which is
  the old every-frame behaviour), over players joining, leaving, entering a heli, coming into
  range, the map opening, the duty blip list changing and blips going off and on again
- snowball and visor keys: read first; nothing else runs, and the snow convar isn't read,
  until one is used; pickup and visor still work
- clothing glow: one wake-up per 25 ms pass instead of one per frame, same glow
- the shared per-frame tick: movement lock, animal camera block, vehicle health text and
  highbeam flash still run every frame, each isolated from the others' errors

Plus source checks for the wiring. Requires Python and the .NET 9 SDK; no FiveM runtime.
"""
from pathlib import Path
import re
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
read = lambda rel: (root / rel).read_text(encoding='utf-8-sig')
# Another copy of FunctionsController.cs can be passed in (to compare an older build).
fc = Path(sys.argv[1]).read_text(encoding='utf-8-sig') if len(sys.argv) > 1 else read('vMenu/FunctionsController.cs')
main_menu = read('vMenu/MainMenu.cs')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


def region(source, name):
    return member(source, r'^        #region ' + re.escape(name) + r'.*?^        #endregion')


# --- wiring -----------------------------------------------------------------------------------
setup = member(fc, r'^        public void SetupTickFunctions\(\).*?^        }$')
for gone in ('Tick += VehicleShowHealthOnScreenTick', 'Tick += VehicleHighbeamFlashTick', 'Tick += DisableMovement', 'Tick += AnimalPedCameraChangeBlocker'):
    assert gone not in setup, f'{gone} is now part of PerFrameChecks'
assert setup.rstrip().endswith('''if (vehicleHealthCheckEnabled || highbeamFlashCheckEnabled || movementLockCheckEnabled || animalCameraCheckEnabled)
            {
                Tick += PerFrameChecks;
            }
        }'''), 'the shared tick is registered when any of its checks is'
assert re.search(r'Tick \+= VehicleOptions;\s*vehicleHealthCheckEnabled = true;\s*if \(IsAllowed\(Permission\.VOFlashHighbeamsOnHonk\)\)\s*\{\s*highbeamFlashCheckEnabled = true;', setup), \
    'health text and highbeams keep their permissions'
assert re.search(r'if \(IsAllowed\(Permission\.PAMenu\)\)\s*\{\s*Tick \+= ManageCamera;\s*movementLockCheckEnabled = true;', setup)
assert re.search(r'if \(GetSettingsBool\(Setting\.vmenu_enable_animals_spawn_menu\)\)\s*\{\s*animalCameraCheckEnabled = true;', setup)
assert 'MenuController.EnableManualGCs = false;' in main_menu, "MenuAPI's forced GC every 60 s is off"
assert '(uint)GetHashKey("gadget_parachute")' not in fc, 'the parachute hash is a constant, not hashed every frame'
weapons = member(fc, r'^        private async Task WeaponOptions\(\).*?^        }$')
assert 'Game.PlayerPed.Handle' not in weapons.split('var ped = PlayerPedId();')[1], 'weapon options reuse one ped lookup'
print('PASS: wiring (shared per-frame tick, permissions, MenuAPI GC, weapon options)')

# --- shared scheduler and checks --------------------------------------------------------------
runtime = r'''
namespace Harness
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // Like FiveM's C# runtime: each frame, Delays that are due resume, then every tick whose last
    // run has finished starts again. Delay(0) resumes next frame. A run that finishes while being
    // resumed starts again only the frame after (the runtime clears it from its task list in a
    // continuation), which is why the profile shows such ticks half as fresh runs, half as resumes.
    public sealed class FrameRuntime
    {
        public int Now;
        public int Resumes;
        readonly List<KeyValuePair<int, TaskCompletionSource<bool>>> delays = new List<KeyValuePair<int, TaskCompletionSource<bool>>>();
        readonly List<Func<Task>> ticks = new List<Func<Task>>();
        readonly List<Task> running = new List<Task>();

        public void Add(Func<Task> tick) { ticks.Add(tick); running.Add(null); }

        public Task Delay(int ms)
        {
            var tcs = new TaskCompletionSource<bool>();
            delays.Add(new KeyValuePair<int, TaskCompletionSource<bool>>(Now + Math.Max(ms, 1), tcs));
            return tcs.Task;
        }

        public void Frame(int now)
        {
            Now = now;
            var waiting = running.ConvertAll(t => t != null && !t.IsCompleted);
            var due = delays.FindAll(d => d.Key <= now);
            delays.RemoveAll(d => d.Key <= now);
            foreach (var d in due) { Resumes++; d.Value.SetResult(true); }
            for (var i = 0; i < ticks.Count; i++)
            {
                if (running[i] != null && running[i].IsFaulted) { throw running[i].Exception; }
                if (running[i] == null || (running[i].IsCompleted && !waiting[i])) { running[i] = ticks[i](); }
            }
        }
    }

    public static class Assert
    {
        public static void Check(bool condition, string message) { if (!condition) { throw new Exception("FAIL: " + message); } }
    }
}
'''

# --- player blips -----------------------------------------------------------------------------
blips = region(fc, 'player blips tasks')
every_frame_blips = blips.replace('private const int PlayerBlipsRefreshMs = 100;', 'private const int PlayerBlipsRefreshMs = 0;')
assert every_frame_blips != blips

blip_test = r'''
namespace Blips
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Harness;
    using static Harness.Assert;
    using static Blips.Natives;

    public class EventHandlerAttribute : Attribute { public EventHandlerAttribute(string name) { } }
    public static class Debug { public static void WriteLine(string text) { World.Current.Log.Add(text); } }

    public struct Vector3
    {
        public float X, Y;
        public Vector3(float x, float y) { X = x; Y = y; }
        public float DistanceToSquared2D(Vector3 other) { var dx = X - other.X; var dy = Y - other.Y; return dx * dx + dy * dy; }
    }

    public class PedState { public Vector3 Pos; public float Heading; public bool InVehicle, InHeli; public int? Decor; }
    public class BlipState
    {
        public int Entity; public string Owner;
        public int Sprite = -1, Colour = -1, Category = -1, Display = -1, NamePlayer = -1, Rotation = int.MinValue;
        public bool? Heading;
        public string Show(bool rotation) => $"{Entity}/{Owner}/s{Sprite}/c{Colour}/k{Category}/d{Display}/h{Heading}/n{NamePlayer}" + (rotation ? $"/r{Rotation}" : "");
    }

    // Every member that is a native in the real game counts as one call.
    public class Ped
    {
        public readonly int Handle;
        public Ped(int handle) { Handle = handle; }
        public Vector3 Position { get { World.Current.Calls++; return World.Current.Peds[Handle].Pos; } }
        public bool IsInHeli { get { World.Current.Calls++; return World.Current.Peds[Handle].InHeli; } }
        public bool Exists() { World.Current.Calls++; return World.Current.Peds.ContainsKey(Handle); }
    }
    public class Vehicle { public int Handle; public bool Exists() { World.Current.Calls++; return true; } }

    public class FakePlayer
    {
        readonly int serverId; readonly bool local;
        public readonly int Handle, PedHandle;
        public FakePlayer(int handle, int serverId, int ped, bool local = false) { Handle = handle; this.serverId = serverId; PedHandle = ped; this.local = local; }
        public int ServerId { get { World.Current.Calls++; return serverId; } }
        public bool IsLocal { get { World.Current.Calls++; return local; } }
        public Ped Character { get { World.Current.Calls++; return World.Current.Peds.ContainsKey(PedHandle) ? new Ped(PedHandle) : null; } }
    }

    public class MiscSettings { public bool ShowPlayerBlips = true; }
    public static class MainMenu
    {
        public static MiscSettings MiscSettingsMenu => World.Current.Misc;
        public static IEnumerable<FakePlayer> PlayersList => World.Current.Players.ToList();
    }
    public static class Game
    {
        public static Ped PlayerPed { get { World.Current.Calls += 2; return new Ped(World.Current.LocalPed); } }
        public static bool IsPaused { get { World.Current.Calls++; return World.Current.Paused; } }
    }
    public static class BlipInfo { public static int GetBlipSpriteForVehicle(int vehicle) { World.Current.Calls++; return 225; } }

    public class World
    {
        public static World Current;
        public readonly FrameRuntime Runtime = new FrameRuntime();
        public int Calls, DecorWrites, NextBlip = 1, LocalPed = 100;
        public bool Paused;
        public readonly MiscSettings Misc = new MiscSettings();
        public readonly Dictionary<int, PedState> Peds = new Dictionary<int, PedState>();
        public readonly SortedDictionary<int, BlipState> Blips = new SortedDictionary<int, BlipState>();
        public readonly List<FakePlayer> Players = new List<FakePlayer>();
        public readonly List<string> Log = new List<string>();
        public readonly HashSet<int> Rotated = new HashSet<int>();

        public void DeletePed(int ped)
        {
            Peds.Remove(ped);
            foreach (var blip in Blips.Where(b => b.Value.Entity == ped).Select(b => b.Key).ToList()) { Blips.Remove(blip); }
        }

        public string Snapshot(ISet<int> rotatedPeds) =>
            string.Join(" | ", Blips.Values.OrderBy(b => b.Entity).ThenBy(b => b.Owner).Select(b => b.Show(rotatedPeds.Contains(b.Entity))));

        public HashSet<int> RotatedPeds() => new HashSet<int>(Rotated.Where(Blips.ContainsKey).Select(b => Blips[b].Entity));
    }

    public static class Natives
    {
        static World W => World.Current;
        static BlipState B(int blip) => W.Blips[blip]; // styling a blip that is gone would throw: a bug
        public static Task Delay(int ms) => W.Runtime.Delay(ms);
        public static int GetGameTimer() { W.Calls++; return W.Runtime.Now; }
        public static bool DecorIsRegisteredAsType(string name, int type) { W.Calls++; return true; }
        public static void DecorRegister(string name, int type) { W.Calls++; }
        public static void DecorSetInt(int ped, string name, int value) { W.Calls++; W.DecorWrites++; }
        public static bool DecorExistOn(int ped, string name) { W.Calls++; return W.Peds.TryGetValue(ped, out var p) && p.Decor.HasValue; }
        public static int DecorGetInt(int ped, string name) { W.Calls++; return W.Peds[ped].Decor.Value; }
        public static bool IsPedInAnyVehicle(int ped, bool atGetIn) { W.Calls++; return W.Peds.TryGetValue(ped, out var p) && p.InVehicle; }
        public static int GetVehiclePedIsIn(int ped, bool last) { W.Calls++; return ped + 1000; }
        public static Vehicle GetVehicle() { W.Calls += 3; return W.Peds[W.LocalPed].InVehicle ? new Vehicle { Handle = 1 } : null; }
        public static bool NetworkIsPlayerActive(int player) { W.Calls++; return player >= 0; }
        public static int GetBlipFromEntity(int ped) { W.Calls++; return W.Blips.Where(b => b.Value.Entity == ped).Select(b => b.Key).FirstOrDefault(); }
        public static int AddBlipForEntity(int ped) { W.Calls++; var blip = W.NextBlip++; W.Blips[blip] = new BlipState { Entity = ped, Owner = "vmenu" }; return blip; }
        public static bool DoesBlipExist(int blip) { W.Calls++; return W.Blips.ContainsKey(blip); }
        public static void RemoveBlip(ref int blip) { W.Calls++; W.Blips.Remove(blip); blip = 0; }
        public static void SetBlipSprite(int blip, int sprite) { W.Calls++; B(blip).Sprite = sprite; }
        public static void SetBlipColour(int blip, int colour) { W.Calls++; B(blip).Colour = colour; }
        public static void SetBlipNameToPlayerName(int blip, int player) { W.Calls++; B(blip).NamePlayer = player; }
        public static void SetBlipCategory(int blip, int category) { W.Calls++; B(blip).Category = category; }
        public static void ShowHeadingIndicatorOnBlip(int blip, bool on) { W.Calls++; B(blip).Heading = on; }
        public static void SetBlipRotation(int blip, int rotation) { W.Calls++; B(blip).Rotation = rotation; W.Rotated.Add(blip); }
        public static float GetEntityHeading(int ped) { W.Calls++; return W.Peds.TryGetValue(ped, out var p) ? p.Heading : 0f; }
        public static void SetBlipDisplay(int blip, int display) { W.Calls++; B(blip).Display = display; }
    }

    public class Throttled
    {
''' + blips + r'''
        public Task Tick() => PlayerBlipsControl();
        public List<KeyValuePair<int, int>> Rotating => rotatingPlayerBlips;
    }

    public class EveryFrame
    {
''' + every_frame_blips + r'''
        public Task Tick() => PlayerBlipsControl();
    }

    public static class Test
    {
        static void Setup(World w)
        {
            void AddPed(int ped, float x, float y, int? decor, bool inVehicle = false) { w.Peds[ped] = new PedState { Pos = new Vector3(x, y), Decor = decor, InVehicle = inVehicle }; }
            AddPed(100, 0, 0, 1);               // you
            AddPed(101, 10, 0, 1);              // on foot nearby
            AddPed(102, 20, 0, 225, true);      // in a car nearby
            AddPed(103, 2000, 0, 1);            // out of minimap range
            AddPed(104, 30, 0, 1);              // on duty: the duty resource draws their blip
            AddPed(105, 40, 0, 1);              // another resource's blip, not in the duty list
            AddPed(106, 50, 0, null, true);     // no decorator: sprite from their vehicle
            AddPed(108, 3000, 0, 1);            // stays out of range (shown again while the map is open)
            w.Blips[900] = new BlipState { Entity = 104, Owner = "duty" };
            w.Blips[901] = new BlipState { Entity = 105, Owner = "other" };
            w.Players.Add(new FakePlayer(0, 1, 100, local: true));
            for (var i = 1; i <= 6; i++) { w.Players.Add(new FakePlayer(i, i + 1, 100 + i)); }
            w.Players.Add(new FakePlayer(8, 10, 108));
            w.Players.Add(new FakePlayer(-1, 9, 0));  // elsewhere on the server, not in scope
        }

        static void Script(World w, int frame)
        {
            foreach (var ped in w.Peds) { ped.Value.Heading = (frame * 7 + ped.Key * 13) % 360; }
            if (frame == 30) { var p = w.Peds[101]; p.Decor = 422; p.InVehicle = true; p.InHeli = true; }
            if (frame == 60) { w.Peds[103].Pos = new Vector3(15, 15); }
            if (frame == 90) { w.Players.RemoveAll(p => p.PedHandle == 102); w.DeletePed(102); }
            if (frame == 100) { w.Peds[107] = new PedState { Pos = new Vector3(5, 5), Decor = 1 }; w.Players.Add(new FakePlayer(7, 8, 107)); }
            w.Paused = frame >= 120 && frame < 140;
            if (frame == 170) { w.Misc.ShowPlayerBlips = false; }
            if (frame == 185) { w.Misc.ShowPlayerBlips = true; }
        }

        public static void Run()
        {
            World a = new World(), b = new World();
            Setup(a); Setup(b);
            var throttled = new Throttled();
            var everyFrame = new EveryFrame();
            a.Runtime.Add(throttled.Tick);
            b.Runtime.Add(everyFrame.Tick);
            World.Current = a; throttled.SetExternalBlipPlayers("5");
            World.Current = b; everyFrame.SetExternalBlipPlayers("5");
            var duty = a.Blips[900].Show(true);
            var other = a.Blips[901].Show(true);

            int lastRefresh = -1, refreshes = 0, compared = 0, quietFrames = 0, quietCalls = 0, everyFrameCalls = 0;
            var refreshRotated = new HashSet<int>();
            var gaps = new List<int>();
            for (var frame = 0; frame < 260; frame++)
            {
                var now = 100000 + frame * 16;  // GetGameTimer is well past 0 in a real game
                Script(a, frame); Script(b, frame);
                if (frame == 150)
                {
                    World.Current = a; throttled.SetExternalBlipPlayers("2,5");
                    World.Current = b; everyFrame.SetExternalBlipPlayers("2,5");
                }
                World.Current = a; a.Calls = 0; a.DecorWrites = 0; a.Rotated.Clear(); a.Runtime.Frame(now);
                World.Current = b; b.Calls = 0; b.DecorWrites = 0; b.Rotated.Clear(); b.Runtime.Frame(now);
                everyFrameCalls += b.Calls;
                World.Current = a;

                Check(a.Blips[900].Show(true) == duty && a.Blips[901].Show(true) == other, $"frame {frame}: other resources' blips are never touched");
                if (a.DecorWrites > 0)
                {
                    // A refresh: everything matches the every-frame version, rotation included for the
                    // blips rotated this frame.
                    refreshes++;
                    if (lastRefresh >= 0 && frame < 170) { gaps.Add(frame - lastRefresh); }
                    lastRefresh = frame;
                    var rotated = b.RotatedPeds();
                    Check(new HashSet<int>(a.RotatedPeds()).SetEquals(rotated), $"frame {frame}: the same blips rotate");
                    Check(a.Snapshot(rotated) == b.Snapshot(rotated), $"frame {frame}: after a refresh the blips match the every-frame version\n  {a.Snapshot(rotated)}\n  {b.Snapshot(rotated)}");
                    refreshRotated = rotated;
                    compared++;
                }
                else if (a.Calls > 0)
                {
                    // Between refreshes: only the rotation, and it follows the ped this frame.
                    quietFrames++;
                    quietCalls += a.Calls;
                    Check(a.Calls <= 1 + 3 * throttled.Rotating.Count, $"frame {frame}: {a.Calls} game calls between refreshes");
                    var stillThere = new HashSet<int>(refreshRotated.Where(ped => a.Blips.Values.Any(x => x.Owner == "vmenu" && x.Entity == ped)));
                    Check(a.RotatedPeds().SetEquals(stillThere), $"frame {frame}: between refreshes the blips the last refresh rotated keep rotating");
                    foreach (var entry in throttled.Rotating)
                    {
                        if (a.Blips.ContainsKey(entry.Key))
                        {
                            Check(a.Rotated.Contains(entry.Key) && a.Blips[entry.Key].Rotation == (int)a.Peds[entry.Value].Heading, $"frame {frame}: blip {entry.Key} follows its player's heading");
                        }
                    }
                }
                Check(a.Log.Count == 0 && b.Log.Count == 0, "no errors logged");
            }
            Check(gaps.Count > 10 && gaps.TrueForAll(g => g == 7), "refreshes every 100 ms (7 frames of 16 ms) while blips are on: " + string.Join(",", gaps));
            Check(a.Blips.Values.Count(x => x.Owner == "vmenu") == b.Blips.Values.Count(x => x.Owner == "vmenu") && a.Blips.Values.Count(x => x.Owner == "vmenu") > 0, "blips are back after turning them on again");
            Console.WriteLine($"PASS: player blips match the every-frame version at all {compared} refreshes, rotate every frame in between "
                + $"(game calls per frame: {(double)quietCalls / quietFrames:0.0} between refreshes vs {(double)everyFrameCalls / 260:0.0} every frame before)");
        }
    }
}
'''

# --- snowball and visor keys ------------------------------------------------------------------
interactions = member(fc, r'^        private async Task AnimationsAndInteractions\(\).*?^        }$')
anim_test = r'''
namespace Interactions
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Harness;
    using static Harness.Assert;
    using static Interactions.Natives;

    public enum Control { Detonate, SwitchVisor, VehicleHeadlight }
    public enum Permission { WPSnowball }

    public static class S
    {
        public static FrameRuntime Runtime;
        public static int Calls, SnowReads, Pickups, VisorSwitches, HeadlightBlocks;
        public static bool MenuOpen, Snow = true, Allowed = true, Walking, InVehicle, OnBike;
        public static readonly HashSet<Control> Pressed = new HashSet<Control>(), JustReleased = new HashSet<Control>();
    }
    public class Ped
    {
        public int Handle => 7;
        public bool IsDead { get { S.Calls++; return false; } }
        public bool IsInVehicle() { S.Calls++; return S.InVehicle; }
        public bool IsOnFoot { get { S.Calls++; return !S.InVehicle; } }
        public bool IsWalking { get { S.Calls++; return S.Walking; } }
        public bool IsInParachuteFreeFall => Flag(); public bool IsFalling => Flag(); public bool IsBeingStunned => Flag();
        public bool IsRunning => Flag(); public bool IsSprinting => Flag(); public bool IsSwimming => Flag();
        public bool IsSwimmingUnderWater => Flag(); public bool IsDiving => Flag();
        static bool Flag() { S.Calls++; return false; }
    }
    public class Model { public bool IsBike => S.OnBike; public bool IsBicycle => false; public bool IsQuadbike => false; }
    public class Vehicle { public Model Model = new Model(); }
    public static class Game
    {
        public static Ped PlayerPed { get { S.Calls += 2; return new Ped(); } }
        public static bool IsPaused { get { S.Calls++; return false; } }
        public static bool IsControlJustReleased(int group, Control control) { S.Calls++; return S.JustReleased.Contains(control); }
        public static bool IsControlPressed(int group, Control control) { S.Calls++; return S.Pressed.Contains(control); }
        public static void DisableControlThisFrame(int group, Control control) { S.Calls++; if (control == Control.VehicleHeadlight) { S.HeadlightBlocks++; } }
    }
    public static class MenuController { public static bool IsAnyMenuOpen() => S.MenuOpen; }
    public static class MainMenu { public static bool DontOpenMenus => false; }
    public static class Fading { public static bool IsFadedIn { get { S.Calls++; return true; } } }
    public static class EventManager { public static bool IsSnowEnabled { get { S.Calls++; S.SnowReads++; return S.Snow; } } }

    public static class Natives
    {
        public static Task Delay(int ms) => S.Runtime.Delay(ms);
        public static int GetGameTimer() { S.Calls++; return S.Runtime.Now; }
        public static bool IsAllowed(Permission permission) => S.Allowed;
        public static bool IsPlayerSwitchInProgress() { S.Calls++; return false; }
        public static int GetInteriorFromEntity(int entity) { S.Calls++; return 0; }
        public static int GetSelectedPedWeapon(int ped) { S.Calls++; return 1; }
        public static int GetHashKey(string name) { S.Calls++; return 2; }
        public static Vehicle GetVehicle() { S.Calls += 3; return S.InVehicle ? new Vehicle() : null; }
    }

    public class Ticker
    {
        readonly uint snowball_hash = 3;
        Task PickupSnowballOnce() { S.Pickups++; return Task.CompletedTask; }
        async Task SwitchHelmetOnce() { S.VisorSwitches++; await Delay(100); }
''' + interactions + r'''
        public Task Tick() => AnimationsAndInteractions();
    }

    public static class Test
    {
        static int frame;
        static void Frames(int count, Action before = null)
        {
            for (var i = 0; i < count; i++)
            {
                S.JustReleased.Clear();
                before?.Invoke();
                S.Runtime.Frame(frame++ * 16);
            }
        }

        public static void Run()
        {
            S.Runtime = new FrameRuntime();
            S.Runtime.Add(new Ticker().Tick);

            S.Calls = 0;
            Frames(100);
            Check(S.Calls == 200 && S.SnowReads == 0, $"no key used: two key reads a frame and nothing else ({S.Calls} calls, {S.SnowReads} snow reads)");

            var once = true;
            Frames(5, () => { if (once) { S.JustReleased.Add(Control.Detonate); once = false; } });
            Check(S.Pickups == 1 && S.SnowReads == 1, "snowball key: one pickup, snow read once");

            S.Snow = false; once = true;
            Frames(5, () => { if (once) { S.JustReleased.Add(Control.Detonate); once = false; } });
            Check(S.Pickups == 1 && S.SnowReads == 2, "snow off: no pickup");
            S.Snow = true;

            S.Walking = true; once = true;
            Frames(5, () => { if (once) { S.JustReleased.Add(Control.Detonate); once = false; } });
            Check(S.Pickups == 1, "walking: no pickup (the old conditions still apply)");
            S.Walking = false;

            S.MenuOpen = true; once = true;
            Frames(5, () => { if (once) { S.JustReleased.Add(Control.Detonate); once = false; } });
            Check(S.Pickups == 1 && S.SnowReads == 3, "menu open: nothing happens, snow not read");
            S.MenuOpen = false;

            S.Pressed.Add(Control.SwitchVisor);
            Frames(20);
            Check(S.VisorSwitches == 0, "visor: not before the key is held 400 ms");
            Frames(10);
            S.Pressed.Clear();
            Frames(20);
            Check(S.VisorSwitches == 1, "visor: held past 400 ms switches once");

            S.InVehicle = true; S.OnBike = true;
            S.Pressed.Add(Control.SwitchVisor);
            Frames(30);
            S.Pressed.Clear();
            Frames(20);
            Check(S.VisorSwitches == 2 && S.HeadlightBlocks > 0, "visor on a bike: headlight key blocked while switching");
            Check(S.Pickups == 1 && S.SnowReads == 3, "the visor key never picks up a snowball or reads the snow setting");
            Console.WriteLine("PASS: snowball and visor keys (read first; pickup, snow setting, menu gate and visor unchanged)");
        }
    }
}
'''

# --- clothing glow ----------------------------------------------------------------------------
glow = region(fc, 'Player clothing animations controller.')
glow_test = r'''
namespace Glow
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Harness;
    using static Harness.Assert;
    using static Glow.Natives;

    public static class S
    {
        public static FrameRuntime Runtime;
        public static int Delays, OwnDecorWrites;
        public static readonly Dictionary<int, int> Decor = new Dictionary<int, int>();
        public static readonly List<string> Glow = new List<string>();
    }
    public static class Debug { public static void WriteLine(string text) { throw new Exception(text); } }
    public class Ped
    {
        public readonly int Handle;
        public Ped(int handle) { Handle = handle; }
        public bool Exists() => true;
        public bool IsDead => false;
    }
    public class Player { public Ped Character; }
    public static class Game { public static Ped PlayerPed => new Ped(1); }
    public static class PlayerAppearance { public static int ClothingAnimationType = 2; }
    public static class Natives
    {
        public static Task Delay(int ms) { S.Delays++; return S.Runtime.Delay(ms); }
        public static bool DecorIsRegisteredAsType(string name, int type) => true;
        public static void DecorRegister(string name, int type) { }
        public static void DecorSetInt(int ped, string name, int value) { S.OwnDecorWrites++; S.Decor[ped] = value; }
        public static bool DecorExistOn(int ped, string name) => S.Decor.ContainsKey(ped);
        public static int DecorGetInt(int ped, string name) => S.Decor[ped];
        public static void SetPedIlluminatedClothingGlowIntensity(int ped, float intensity) { S.Glow.Add($"{S.Runtime.Now}:{ped}:{intensity:0.00}"); }
    }

    public class Glower
    {
        public const string clothingAnimationDecor = "clothing_animation_type";
        private bool clothingAnimationReverse = false;
        private float clothingOpacity = 1f;
        public IEnumerable<Player> Players = new[] { new Player { Character = new Ped(1) }, new Player { Character = new Ped(2) }, new Player { Character = new Ped(3) } };
''' + glow + r'''
        public Task Tick() => PlayerClothingAnimationsController();
    }

    public static class Test
    {
        public static void Run()
        {
            S.Runtime = new FrameRuntime();
            S.Decor[2] = 0;  // solid
            S.Decor[3] = 3;  // flash
            S.Runtime.Add(new Glower().Tick);
            for (var frame = 0; frame < 63; frame++) { S.Runtime.Frame(frame * 16); }  // 1 s at 62.5 fps
            var passes = S.Glow.Count(g => g.Contains(":3:"));
            Check(passes == 21, $"a glow pass every 25 ms, plus the frame the runtime takes to restart the tick (every 3rd 16 ms frame, as before): {passes} passes");
            Check(S.Delays == passes && S.Runtime.Resumes >= passes - 1 && S.Runtime.Resumes <= passes, $"one wake-up per pass, not one per frame ({S.Delays} delays, {S.Runtime.Resumes} wake-ups)");
            Check(S.Glow.Count(g => g.Contains(":2:")) == 1, "a solid glow is applied once");
            Check(S.Glow.Count(g => g.Contains(":1:")) == passes, "your own fading glow is updated every pass");
            Check(S.OwnDecorWrites == 1, "your own decorator is written once");
            var fades = S.Glow.Where(g => g.Contains(":1:")).Select(g => float.Parse(g.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture)).ToList();
            Check(fades.Zip(fades.Skip(1), (x, y) => Math.Abs(x - y)).All(step => step <= 0.051f), "the fade moves 0.05 a pass");
            Console.WriteLine($"PASS: clothing glow ({passes} passes a second, one wake-up each; solid applied once; fade steps unchanged)");
        }
    }
}
'''

# --- shared per-frame tick --------------------------------------------------------------------
perframe = '\n'.join([
    region(fc, 'per-frame checks'),
    member(fc, r'^        private void VehicleHighbeamFlash\(\).*?^        }$'),
    member(fc, r'^        private void VehicleShowHealthOnScreen\(\).*?^        }$'),
    member(fc, r'^        private void DisableMovement\(\).*?^        }$'),
    member(fc, r'^        private bool IsMpCharEditorOpen\(\).*?^        }$'),
    member(fc, r'^        private void AnimalPedCameraChangeBlocker\(\).*?^        }$'),
])
controls = sorted(set(re.findall(r'Control\.(\w+)', perframe)))
perframe_test = r'''
namespace PerFrame
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Harness;
    using static Harness.Assert;
    using static PerFrame.Natives;

    public enum Control { ''' + ', '.join(controls) + r''' }
    public static class S
    {
        public static FrameRuntime Runtime;
        public static bool EditorOpen, InVehicle, Horn, HornReleased, BrokenVehicle;
        public static uint Model = 1;
        public static int Disabled, FirstPersonBlocks, Texts;
        public static bool? HighBeams;
        public static readonly List<string> Log = new List<string>();
    }
    public static class Debug { public static void WriteLine(string text) { S.Log.Add(text); } }
    public class Ped
    {
        public int Handle => 7;
        public static bool operator ==(Ped x, Ped y) => x?.Handle == y?.Handle;
        public static bool operator !=(Ped x, Ped y) => !(x == y);
        public override bool Equals(object o) => o is Ped p && p.Handle == Handle;
        public override int GetHashCode() => Handle;
    }
    public class Vehicle
    {
        public bool Exists() => true;
        public bool IsDead => false;
        public Ped Driver => new Ped();
        public bool IsEngineRunning => true;
        public bool AreHighBeamsOn { set { S.HighBeams = value; } }
        public float EngineHealth => 1000f;
        public float BodyHealth => 500f;
        public float PetrolTankHealth => 0f;
    }
    public class Menu { public bool Visible => S.EditorOpen; }
    public class MpMenus { public Menu appearanceMenu = new Menu(), faceShapeMenu = new Menu(), createCharacterMenu = new Menu(), inheritanceMenu = new Menu(), propsMenu = new Menu(), clothesMenu = new Menu(), tattoosMenu = new Menu(); }
    public class VehicleMenu { public bool FlashHighbeamsOnHonk = true, VehicleShowHealth = true; }
    public static class MainMenu { public static VehicleMenu VehicleOptionsMenu = new VehicleMenu(); public static MpMenus MpPedCustomizationMenu = new MpMenus(); }
    public static class Game
    {
        public static Ped PlayerPed => new Ped();
        public static bool IsControlPressed(int group, Control control) => S.Horn;
        public static bool IsControlJustReleased(int group, Control control) => S.HornReleased;
        public static void DisableControlThisFrame(int group, Control control) { S.Disabled++; }
    }
    public static class Natives
    {
        public static readonly HashSet<uint> AnimalHashes = new HashSet<uint> { 50, 51 };
        public static int PlayerPedId() => 7;
        public static int GetEntityModel(int entity) => (int)S.Model;
        public static void DisableFirstPersonCamThisFrame() { S.FirstPersonBlocks++; }
        public static bool IsPedInAnyVehicle(int ped, bool atGetIn) => S.InVehicle;
        public static bool IsPauseMenuActive() => false;
        public static Vehicle GetVehicle() { if (S.BrokenVehicle) { throw new InvalidOperationException("broken"); } return S.InVehicle ? new Vehicle() : null; }
        public static double Map(double value, double minIn, double maxIn, double minOut, double maxOut) => (value - minIn) * (maxOut - minOut) / (maxIn - minIn) + minOut;
        public static void DrawTextOnScreen(string text, float x, float y) { S.Texts++; }
    }

    public class Checks
    {
''' + perframe + r'''
        public Task Tick() => PerFrameChecks();
        public void EnableAll() { vehicleHealthCheckEnabled = highbeamFlashCheckEnabled = movementLockCheckEnabled = animalCameraCheckEnabled = true; }
    }

    public static class Test
    {
        static int frame;
        static void Frames(int count) { for (var i = 0; i < count; i++) { S.Runtime.Frame(frame++ * 16); } }
        static void Reset() { S.Disabled = S.FirstPersonBlocks = S.Texts = 0; S.HighBeams = null; S.Log.Clear(); }

        public static void Run()
        {
            S.Runtime = new FrameRuntime();
            var checks = new Checks();
            S.Runtime.Add(checks.Tick);

            Frames(10);
            Check(S.Disabled == 0 && S.FirstPersonBlocks == 0 && S.Texts == 0 && S.HighBeams == null, "checks off: nothing runs");
            checks.EnableAll();

            Reset(); Frames(10);
            Check(S.Disabled == 0 && S.FirstPersonBlocks == 0 && S.Texts == 0, "on foot, editor shut, human ped: nothing to do");

            Reset(); S.EditorOpen = true; Frames(10); S.EditorOpen = false;
            Check(S.Disabled == ''' + str(member(fc, r'^        private void DisableMovement\(\).*?^        }$').count('Game.DisableControlThisFrame(')) + r''' * 10, $"MP editor open: movement controls disabled every frame ({S.Disabled})");

            Reset(); S.Model = 50; Frames(5); S.Model = 51; Frames(5); S.Model = 1; Frames(5);
            Check(S.FirstPersonBlocks == 10, $"animal ped: first person blocked every frame, through a change of animal ({S.FirstPersonBlocks})");

            Reset(); S.InVehicle = true; Frames(10);
            Check(S.Texts == 30, "vehicle health: three lines every frame");
            S.Horn = true; Frames(1);
            Check(S.HighBeams == true, "honking turns the high beams on");
            S.Horn = false; S.HornReleased = true; Frames(1); S.HornReleased = false;
            Check(S.HighBeams == false, "letting go turns them off");

            Reset(); S.BrokenVehicle = true; S.EditorOpen = true; S.Model = 50; Frames(3); S.BrokenVehicle = false; S.EditorOpen = false; S.Model = 1;
            Check(S.Log.Count == 6 && S.Disabled > 0 && S.FirstPersonBlocks == 3, "a failing check is logged and the others still run");
            Console.WriteLine("PASS: shared per-frame tick (movement lock, animal camera, health text, highbeams; errors isolated)");
        }
    }
}
'''

program = runtime + blip_test + anim_test + glow_test + perframe_test + r'''
static class Program
{
    static void Main()
    {
        Blips.Test.Run();
        Interactions.Test.Run();
        Glow.Test.Run();
        PerFrame.Test.Run();
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-frame-costs-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable><NoWarn>CS1998;CS0162;CS0414;CS0649;CS0660;CS0661;CS8321</NoWarn></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
