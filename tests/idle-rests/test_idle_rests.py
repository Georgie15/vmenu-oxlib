"""Idle rests: the busier loops do what build-2026-11-17-3's every-frame versions did.

Release H makes vMenu's loops rest (wait 100-250 ms between runs) whenever nothing they do has to
happen every frame, instead of being started by FiveM's C# runtime every frame. This test compiles
build-2026-11-17-3's MiscSettings, MiscRecordingKeybinds, PlayerOptions, WeaponOptions and
VoiceChat (from git) and this checkout's versions of the same methods plus RadarSettings, runs
both against the same fake game, frame by frame, through a long scripted and randomised session,
and compares what each version does to the game every frame:

- everything players see or feel (draws, controls, pointing, drift, recording, teleport, radar
  hiding, the expanded map key, god mode, no ragdoll, super jump, invisibility, never wanted,
  weapon options, voice chat) must match on every frame;
- settings that persist once set and that the new version re-applies 4 times a second instead of
  every frame (the pause menu radar preference, the expanded radar option, voice chat off) may
  lag by up to 600 ms after something outside vMenu changes them, and must then match again.

Options are switched while the menu is open, as players do. It also checks that the new loops
really rest while idle, and that every option and every key still gets every frame.

Release M: with every option off, MiscSettings, MiscRecordingKeybinds, PlayerOptions and
WeaponOptions take themselves off the tick list (TickSleeper) instead of resting, and come back
when a menu opens (IdleRest.Woken) or when GeneralTasks' 100 ms check finds work (emulated here).
God mode, no ragdoll, invisibility, never wanted, no reload (now the game's infinite clip flag),
the parachutes and the expanded radar set things the game keeps, so the new loops re-apply them
4 times a second: those are compared as game state, after a 300 ms settle when something outside
vMenu (a respawn, a wanted level, a lost parachute) resets them. Super jump, unlimited ammo, hide
radar, the camera locks and the recording keys still run every frame, and must match call for call.

Requires Python, git (the old version is read from the build-2026-11-17-3 tag) and the .NET 9 SDK.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
OLD_REF = 'build-2026-11-17-3'
new_fc = (root / 'vMenu/FunctionsController.cs').read_text(encoding='utf-8-sig')
old_fc = subprocess.run(['git', 'show', f'{OLD_REF}:vMenu/FunctionsController.cs'], cwd=root,
                        capture_output=True, check=True).stdout.decode('utf-8-sig')
latch = (root / 'vMenu/OverrideLatch.cs').read_text(encoding='utf-8-sig')
latch = latch[latch.index('internal sealed class OverrideLatch'):latch.rindex('}')]


def member(source, name):
    match = re.search(r'^        (?:private|internal|public)[^\n(]*\b' + name + r'\(\)\s*\n        \{.*?^        \}$', source, re.M | re.S)
    assert match, f'{name} not found'
    return match[0]


def field_lines(source):
    names = r'(?:cameraRotationHeading|radarSwitchTimer|vMenuOpenedBigmap|lastPressedPoint|voiceTimer|voiceCycle|voiceIndicator\w*|expandedRadarProfile|\w+Latch)'
    lines = re.findall(r'^        (?:private |internal |public )?(?:readonly |const |static )*[\w<>]+ ' + names + r'(?: = [^;]+)?;$', source, re.M)
    return '\n'.join(lines)


METHODS = ['MiscSettings', 'MiscRecordingKeybinds', 'PlayerOptions', 'WeaponOptions', 'VoiceChat']


def apply_override(source):
    return re.search(r'^        private static void ApplyOverride\(.*?^        }$', source, re.M | re.S)[0]


def klass(name, source, extra):
    body = '\n'.join([field_lines(source), apply_override(source)] + [member(source, m) for m in METHODS + extra])
    calls = '\n'.join(f'        public Task Call{m}() => {m}();' for m in METHODS + extra)
    return f'''
    public class {name}
    {{
        IEnumerable<FakePlayer> Players => W.Players;
        void DrawMiscSettingsText() {{ var m = MainMenu.MiscSettingsMenu; if (m.ShowCoordinates) Fx("DrawCoords"); if (m.ShowLocation) Fx("DrawLocation"); if (m.DrawTimeOnScreen) Fx("DrawTime"); if (m.ShowSpeedoKmh && W.InVehicle) Fx("DrawKmh"); if (m.ShowSpeedoMph && W.InVehicle) Fx("DrawMph"); }}
{body}
{calls}
    }}
'''


for name, source in (('old', old_fc), ('new', new_fc)):
    for m in METHODS:
        member(source, m)
assert 'private async Task RadarSettings()' in new_fc

controls = sorted(set(re.findall(r'Control\.(\w+)', '\n'.join(member(s, m) for s in (old_fc, new_fc) for m in METHODS))) | {'TpKey'})
permissions = sorted(set(re.findall(r'Permission\.(\w+)', '\n'.join(member(s, m) for s in (old_fc, new_fc) for m in METHODS))))

program = r'''
namespace Rests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using vMenuClient;
    using static Rests.Natives;

    public enum Control { ''' + ', '.join(controls) + r''' }
    public enum Permission { ''' + ', '.join(permissions) + r''' }
    public enum Setting { vmenu_handle_invisibility }
    public enum InputMode { MouseAndKeyboard, GamePad }
    public enum Alignment { Left, Center, Right }
    public enum HudComponent { StreetName = 9 }
    public enum WeaponHash : uint { Unarmed = 0xA2719263, Minigun = 0x42BF8A85, Parachute = 0xFBAB5776, Pistol = 0x1B06D571 }

    // FiveM's C# runtime, as in tests/frame-costs: each frame, Delays that are due resume, then
    // every tick whose last run has finished starts again. Delay(0) resumes next frame.
    public sealed class FrameRuntime
    {
        public int Now;
        readonly List<KeyValuePair<int, TaskCompletionSource<bool>>> delays = new List<KeyValuePair<int, TaskCompletionSource<bool>>>();
        readonly List<Func<Task>> ticks = new List<Func<Task>>();
        readonly List<Task> running = new List<Task>();
        // Added and removed between frames, as FiveM does: a tick added while a frame runs starts on the
        // next one (TickSleeper's Wake), and one removed while it runs finishes that run.
        bool inFrame;
        readonly List<Func<Task>> toAdd = new List<Func<Task>>(), toRemove = new List<Func<Task>>();
        public void Add(Func<Task> tick) { if (inFrame) { toAdd.Add(tick); } else { ticks.Add(tick); running.Add(null); } }
        public void Remove(Func<Task> tick) { if (inFrame) { toRemove.Add(tick); } else { RemoveNow(tick); } }
        void RemoveNow(Func<Task> tick) { var i = ticks.IndexOf(tick); if (i >= 0) { ticks.RemoveAt(i); running.RemoveAt(i); } }
        public Task Delay(int ms)
        {
            var tcs = new TaskCompletionSource<bool>();
            delays.Add(new KeyValuePair<int, TaskCompletionSource<bool>>(Now + Math.Max(ms, 1), tcs));
            return tcs.Task;
        }
        public void Frame(int now)
        {
            Now = now;
            inFrame = true;
            var waiting = running.ConvertAll(t => t != null && !t.IsCompleted);
            var due = delays.FindAll(d => d.Key <= now);
            delays.RemoveAll(d => d.Key <= now);
            foreach (var d in due) { d.Value.SetResult(true); }
            for (var i = 0; i < ticks.Count; i++)
            {
                if (running[i] != null && running[i].IsFaulted) { throw running[i].Exception; }
                if (running[i] == null || (running[i].IsCompleted && !waiting[i])) { running[i] = ticks[i](); }
            }
            inFrame = false;
            foreach (var tick in toRemove) { RemoveNow(tick); }
            toRemove.Clear();
            foreach (var tick in toAdd) { ticks.Add(tick); running.Add(null); }
            toAdd.Clear();
        }
    }

    public class Settings
    {
        // Misc settings
        public bool HideRadar, LockCameraX, LockCameraY, KbTpToWaypoint, KbDriftMode, KbPointKeys, KbRadarKeys, KbRecordKeys,
            ShowCoordinates, ShowLocation, DrawTimeOnScreen, ShowSpeedoKmh, ShowSpeedoMph;
        public int KbTpToWaypointKey = (int)Control.TpKey;
        // Player options
        public bool PlayerGodMode, PlayerNoRagdoll, PlayerInvisible, PlayerSuperJump, PlayerNeverWanted;
        // Weapon options
        public bool NoReload, UnlimitedAmmo, AutoEquipChute, UnlimitedParachutes;
        // Voice chat
        public bool EnableVoicechat, ShowCurrentSpeaker, ShowVoiceStatus;
        public float currentProximity = 5f;
        public List<string> channels = new List<string> { "None", "1", "2" };
        public string currentChannel = "None";

        // Release J: the teleport-to-waypoint, drift, point and minimap keys are FiveM key bindings,
        // and coordinates, location, time and speed are shown on the web page, so MiscSettings no
        // longer does either; tests/key-bindings and tests/hud-overlay cover them. They stay off here.
        public static readonly string[] Toggles =
        {
            "HideRadar", "LockCameraX", "LockCameraY", "KbRecordKeys",
            "PlayerGodMode", "PlayerNoRagdoll", "PlayerInvisible", "PlayerSuperJump", "PlayerNeverWanted",
            "NoReload", "UnlimitedAmmo", "AutoEquipChute", "UnlimitedParachutes",
            "EnableVoicechat", "ShowCurrentSpeaker", "ShowVoiceStatus",
        };
        public void Set(string name, bool value) => GetType().GetField(name).SetValue(this, value);
        public bool Get(string name) => (bool)GetType().GetField(name).GetValue(this);
        public bool AnyOn => Toggles.Any(Get);
    }

    public class Menu { public bool Visible; }
    public class MpMenus { public Menu appearanceMenu = new Menu(), faceShapeMenu = new Menu(), createCharacterMenu = new Menu(), inheritanceMenu = new Menu(), propsMenu = new Menu(), clothesMenu = new Menu(), tattoosMenu = new Menu(); }
    public static class MainMenu
    {
        public static Settings MiscSettingsMenu => W.Settings;
        public static Settings PlayerOptionsMenu => W.Settings;
        public static Settings WeaponOptionsMenu => W.Settings;
        public static Settings VoiceChatSettingsMenu => W.Settings;
        public static readonly MpMenus MpPedCustomizationMenu = new MpMenus();
    }
    public static class MenuController { public static bool IsAnyMenuOpen() => W.MenuOpen; }
    public static class Fading { public static bool IsFadedIn => true; }
    public static class Notify
    {
        public static void Success(string m) => Fx("Notify.Success", m);
        public static void Error(string m) => Fx("Notify.Error", m);
        public static void Custom(string m) => Fx("Notify.Custom", m);
        public static void Alert(string m) => Fx("Notify.Alert", m);
    }
    public static class HelpMessage { public static void Custom(string m) => Fx("Help", m); }

    public class StateBag { public object this[string key] => key == "activeOrg" ? W.ActiveOrg : null; }
    public class Player { public int Handle => 0; public StateBag State { get; } = new StateBag(); }
    public class FakePlayer { public int Handle; public string Name; }
    public class WeaponSlot { public WeaponHash Hash => (WeaponHash)(uint)W.Weapon; }
    public class Weapons { public WeaponSlot Current => new WeaponSlot(); }
    public class Ped
    {
        public int Handle => W.Ped;
        public bool IsInVehicle() => W.InVehicle;
        public Weapons Weapons => new Weapons();
    }
    public class Vehicle { public int Handle => 900; public bool IsDead => false; public bool Exists() => true; }
    public static class Game
    {
        public static Ped PlayerPed => new Ped();
        public static Player Player => new Player();
        public static InputMode CurrentInputMode => W.Mode;
        public static bool IsWaypointActive => W.Waypoint;
        public static bool IsControlPressed(int g, Control c) => W.Pressed.Contains(c);
        public static bool IsControlJustReleased(int g, Control c) => W.JustReleased.Contains(c);
        public static bool IsControlJustPressed(int g, Control c) => W.JustPressed.Contains(c);
        public static bool IsControlEnabled(int g, Control c) => true;
        public static void DisableControlThisFrame(int g, Control c) => Fx("DisableControlThisFrame", c);
    }

    public class World
    {
        public string Name;
        public readonly FrameRuntime Runtime = new FrameRuntime();
        public readonly Settings Settings = new Settings();
        public readonly List<string> Effects = new List<string>();
        public readonly Dictionary<string, int> Runs = new Dictionary<string, int>();
        public readonly List<FakePlayer> Players = new List<FakePlayer> { new FakePlayer { Handle = 0, Name = "you" }, new FakePlayer { Handle = 1, Name = "them" } };
        public readonly HashSet<Control> Pressed = new HashSet<Control>(), JustReleased = new HashSet<Control>(), JustPressed = new HashSet<Control>();
        // MainMenu's per-frame watcher ends every idle rest when a menu opens (IdleRest.MenuOpened).
        public void OpenMenu() { MenuOpen = true; vMenuClient.IdleRest.MenuOpened(); }
        public bool MenuOpen, InVehicle, InHeli, Waypoint, Paused, RadarShown = true, RadarPref = true, ExpandedPref, Bigmap,
            Pointing, Recording, HasChute, Reserve, Keyboard, DictLoaded, DriveToWp, DriveWander;
        public bool? VoiceActive;
        public InputMode Mode;
        public int Ped = 100, Weapon = unchecked((int)WeaponHash.Unarmed), Wanted, MaxWanted = 5, NextNotification = 1;
        public readonly HashSet<int> Talking = new HashSet<int>();
        public object ActiveOrg;
        // What the game keeps per ped, for comparing the options re-applied 4 times a second.
        public readonly Dictionary<int, bool> Invincible = new Dictionary<int, bool>(), Visible = new Dictionary<int, bool>(),
            Ragdoll = new Dictionary<int, bool>(), InfiniteClip = new Dictionary<int, bool>();
        public bool ClipToppedThisFrame;   // the old no reload refilled the clip every frame
        public bool Is(Dictionary<int, bool> flags, bool otherwise) => flags.TryGetValue(Ped, out var on) ? on : otherwise;
        public bool NoReloadOn => Name == "old" ? ClipToppedThisFrame : Is(InfiniteClip, false);
        public string Kept => $"invincible {Is(Invincible, false)}, visible {Is(Visible, true)}, ragdoll {Is(Ragdoll, true)}, wanted {Wanted}/{MaxWanted}, "
            + $"no reload {NoReloadOn}, chute {HasChute}, reserve {Reserve}, map {Bigmap}, autopilot {DriveToWp}";
    }

    public static class Natives
    {
        public static World W;
        static string Show(object a) => a is float f ? f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : a is bool b ? (b ? "true" : "false") : $"{a}";
        public static void Fx(string name, params object[] args) => W.Effects.Add($"{name}({string.Join(",", args.Select(Show))})");

        public static bool DriveToWpTaskActive { get => W.DriveToWp; set => W.DriveToWp = value; }
        public static bool DriveWanderTaskActive { get => W.DriveWander; set => W.DriveWander = value; }
        public static Task Delay(int ms) => W.Runtime.Delay(ms);
        public static bool IsAllowed(Permission p) => true;
        public static bool GetSettingsBool(Setting s) => true;
        public static Vehicle GetVehicle() => W.InVehicle ? new Vehicle() : null;
        public static Task<bool> LoadAnimDictAsync(string dict) => Task.FromResult(true);
        public static void TeleportToWp() => Fx("TeleportToWp");
        public static float GetPointingPitch() => 1f;
        public static float GetPointingHeading() => 2f;
        public static bool GetPointingIsBlocked() => false;
        public static void DrawTextOnScreen(string text, float x, float y, float size, Alignment a, int font) => Fx("DrawTextOnScreen", text);

        // Queries
        public static int GetGameTimer() => W.Runtime.Now;
        public static int PlayerPedId() => W.Ped;
        public static int PlayerId() => 0;
        public static bool DoesEntityExist(int e) => e > 0;
        public static bool IsRadarHidden() => !W.RadarShown;
        public static bool IsRadarPreferenceSwitchedOn() => W.RadarPref;
        public static int GetProfileSetting(int s) => s == 221 && W.ExpandedPref ? 1 : 0;
        public static bool IsBigmapActive() => W.Bigmap;
        public static bool IsPedPointing(int ped) => W.Pointing;
        public static int GetFollowPedCamViewMode() => 1;
        public static int UpdateOnscreenKeyboard() => W.Keyboard ? 0 : 1;
        public static bool IsPauseMenuActive() => W.Paused;
        public static bool IsScreenFadedIn() => true;
        public static bool IsPlayerSwitchInProgress() => false;
        public static bool IsRecording() => W.Recording;
        public static int GetPlayerWantedLevel(int player) => W.Wanted;
        public static int GetMaxWantedLevel() => W.MaxWanted;
        public static int GetSelectedPedWeapon(int ped) => W.Weapon;
        public static bool HasPedGotWeapon(int ped, uint hash, bool b) => W.HasChute;
        public static bool IsPedInAnyHeli(int ped) => W.InHeli;
        public static bool IsPedInAnyPlane(int ped) => false;
        public static bool GetPlayerHasReserveParachute(int player) => W.Reserve;
        public static bool NetworkIsPlayerTalking(int player) => W.Talking.Contains(player);
        public static bool HasStreamedTextureDictLoaded(string dict) => W.DictLoaded;
        public static int GetHashKey(string s) => s.GetHashCode();

        // Effects
        public static void DisplayRadar(bool on) { Fx("DisplayRadar", on); W.RadarShown = on; }
        public static void SetBigmapActive(bool on, bool full) { Fx("SetBigmapActive", on, full); W.Bigmap = on; }
        public static void SetGameplayCamRelativePitch(float p, float s) => Fx("SetGameplayCamRelativePitch", p, s);
        public static void SetGameplayCamRelativeHeading(float h) => Fx("SetGameplayCamRelativeHeading", h);
        public static void SetVehicleReduceGrip(int veh, bool on) => Fx("SetVehicleReduceGrip", veh, on);
        public static bool IsPedInAnyVehicle(int ped, bool atGetIn) => W.InVehicle;
        public static void HideHudComponentThisFrame(int component) => Fx("HideHudComponentThisFrame", component);
        public static void ClearPedSecondaryTask(int ped) { Fx("ClearPedSecondaryTask", ped); W.Pointing = false; }
        public static void TaskMoveNetwork(int ped, string task, float m, bool b, string dict, int flags) { Fx("TaskMoveNetwork", ped, task); W.Pointing = true; }
        public static void RemoveAnimDict(string dict) => Fx("RemoveAnimDict", dict);
        public static void SetTaskMoveNetworkSignalFloat(int ped, string s, float v) => Fx("SignalFloat", s, v);
        public static void SetTaskMoveNetworkSignalBool(int ped, string s, bool v) => Fx("SignalBool", s, v);
        public static void StartRecording(int mode) { Fx("StartRecording", mode); W.Recording = true; }
        public static void StopRecording() { Fx("StopRecording"); W.Recording = false; }
        public static void StopRecordingAndDiscardClip() { Fx("StopRecordingAndDiscardClip"); W.Recording = false; }
        public static void SetNotificationTextEntry(string s) => Fx("SetNotificationTextEntry", s);
        public static int DrawNotificationWithButton(int t, string button, string text) { Fx("DrawNotificationWithButton", button, text); return W.NextNotification++; }
        public static void RemoveNotification(int id) => Fx("RemoveNotification", id);
        public static void SetEntityInvincible(int e, bool on) { Fx("SetEntityInvincible", e, on); W.Invincible[e] = on; }
        public static void SetEntityVisible(int e, bool on, bool b) { Fx("SetEntityVisible", e, on); W.Visible[e] = on; }
        public static void SetSuperJumpThisFrame(int player) => Fx("SetSuperJumpThisFrame", player);
        public static void SetPedCanRagdoll(int ped, bool on) { Fx("SetPedCanRagdoll", ped, on); W.Ragdoll[ped] = on; }
        public static void ClearPlayerWantedLevel(int player) { Fx("ClearPlayerWantedLevel"); W.Wanted = 0; }
        public static void SetMaxWantedLevel(int level) { Fx("SetMaxWantedLevel", level); W.MaxWanted = level; }
        public static void ClearPedTasks(int ped) => Fx("ClearPedTasks", ped);
        public static void SetAmmoInClip(int ped, uint hash, int ammo) { Fx("SetAmmoInClip", hash, ammo); W.ClipToppedThisFrame = true; }
        public static void SetPedInfiniteAmmoClip(int ped, bool on) { Fx("SetPedInfiniteAmmoClip", ped, on); W.InfiniteClip[ped] = on; }
        public static void SetPedInfiniteAmmo(int ped, bool on, uint hash) => Fx("SetPedInfiniteAmmo", ped, on, hash);
        public static void GiveWeaponToPed(int ped, uint hash, int ammo, bool a, bool b) { Fx("GiveWeaponToPed", hash, ammo); if (hash == (uint)WeaponHash.Parachute) { W.HasChute = true; } }
        public static void SetPlayerHasReserveParachute(int player) { Fx("SetPlayerHasReserveParachute"); W.Reserve = true; }
        public static void SetPlayerCanLeaveParachuteSmokeTrail(int ped, bool on) => Fx("SetPlayerCanLeaveParachuteSmokeTrail", on);
        public static void NetworkSetVoiceActive(bool on) { Fx("NetworkSetVoiceActive", on); W.VoiceActive = on; }
        public static void NetworkSetTalkerProximity(float p) => Fx("NetworkSetTalkerProximity", p);
        public static void NetworkClearVoiceChannel() => Fx("NetworkClearVoiceChannel");
        public static void NetworkSetVoiceChannel(int c) => Fx("NetworkSetVoiceChannel", c);
        public static void RequestStreamedTextureDict(string d, bool b) { Fx("RequestStreamedTextureDict", d); W.DictLoaded = true; }
        public static void SetStreamedTextureDictAsNoLongerNeeded(string d) { Fx("SetStreamedTextureDictAsNoLongerNeeded", d); W.DictLoaded = false; }
        public static void DrawSprite(string dict, string name, float x, float y, float w, float h, float r, int cr, int cg, int cb, int ca) => Fx("DrawSprite", name);
    }

''' + klass('Old', old_fc, []) + klass('New', new_fc, ['RadarSettings']).replace(
    'IEnumerable<FakePlayer> Players => W.Players;',
    'IEnumerable<FakePlayer> Players => W.Players;\n        const int IdleRestMs = 250;\n        const int StateRestMs = 100;\n        internal TickSleeper miscSettingsSleeper, recordingKeysSleeper, playerOptionsSleeper, weaponOptionsSleeper;\n        public bool WantsMisc() => MiscSettingsWanted();\n        public static bool WantsPlayer() => PlayerOptionsWanted();\n        public static bool WantsWeapons() => WeaponOptionsWanted();\n' + '\n'.join(member(new_fc, n) for n in ('PlayerOptionsWanted', 'WeaponOptionsWanted', 'MiscSettingsWanted')) + '\n        static bool AnyMenuOpen => MenuController.IsAnyMenuOpen();\n        static Task Rest(int ms) => vMenuClient.IdleRest.Until(Delay(ms));') + r'''

    public static class Test
    {
        static int failures;
        static void Check(bool ok, string message) { if (!ok) { failures++; if (failures <= 15) { Console.WriteLine("FAIL: " + message); } } }

        static Func<Task> Counted(World w, string name, Func<Task> tick) => () => { w.Runs[name] = w.Runs.TryGetValue(name, out var n) ? n + 1 : 1; return tick(); };

        // Effects compared only as state: re-applied settings that persist.
        static bool Guard(World w, string effect) =>
            (effect.StartsWith("DisplayRadar(") && !w.Settings.HideRadar)
            || (!w.Settings.EnableVoicechat && (effect == "NetworkSetVoiceActive(false)" || effect == "NetworkClearVoiceChannel()"));

        public static void Run()
        {
            World a = new World { Name = "old" }, b = new World { Name = "new" };
            var oldTicks = new Old(); var newTicks = new New();
            a.Runtime.Add(Counted(a, "MiscSettings", () => oldTicks.CallMiscSettings()));
            a.Runtime.Add(Counted(a, "MiscRecordingKeybinds", () => oldTicks.CallMiscRecordingKeybinds()));
            a.Runtime.Add(Counted(a, "PlayerOptions", () => oldTicks.CallPlayerOptions()));
            a.Runtime.Add(Counted(a, "WeaponOptions", () => oldTicks.CallWeaponOptions()));
            a.Runtime.Add(Counted(a, "VoiceChat", () => oldTicks.CallVoiceChat()));
            // Release M: four of them sleep while idle (see Sleeping loops in FunctionsController).
            var sleepers = new List<KeyValuePair<TickSleeper, Func<bool>>>();
            TickSleeper Sleeping(string name, Func<Task> loop, Func<bool> wanted)
            {
                var tick = Counted(b, name, loop);
                var sleeper = new TickSleeper(tick, t => b.Runtime.Add(t), t => b.Runtime.Remove(t));
                sleepers.Add(new KeyValuePair<TickSleeper, Func<bool>>(sleeper, wanted));
                b.Runtime.Add(tick);
                return sleeper;
            }
            newTicks.miscSettingsSleeper = Sleeping("MiscSettings", () => newTicks.CallMiscSettings(), () => { W = b; return newTicks.WantsMisc(); });
            b.Runtime.Add(Counted(b, "RadarSettings", () => newTicks.CallRadarSettings()));
            newTicks.recordingKeysSleeper = Sleeping("MiscRecordingKeybinds", () => newTicks.CallMiscRecordingKeybinds(), () => { W = b; return b.Settings.KbRecordKeys; });
            newTicks.playerOptionsSleeper = Sleeping("PlayerOptions", () => newTicks.CallPlayerOptions(), () => { W = b; return New.WantsPlayer(); });
            newTicks.weaponOptionsSleeper = Sleeping("WeaponOptions", () => newTicks.CallWeaponOptions(), () => { W = b; return New.WantsWeapons(); });
            b.Runtime.Add(Counted(b, "VoiceChat", () => newTicks.CallVoiceChat()));
            // A menu opening, or RadarSettings seeing the expanded radar, wakes every sleeping loop.
            vMenuClient.IdleRest.Woken += () => { foreach (var s in sleepers) { if (s.Key.Asleep) { s.Key.Wake(); } } };

            var random = new Random(20261117);
            var frame = 0;
            var settleUntil = 0;       // radar, expanded radar and voice re-checks may lag until then
            var keptSettleUntil = 0;   // the options re-applied 4 times a second may lag until then
            string lastInputs = null;
            int compared = 0, guarded = 0, maxResting = 0, keptCompared = 0;
            // What the new loops re-apply 4 times a second instead of every frame (release M): compared
            // as the state the game keeps, not call by call. Auto-pilot's end notices ride on PlayerOptions.
            string[] kept = { "SetEntityInvincible(", "SetEntityVisible(", "SetPedCanRagdoll(", "ClearPlayerWantedLevel(", "SetMaxWantedLevel(",
                "SetAmmoInClip(", "SetPedInfiniteAmmoClip(", "GiveWeaponToPed(", "SetPlayerHasReserveParachute(", "SetPlayerCanLeaveParachuteSmokeTrail(",
                "SetBigmapActive(", "ClearPedTasks(", "Notify." };
            bool Kept(string e) => kept.Any(e.StartsWith);

            void Both(Action<World> change) { change(a); change(b); }
            void Step(Action<World> inputs = null)
            {
                var now = 100000 + frame * 16;
                foreach (var w in new[] { a, b })
                {
                    w.JustReleased.Clear(); w.JustPressed.Clear();
                    inputs?.Invoke(w);
                    // Something outside vMenu reset what the game keeps (a respawn, a wanted level, a
                    // lost parachute, getting into a helicopter, the waypoint or duty): the new loops
                    // put it back within 250 ms.
                    if (w == a)
                    {
                        var state = $"{w.Ped},{w.Wanted},{w.HasChute},{w.Reserve},{w.InHeli},{w.Waypoint},{w.ActiveOrg}";
                        if (lastInputs != null && state != lastInputs) { keptSettleUntil = now + 300; }
                    }
                    w.Effects.Clear();
                    w.ClipToppedThisFrame = false;
                    W = w;
                    w.Runtime.Frame(now);
                }
                lastInputs = $"{a.Ped},{a.Wanted},{a.HasChute},{a.Reserve},{a.InHeli},{a.Waypoint},{a.ActiveOrg}";
                // GeneralTasks, every 100 ms: sleeping loops with work now are woken.
                if (frame % 6 == 0)
                {
                    foreach (var s in sleepers) { if (s.Key.Asleep && s.Value()) { s.Key.Wake(); } }
                }
                if (now >= keptSettleUntil)
                {
                    Check(a.Kept == b.Kept, $"frame {frame}: what the game keeps\n  old: {a.Kept}\n  new: {b.Kept}");
                    keptCompared++;
                }
                var ea = a.Effects.Where(e => !Guard(a, e) && !Kept(e)).OrderBy(e => e).ToList();
                var eb = b.Effects.Where(e => !Guard(b, e) && !Kept(e)).OrderBy(e => e).ToList();
                if (now < settleUntil)
                {
                    ea.RemoveAll(e => e.StartsWith("SetBigmapActive("));
                    eb.RemoveAll(e => e.StartsWith("SetBigmapActive("));
                    guarded++;
                }
                else
                {
                    Check(a.RadarShown == b.RadarShown, $"frame {frame}: radar shown {a.RadarShown} (old) vs {b.RadarShown} (new)");
                    Check(a.VoiceActive == b.VoiceActive || b.VoiceActive == null || a.VoiceActive == null, $"frame {frame}: voice active {a.VoiceActive} vs {b.VoiceActive}");
                    Check(a.Bigmap == b.Bigmap, $"frame {frame}: expanded map {a.Bigmap} (old) vs {b.Bigmap} (new)");
                }
                Check(ea.SequenceEqual(eb), $"frame {frame}: effects differ\n  old: {string.Join(" ", ea)}\n  new: {string.Join(" ", eb)}");
                compared++;
                maxResting = Math.Max(maxResting, vMenuClient.IdleRest.Resting);
                frame++;
            }
            void Steps(int count, Action<World> inputs = null) { for (var i = 0; i < count; i++) { Step(inputs); } }

            // 1. Idle: every option off, menu closed. The new loops rest.
            Steps(20);
            var before = new Dictionary<string, int>(b.Runs);
            Steps(125);
            foreach (var name in new[] { "MiscSettings", "MiscRecordingKeybinds", "PlayerOptions", "WeaponOptions" })
            {
                var runs = b.Runs[name] - before[name];
                Check(runs == 0, $"idle: {name} ran {runs} times in 125 frames (2 s); asleep, it should not run at all");
            }
            Check(sleepers.All(s => s.Key.Asleep), "idle: the four option loops are off the tick list");
            foreach (var name in new[] { "VoiceChat", "RadarSettings" })
            {
                var runs = b.Runs[name] - before[name];
                Check(runs <= 10, $"idle: {name} ran {runs} times in 125 frames (2 s); it should rest 250 ms at a time");
            }
            Check(a.Runs["MiscSettings"] - 20 >= 120, "the old MiscSettings ran every frame (sanity)");

            // 2. Each option on its own, switched in the open menu, then used with the menu closed.
            foreach (var option in Settings.Toggles)
            {
                // Switched two frames after the menu opens: opening it ends every rest.
                Both(w => w.OpenMenu()); Steps(2);
                Both(w => w.Settings.Set(option, true)); Steps(5);
                Both(w => w.MenuOpen = false);
                var runsBefore = new Dictionary<string, int>(b.Runs);
                var oldRunsBefore = new Dictionary<string, int>(a.Runs);
                Steps(40, w => Script(w, frame, option));
                // Whatever this option drives must still run every frame (its loop does not rest):
                // started as often as the old one, which ran every frame. (A loop busy across
                // frames, like holding the controller's recording button, is started less often.)
                var loop = option == "KbRecordKeys" ? "MiscRecordingKeybinds"
                    : option.StartsWith("Player") ? "PlayerOptions"
                    : new[] { "NoReload", "UnlimitedAmmo", "AutoEquipChute", "UnlimitedParachutes" }.Contains(option) ? "WeaponOptions"
                    : new[] { "EnableVoicechat", "ShowCurrentSpeaker", "ShowVoiceStatus" }.Contains(option) ? "VoiceChat"
                    : "MiscSettings";
                if (option == "ShowCurrentSpeaker" || option == "ShowVoiceStatus")
                {
                    // Only act with voice chat on; they ride on it.
                }
                else if (new[] { "PlayerGodMode", "PlayerNoRagdoll", "PlayerInvisible", "PlayerNeverWanted", "NoReload", "AutoEquipChute", "UnlimitedParachutes" }.Contains(option))
                {
                    // Release M: set things the game keeps, re-applied 4 times a second (640 ms here).
                    var newRuns = b.Runs[loop] - runsBefore[loop];
                    var oldRuns = a.Runs[loop] - oldRunsBefore[loop];
                    Check(newRuns >= 2 && newRuns <= 4 && oldRuns >= 20, $"{option} on: {loop} runs 4 times a second ({newRuns} runs in 40 frames; the old one {oldRuns})");
                }
                else
                {
                    var newRuns = b.Runs[loop] - runsBefore[loop];
                    var oldRuns = a.Runs[loop] - oldRunsBefore[loop];
                    Check(newRuns == oldRuns && newRuns >= 20, $"{option} on: {loop} runs as often as before ({newRuns} runs in 40 frames, {oldRuns} before)");
                }
                Both(w => w.OpenMenu()); Steps(2);
                Both(w => w.Settings.Set(option, false)); Steps(5);
                Both(w => w.MenuOpen = false); Steps(30);
                Both(w => { w.Pointing = false; w.Recording = false; w.InVehicle = false; w.InHeli = false; w.Mode = InputMode.MouseAndKeyboard; w.Pressed.Clear(); });
                Steps(20);
            }

            // 3. A long random session: random option sets switched in the menu, random keys, vehicle,
            // weapons, wanted level, respawns, the waypoint, and changes made by other resources.
            for (var cycle = 0; cycle < 120; cycle++)
            {
                Both(w => w.OpenMenu()); Steps(2 + random.Next(16));
                var set = Settings.Toggles.Where(_ => random.NextDouble() < 0.25).ToList();
                var drive = random.NextDouble() < 0.1;
                Both(w => { foreach (var t in Settings.Toggles) { w.Settings.Set(t, set.Contains(t)); } if (drive) { w.DriveToWp = true; w.Waypoint = true; } });
                Steps(4);
                Both(w => w.MenuOpen = false);
                var length = 60 + random.Next(120);
                // Keys and game events on about one frame in 12; radar and pause menu changes on
                // about one in 100, so most frames are compared outside a settle window.
                var events = Enumerable.Range(0, length).Select(_ => random.Next(300)).ToArray();
                var modeSwitch = random.NextDouble() < 0.3;
                for (var i = 0; i < length; i++)
                {
                    // Nothing else happens while a pause menu or radar change settles, so both
                    // versions see the same keys once it has.
                    var e = 100000 + (frame + 1) * 16 < settleUntil ? -1 : events[i];
                    var slow = e == 30 || e == 31 || e == 32;
                    if (slow) { settleUntil = 100000 + (frame + 1) * 16 + 600; }
                    Step(w =>
                    {
                        if (modeSwitch && i == 0) { w.Mode = w.Mode == InputMode.GamePad ? InputMode.MouseAndKeyboard : InputMode.GamePad; }
                        switch (e)
                        {
                            case 0: w.JustReleased.Add(Control.SpecialAbilitySecondary); break;
                            case 1: w.JustReleased.Add(Control.TpKey); break;
                            case 2: w.JustReleased.Add(Control.MultiplayerInfo); break;
                            case 3: w.JustReleased.Add(Control.ReplayStartStopRecording); break;
                            case 4: w.JustPressed.Add(Control.ReplayClipDelete); break;
                            case 5: w.Pressed.Add(Control.MultiplayerInfo); break;
                            case 6: w.Pressed.Remove(Control.MultiplayerInfo); w.JustReleased.Add(Control.MultiplayerInfo); break;
                            case 7: w.Pressed.Add(Control.Sprint); break;
                            case 8: w.Pressed.Remove(Control.Sprint); w.JustReleased.Add(Control.Sprint); break;
                            case 9: w.Pressed.Add(Control.LookLeftOnly); break;
                            case 10: w.Pressed.Remove(Control.LookLeftOnly); break;
                            case 11: w.InVehicle = !w.InVehicle; break;
                            case 12: w.InHeli = w.InVehicle && !w.InHeli; break;
                            case 13: w.Weapon = w.Weapon == unchecked((int)WeaponHash.Pistol) ? unchecked((int)WeaponHash.Unarmed) : unchecked((int)WeaponHash.Pistol); break;
                            case 14: w.Wanted = 2; break;
                            case 15: w.Ped++; break;                         // respawn: a new ped
                            case 16: w.HasChute = false; break;
                            case 17: w.Reserve = false; break;
                            case 18: if (w.Talking.Contains(1)) { w.Talking.Remove(1); } else { w.Talking.Add(1); } break;
                            case 19: w.Waypoint = false; break;
                            case 20: w.Keyboard = !w.Keyboard; break;
                            case 21: w.Paused = !w.Paused; break;
                            case 22: w.Pressed.Add(Control.Jump); break;
                            case 23: w.Pressed.Remove(Control.Jump); w.JustReleased.Add(Control.Jump); break;
                            case 24: w.ActiveOrg = w.ActiveOrg == null ? (object)"police" : null; break;
                            case 30: w.RadarShown = !w.RadarShown; break;     // another resource shows or hides the radar
                            case 31: w.RadarPref = !w.RadarPref; break;       // pause menu: show radar
                            case 32: w.ExpandedPref = !w.ExpandedPref; break; // pause menu: expanded radar
                        }
                    });
                }
                Both(w => { w.Pressed.Clear(); w.DriveToWp = false; });
                Steps(2);
            }

            Check(maxResting <= 6, $"IdleRest lists at most one rest per resting loop (6 loops), never more: peak {maxResting}");
            Check(keptCompared > compared / 2, $"what the game keeps was compared on most frames ({keptCompared} of {compared})");
            Console.WriteLine($"Compared {compared} frames ({guarded} inside a 600 ms settle window after another resource or the pause menu changed the radar); "
                + $"what the game keeps compared on {keptCompared}; at most {maxResting} rests in progress at once.");
            if (failures > 0) { throw new Exception($"{failures} check(s) failed"); }
            Console.WriteLine("PASS: idle rests (every frame matches build-2026-11-17-3's per-frame loops; idle loops rest)");
        }

        // Inputs for the single-option pass: use whatever the option reacts to.
        static void Script(World w, int frame, string option)
        {
            var t = frame % 40;
            switch (option)
            {
                case "KbPointKeys": if (t == 3 || t == 25) { w.JustReleased.Add(Control.SpecialAbilitySecondary); } break;
                case "KbTpToWaypoint": if (t == 5) { w.JustReleased.Add(Control.TpKey); w.Waypoint = !w.Waypoint; } break;
                case "KbRadarKeys": if (t == 7 || t == 30) { w.JustReleased.Add(Control.MultiplayerInfo); } break;
                case "KbRecordKeys":
                    if (t == 2 || t == 12) { w.JustReleased.Add(Control.ReplayStartStopRecording); }
                    if (t == 20) { w.Mode = InputMode.GamePad; w.Pressed.Add(Control.MultiplayerInfo); }
                    if (t == 28) { w.JustReleased.Add(Control.ReplayStartStopRecording); }
                    if (t == 33) { w.Pressed.Remove(Control.MultiplayerInfo); w.Mode = InputMode.MouseAndKeyboard; }
                    break;
                case "KbDriftMode": if (t == 1) { w.InVehicle = true; } if (t == 4) { w.Pressed.Add(Control.Sprint); } if (t == 20) { w.Pressed.Remove(Control.Sprint); w.JustReleased.Add(Control.Sprint); } break;
                case "LockCameraX": if (t == 10) { w.Pressed.Add(Control.LookLeftOnly); } if (t == 20) { w.Pressed.Remove(Control.LookLeftOnly); } break;
                case "ShowSpeedoKmh": case "ShowSpeedoMph": if (t == 10) { w.InVehicle = true; } if (t == 30) { w.InVehicle = false; } break;
                case "PlayerNeverWanted": if (t == 8 || t == 22) { w.Wanted = 3; } break;
                case "PlayerGodMode": case "PlayerNoRagdoll": if (t == 15) { w.Ped++; } break;
                case "UnlimitedAmmo": case "NoReload": if (t == 6 || t == 18) { w.Weapon = w.Weapon == unchecked((int)WeaponHash.Pistol) ? unchecked((int)WeaponHash.Unarmed) : unchecked((int)WeaponHash.Pistol); } break;
                case "AutoEquipChute": if (t == 4) { w.InVehicle = w.InHeli = true; w.HasChute = false; } if (t == 30) { w.InVehicle = w.InHeli = false; } break;
                case "UnlimitedParachutes": if (t == 9) { w.HasChute = false; w.Reserve = false; } break;
                case "EnableVoicechat": if (t == 12) { w.Talking.Add(1); } if (t == 30) { w.Talking.Remove(1); } break;
            }
        }
    }
}

namespace Sleepers
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using vMenuClient;

    // BaseScript's tick list as FiveM's client runtime keeps it (code/client/clrcore/BaseScript.cs):
    // handlers are matched by method and target; each frame every registered handler that has no
    // run in progress is started; removing a handler never stops a run in progress, and a handler
    // with a run in progress is never started a second time.
    public sealed class Script
    {
        public int Now;
        readonly List<Func<Task>> ticks = new List<Func<Task>>();
        readonly Dictionary<Func<Task>, Task> current = new Dictionary<Func<Task>, Task>();
        readonly List<KeyValuePair<int, TaskCompletionSource<bool>>> delays = new List<KeyValuePair<int, TaskCompletionSource<bool>>>();
        public int Starts;
        public int Registered => ticks.Count;
        public void Add(Func<Task> tick) => ticks.Add(tick);
        public void Remove(Func<Task> tick) => ticks.RemoveAll(t => t.Equals(tick));
        public Task Delay(int ms) { var t = new TaskCompletionSource<bool>(); delays.Add(new KeyValuePair<int, TaskCompletionSource<bool>>(Now + Math.Max(ms, 1), t)); return t.Task; }
        public void Frame(int now)
        {
            Now = now;
            var due = delays.FindAll(d => d.Key <= now);
            delays.RemoveAll(d => d.Key <= now);
            foreach (var d in due) { d.Value.SetResult(true); }
            foreach (var done in new List<Func<Task>>(current.Keys)) { if (current[done].IsCompleted) { if (current[done].IsFaulted) { throw current[done].Exception; } current.Remove(done); } }
            foreach (var tick in ticks.ToArray())
            {
                if (current.ContainsKey(tick)) { continue; }
                Starts++;
                current[tick] = tick();
            }
        }
    }

    // The NoClip and EntitySpawner pattern: sleep when off, wake when switched on.
    internal sealed class Feature
    {
        public readonly Script Script = new Script();
        public readonly TickSleeper Sleeper;
        public bool Active;
        public int Work, Concurrent, MaxConcurrent;
        public Feature() { Sleeper = new TickSleeper(Handler, t => Script.Add(t), t => Script.Remove(t)); Script.Add(Handler); }
        public void SetActive(bool active) { Active = active; if (active) { Sleeper.Wake(); } }
        async Task Handler()
        {
            if (!Active) { Sleeper.Sleep(); return; }
            Concurrent++; MaxConcurrent = Math.Max(MaxConcurrent, Concurrent);
            try
            {
                while (Active) { Work++; await Script.Delay(0); }
                await Script.Delay(0); // the reset after the loop, one frame later
            }
            finally { Concurrent--; }
        }
    }

    public static class Test
    {
        static void Check(bool ok, string message) { if (!ok) { throw new Exception("FAIL: " + message); } }

        public static void Run()
        {
            var f = new Feature(); var frame = 0;
            void Frames(int n) { for (var i = 0; i < n; i++) { f.Script.Frame(frame++ * 16); } }

            Frames(1);
            Check(f.Sleeper.Asleep && f.Script.Registered == 0, "off: one run, then it leaves the tick list");
            var starts = f.Script.Starts; Frames(100);
            Check(f.Script.Starts == starts, "asleep: not started at all");

            f.SetActive(true); Frames(1);
            Check(f.Work == 1, "woken: works from the next frame");
            Frames(9);
            Check(f.Work == 10, "on: works every frame");

            f.SetActive(false); Frames(3);
            Check(f.Sleeper.Asleep && f.Script.Registered == 0, "switched off: finishes, then sleeps again");

            // Off and on again within a run: the run carries on, nothing starts twice.
            f.SetActive(true); Frames(5); f.SetActive(false); f.SetActive(true); Frames(5);
            Check(f.MaxConcurrent == 1 && f.Script.Registered == 1, $"toggled within a run: one run at a time ({f.MaxConcurrent}), registered once ({f.Script.Registered})");

            // Switched on again while the last run is still finishing (before it could sleep).
            f.SetActive(false); Frames(1); f.SetActive(true); var work = f.Work; Frames(5);
            Check(f.MaxConcurrent == 1 && f.Work >= work + 3, "woken while finishing: picks up again, never twice at once");

            // Woken twice, put to sleep twice: one registration, no errors.
            f.SetActive(false); Frames(4); f.Sleeper.Wake(); f.Sleeper.Wake();
            Check(f.Script.Registered == 1, "waking twice registers once");
            Frames(2); f.Sleeper.Sleep();
            Check(f.Script.Registered == 0 && f.Sleeper.Asleep, "sleeping twice is harmless");
            Console.WriteLine("PASS: TickSleeper (asleep costs nothing, wakes next frame, never runs twice at once)");
        }
    }
}

static class Program
{
    static void Main()
    {
        Sleepers.Test.Run();
        Rests.Test.Run();
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-idle-rests-') as temp:
    work = Path(temp).resolve()
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable><NoWarn>CS1998;CS0162;CS0414;CS0649;CS0169;CS8321;CS0219</NoWarn></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'Latch.cs').write_text('namespace Rests\n{\n    ' + latch + '\n}\n', encoding='utf-8')
    (work / 'TickSleeper.cs').write_text((root / 'vMenu/TickSleeper.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    (work / 'IdleRest.cs').write_text((root / 'vMenu/IdleRest.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
