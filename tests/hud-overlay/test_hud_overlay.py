"""Release J: vMenu's on-screen text is shown on the web page instead of drawn every frame.

vMenu Enhanced shows coordinates, location, time and speed on its NUI page, updated a few times a
second. Release J does the same with the MenuAPI 4 page (build/vMenu/storage.html):
HudOverlayTick sends what to show, at most 10 times a second and only when it changed, and does
nothing while every one of these options is off.

Source checks: MiscSettings no longer draws text or runs every frame for these options; the loop is
registered; the page has the HUD layer outside the storage page's scoped styles and only reacts to
its own message.

Compiled checks: the production HudOverlayTick runs against fake natives: rests and hides once
when off, sends once per change, hides over the pause menu, respects the coordinates and location
permissions, speed only in a vehicle, both speeds when both are on.

Requires Python and the .NET 9 SDK; no FiveM runtime.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
fc = (root / 'vMenu' / 'FunctionsController.cs').read_text(encoding='utf-8-sig')
page = (root / 'build' / 'vMenu' / 'storage.html').read_text(encoding='utf-8-sig')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


# --- source checks ---------------------------------------------------------------------------
misc = member(fc, r'^        private async Task MiscSettings\(\).*?^        }$')
for gone in ('DrawMiscSettingsText', 'ShowCoordinates', 'ShowLocation', 'DrawTimeOnScreen', 'ShowSpeedo'):
    assert gone not in misc, f'MiscSettings no longer draws or runs every frame for {gone}'
for gone in ('private void DrawMiscSettingsText', 'private void ShowLocation()', 'private void ShowSpeedKmh', 'private void ShowSpeedMph'):
    assert gone not in fc, f'{gone} is gone'
setup = member(fc, r'^        public void SetupTickFunctions\(\).*?^        }$')
assert 'Tick += HudOverlayTick;' in setup
assert '<div id="vmenu-hud" hidden>' in page and 'd.action !== "vmenuHud"' in page
hud_markup = page.index('<div id="vmenu-hud" hidden>')
storage_markup = page.index('<div id="vmenu-storage">')
assert hud_markup < storage_markup, 'the HUD layer is outside #vmenu-storage, so the storage page reset never reaches it'
assert re.search(r'^\s*#vmenu-hud \{', page, re.M) and not re.search(r'#vmenu-storage[^{]*#vmenu-hud', page), 'its styles are its own'
print('PASS: on-screen text moved off the every-frame loop and onto the page')

# --- compiled checks -------------------------------------------------------------------------
tick = member(fc, r'^        private async Task HudOverlayTick\(\).*?^        }$')
fields = member(fc, r'^        private string lastHudMessage;.*?private string locationCross = "";')

program = r'''
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using static Hud.N;

namespace Hud
{
    public enum Permission { MSShowCoordinates, MSShowLocation }
    public static class W
    {
        public static bool Coords, Location, Time, Kmh, Mph, Pause, InVehicle, MenuOpen;
        public static HashSet<Permission> Denied = new HashSet<Permission>();
        public static float X = 1, Y = 2, Z = 3, Heading = 90, Speed = 10;
        public static List<string> Sent = new List<string>();
        public static int Rests, Delays;
    }
    public class Misc
    {
        public bool ShowCoordinates => W.Coords; public bool ShowLocation => W.Location; public bool DrawTimeOnScreen => W.Time;
        public bool ShowSpeedoKmh => W.Kmh; public bool ShowSpeedoMph => W.Mph;
    }
    public static class MainMenu { public static Misc MiscSettingsMenu = new Misc(); }
    public struct Vector3 { public float X, Y, Z; }
    public static class World { public static TimeSpan CurrentDayTime => new TimeSpan(14, 5, 0); }
    public static class N
    {
        public static bool IsAllowed(Permission p) => !W.Denied.Contains(p);
        public static int PlayerPedId() => 5;
        public static bool IsPauseMenuActive() => W.Pause;
        public static bool IsScreenFadedIn() => true;
        public static bool IsPlayerSwitchInProgress() => false;
        public static bool IsHudHidden() => false;
        public static float GetSafeZoneSize() => 1f;
        public static Vector3 GetEntityCoords(int e, bool alive) => new Vector3 { X = W.X, Y = W.Y, Z = W.Z };
        public static float GetEntityHeading(int e) => W.Heading;
        public static float GetTextScaleHeight(float scale, int font) => scale * 0.05f;
        public static int GetVehiclePedIsIn(int ped, bool last) => W.InVehicle ? 9 : 0;
        public static float GetEntitySpeed(int e) => W.Speed;
        public static void SendNuiMessage(string json) => W.Sent.Add(json);
        public static Task Delay(int ms) { W.Delays++; return Task.CompletedTask; }
    }

    public class Ticker
    {
        bool AnyMenuOpen => W.MenuOpen;
        Task Rest(int ms) { W.Rests++; return Task.CompletedTask; }
        const int IdleRestMs = 250;
        float safeZoneSizeX = -0.0247f;
        string zoneDisplay = "Airport";
        string headingDisplay = "N";
''' + fields + tick + r'''
        public void Run() => HudOverlayTick().Wait();
        public void SetLocation(bool near, string main, string cross) { locationNear = near; locationMain = main; locationCross = cross; }
    }

    public static class Program
    {
        static int failures;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

        public static int Main()
        {
            var t = new Ticker();
            t.Run(); t.Run();
            Check(W.Sent.Count == 0 && W.Rests == 2, "all off: nothing sent, the loop rests");

            W.Coords = true; t.Run();
            Check(W.Sent.Count == 1 && W.Sent[0].Contains("\"coords\":{\"x\":\"1.00\"") && W.Sent[0].Contains("\"visible\":true"), "coordinates on: sent with two decimals");
            t.Run(); t.Run();
            Check(W.Sent.Count == 1 && W.Delays == 3, "unchanged: not sent again, checked every 100 ms");
            W.X = 5; t.Run();
            Check(W.Sent.Count == 2 && W.Sent[1].Contains("\"x\":\"5.00\""), "moved: sent again");

            W.Pause = true; t.Run();
            Check(W.Sent[W.Sent.Count - 1].Contains("\"visible\":false"), "pause menu: hidden");
            W.Pause = false;

            W.Denied.Add(Permission.MSShowCoordinates); t.Run();
            Check(W.Sent[W.Sent.Count - 1] == "{\"action\":\"vmenuHud\",\"visible\":false}", "coordinates without permission: hidden");
            W.Denied.Clear();

            W.Coords = false; W.Location = true; t.SetLocation(true, "Main St", "Cross Rd"); t.Run();
            var last = W.Sent[W.Sent.Count - 1];
            Check(last.Contains("\"near\":true,\"main\":\"Main St\",\"cross\":\"Cross Rd\",\"zone\":\"Airport\",\"heading\":\"N\""), "location: street, near, crossing, zone and heading");
            W.Location = false;

            W.Kmh = true; W.Mph = true; t.Run();
            Check(W.Sent[W.Sent.Count - 1].Contains("\"speed\":null"), "speed: not on foot");
            W.InVehicle = true; t.Run();
            Check(W.Sent[W.Sent.Count - 1].Contains("\"kmh\":36,\"mph\":22"), "speed: both in a vehicle (10 m/s = 36 km/h, 22 mph)");
            W.Mph = false; t.Run();
            Check(W.Sent[W.Sent.Count - 1].Contains("\"kmh\":36,\"mph\":null"), "speed: only km/h when only it is on");

            W.Kmh = false; W.Time = true; t.Run();
            Check(W.Sent[W.Sent.Count - 1].Contains("\"time\":{\"text\":\"14:05\""), "time: HH:MM");

            W.Time = false; var before = W.Sent.Count; t.Run(); t.Run();
            Check(W.Sent.Count == before + 1 && W.Sent[W.Sent.Count - 1] == "{\"action\":\"vmenuHud\",\"visible\":false}", "all off again: hidden once, then nothing");

            W.MenuOpen = true; var rests = W.Rests; t.Run();
            Check(W.Rests == rests, "all off with a menu open: no rest, so switching one on shows at once");

            Console.WriteLine(failures == 0 ? "PASS: the HUD loop sends only what changed and rests while off" : $"{failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
'''

newtonsoft = root / 'dependencies' / 'shared' / 'Newtonsoft.Json.dll'
with tempfile.TemporaryDirectory(prefix='vmenu-hud-overlay-') as tmp:
    work = Path(tmp)
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'Tests.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable><NoWarn>CS1998;CS0162;CS0414;CS0649;CS0169</NoWarn></PropertyGroup>
  <ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>{newtonsoft}</HintPath></Reference></ItemGroup>
</Project>
''', encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'), '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
