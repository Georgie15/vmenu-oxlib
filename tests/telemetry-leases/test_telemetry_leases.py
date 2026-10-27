"""psrp_telemetry invincibility leases for vMenu god mode and noclip, and vitals after a model swap.

Compiles the production vMenu/GodmodeTelemetry.cs (client heartbeat) and
vMenuServer/GodmodeTelemetry.cs (server lease renewal) against fake natives, with the real
Permission enum, GetPermissionAndParentPermissions and GetAceName from
SharedClasses/PermissionsManager.cs deciding which ACEs count. Checks:

- client: one tick and one poll publish both states; a state is sent on the first poll, on
  every change and every 2 s while active; noclip needs the NoClip permission
- server: a lease is renewed every second only while the heartbeat is at most 6 s old, the
  exact ACE (the permission or a vMenu parent of it) is held and OneSync reports the player
  invincible; the reasons are exactly "vMenu god mode" and "vMenu noclip"; requests are cleared
  on inactive, on failed validation and on playerDropped; nothing runs when nothing is requested;
  the older client's god mode event alone still works

Plus source checks that every SetPlayerModel in vMenu/ restores the vitals before its first await.

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
read = lambda rel: (root / rel).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
client = read('vMenu/GodmodeTelemetry.cs')
server = read('vMenuServer/GodmodeTelemetry.cs')
noclip = read('vMenu/Noclip.cs')
perms = read('SharedClasses/PermissionsManager.cs')


def member(source, pattern):
    match = re.search(pattern, source, re.M | re.S)
    assert match, f'not found: {pattern}'
    return match[0]


# --- wiring ------------------------------------------------------------------------------------
assert client.count('[Tick]') == 1 and client.count('await Delay(') == 1, 'client: one tick, one poll for both states'
assert client.count('TriggerServerEvent(') == 1, 'client: both states go through the same heartbeat'
assert '"vMenu:PSRP:GodmodeState"' in client and '"vMenu:PSRP:NoclipState"' in client
assert server.count('[Tick]') == 1, 'server: one renewal tick for both leases'
assert '[EventHandler("vMenu:PSRP:GodmodeState")]' in server, 'server still accepts the older client god mode event'
assert '[EventHandler("vMenu:PSRP:NoclipState")]' in server and '[EventHandler("playerDropped")]' in server
assert 'private const string GodmodeReason = "vMenu god mode";' in server
assert 'private const string NoclipReason = "vMenu noclip";' in server
assert server.count('GrantInvincibilityGrace(') == 1, 'one export call site'
assert 'Game.Player.State.Set("vmenu_noclip_active"' in noclip, 'the client statebag is still written, but grants nothing'
assert 'SetEntityInvincible(noclipEntity, true);' in noclip, 'noclip still sets invincibility (why the lease exists)'
renew = member(server, r'^        internal async Task RenewValidatedLeases\(\).*?^        }$')
assert renew.index('await Delay(RenewalIntervalMs);') < renew.index('if (requested.Count == 0)') < renew.index('GetResourceState('), \
    'no work when nothing is requested'
print('PASS: wiring (one tick each side, events, reasons, idle skip)')

# --- vitals restored before the first await after every SetPlayerModel ---------------------------
vitals = ['Game.Player.MaxArmor = maxArmour;', 'Game.PlayerPed.MaxHealth = maxHealth;',
          'Game.PlayerPed.Health = health;', 'Game.PlayerPed.Armor = armour;']
sites = 0
for path in sorted((root / 'vMenu').rglob('*.cs')):
    source = path.read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    for match in re.finditer(r'^\s*SetPlayerModel\(', source, re.M):
        sites += 1
        rest = source[match.end():]
        first_await = rest.find('await ')
        assert first_await > 0, f'{path.name}: an await follows SetPlayerModel'
        before_await = rest[:first_await]
        where = f'{path.relative_to(root)} line {source[:match.start()].count(chr(10)) + 1}'
        positions = [before_await.find(line) for line in vitals]
        assert all(p >= 0 for p in positions), f'{where}: vitals restored before the first await'
        assert positions == sorted(positions), f'{where}: max armour/health before health/armour'
        # Up to the next model swap or the end of the method, the vitals are restored only once.
        ends = [i for i in (rest.find('SetPlayerModel('), rest.find('\n        }\n')) if i >= 0]
        window = rest[:min(ends)]
        assert all(window.count(line) == 1 for line in vitals), f'{where}: vitals restored once, not again after the await'
assert sites == 4, f'expected 4 SetPlayerModel call sites in vMenu/, found {sites}'
mp = read('vMenu/menus/MpPedCustomization.cs')
saved = member(mp, r'^        private async Task SpawnSavedPed\(bool restoreWeapons\).*?^        }$')
order = [saved.index(s) for s in ('TriggerEvent("vMenu:PedChange:Before");', 'SaveWeaponLoadout(', 'SetPlayerModel(',
                                   'Game.PlayerPed.Armor = armour;', 'await SpawnWeaponLoadoutAsync(', 'TriggerEvent("vMenu:PedChange:After");')]
assert order == sorted(order), 'SpawnSavedPed keeps Before, save, swap, (vitals), weapons, After in order'
print(f'PASS: vitals restored before the first await at all {sites} SetPlayerModel sites')

# --- compiled behaviour ------------------------------------------------------------------------
enum = member(perms, r'^        public enum Permission\s*\{.*?^        \}')
parents_field = member(perms, r'^        private static readonly Dictionary<Permission, List<Permission>> parentPermissions = new\(\);$')
parents = member(perms, r'^        public static List<Permission> GetPermissionAndParentPermissions\(.*?^        }$')
ace_name = member(perms, r'^        private static string GetAceName\(Permission permission\).*?^        }$') \
    .replace('private static string GetAceName', 'public static string GetAceName')

fakes = r'''
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CitizenFX.Core
{
    [AttributeUsage(AttributeTargets.Method)] public class TickAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class EventHandlerAttribute : Attribute { public EventHandlerAttribute(string name) { } }
    [AttributeUsage(AttributeTargets.Parameter)] public class FromSourceAttribute : Attribute { }
    public class Player { public string Handle; }
    public class PlayerList { public Player this[string handle] => World.Online.Contains(handle) ? new Player { Handle = handle } : null; }
    public class ExportDictionary { public dynamic this[string resource] => World.Telemetry; }
    public static class Debug { public static void WriteLine(string text) { World.Log.Add(text); } }
    public abstract class BaseScript
    {
        protected PlayerList Players { get; } = new PlayerList();
        protected ExportDictionary Exports { get; } = new ExportDictionary();
        public static Task Delay(int ms) { World.Now += ms; return Task.CompletedTask; }
        public static void TriggerServerEvent(string name, params object[] args) { World.Sent.Add(name + "=" + args[0]); }
    }
}

namespace CitizenFX.Core.Native
{
    public static class API
    {
        public static int GetGameTimer() => World.Now;
        public static bool IsPlayerAceAllowed(string source, string ace) { World.NativeCalls++; return World.Aces.Contains(source + "|" + ace); }
        public static bool GetPlayerInvincible(string source) { World.NativeCalls++; return World.Invincible.Contains(source); }
        public static string GetResourceState(string resource) { World.NativeCalls++; return World.TelemetryState; }
    }
}

public class FakeTelemetry
{
    public void GrantInvincibilityGrace(int source, int seconds, string reason) { World.Grants.Add($"{source}:{seconds}:{reason}"); }
}

public static class World
{
    public static int Now;
    public static DateTime Clock = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    public static int NativeCalls;
    public static string TelemetryState = "started";
    public static readonly HashSet<string> Online = new HashSet<string>();
    public static readonly HashSet<string> Aces = new HashSet<string>();
    public static readonly HashSet<string> Invincible = new HashSet<string>();
    public static readonly List<string> Grants = new List<string>();
    public static readonly List<string> Sent = new List<string>();
    public static readonly List<string> Log = new List<string>();
    public static readonly FakeTelemetry Telemetry = new FakeTelemetry();
}

public static class FakeClock { public static DateTime UtcNow => World.Clock; }

namespace vMenuShared
{
    public static class PermissionsManager
    {
''' + enum + '\n' + parents_field + '\n' + parents + '\n' + ace_name + r'''
        public static readonly HashSet<Permission> Granted = new HashSet<Permission>();
        public static bool IsAllowed(Permission permission, bool checkAnyway = false) => Granted.Contains(permission);
    }
}

namespace vMenuClient
{
    public class FakePlayerOptions { public bool PlayerGodMode; }
    public static class MainMenu
    {
        public static bool PermissionsSetupComplete;
        public static FakePlayerOptions PlayerOptionsMenu;
    }
    public static class NoClip
    {
        public static bool Active;
        internal static bool IsNoclipActive() => Active;
    }
}
'''

program = r'''
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using vMenuShared;
using static vMenuShared.PermissionsManager;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }

    static HashSet<string> Aces(Permission permission) =>
        new HashSet<string>(GetPermissionAndParentPermissions(permission).Select(GetAceName));

    static void Reset()
    {
        World.Grants.Clear(); World.Sent.Clear(); World.Online.Clear(); World.Aces.Clear(); World.Invincible.Clear();
        World.NativeCalls = 0; World.TelemetryState = "started";
        World.Clock = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    }

    static void Online(string handle, params string[] aces)
    {
        World.Online.Add(handle);
        foreach (var ace in aces) World.Aces.Add(handle + "|" + ace);
    }

    static List<string> Renew(vMenuServer.GodmodeTelemetry server, int seconds = 1)
    {
        World.Grants.Clear();
        World.Clock = World.Clock.AddSeconds(seconds);
        server.RenewValidatedLeases().Wait();
        return World.Grants.ToList();
    }

    static void Main()
    {
        // --- which ACEs count, from the real permission table ----------------------------------
        var noclipAces = Aces(Permission.NoClip);
        var godAces = Aces(Permission.POGod);
        Check(noclipAces.SetEquals(new[] { "vMenu.NoClip", "vMenu.Everything" }), "NoClip ACE set: " + string.Join(",", noclipAces));
        Check(godAces.SetEquals(new[] { "vMenu.PlayerOptions.God", "vMenu.PlayerOptions.All", "vMenu.Everything" }), "God ACE set: " + string.Join(",", godAces));
        var allAces = new[] { "vMenu.NoClip", "vMenu.Everything", "vMenu.PlayerOptions.God", "vMenu.PlayerOptions.All",
                              "vMenu.PlayerOptions.Menu", "vMenu.Staff", "vMenu.OnlinePlayers.All", "vMenu.Noclip", "vmenu.noclip" };
        Console.WriteLine("PASS: ACE sets from PermissionsManager (NoClip: NoClip/Everything; God: God/All/Everything)");

        // --- server: each ACE alone, for each event ------------------------------------------------
        foreach (var ace in allAces)
        {
            foreach (var (eventName, reason, expected) in new[] { ("noclip", "vMenu noclip", noclipAces), ("god", "vMenu god mode", godAces) })
            {
                Reset();
                var server = new vMenuServer.GodmodeTelemetry();
                Online("7", ace);
                World.Invincible.Add("7");
                var player = new CitizenFX.Core.Player { Handle = "7" };
                if (eventName == "noclip") server.OnNoclipState(player, true); else server.OnGodmodeState(player, true);
                var grants = Renew(server);
                var want = expected.Contains(ace) ? new[] { "7:4:" + reason } : new string[0];
                Check(grants.SequenceEqual(want), $"{eventName} with only {ace}: [{string.Join(",", grants)}]");
            }
        }
        Console.WriteLine("PASS: a lease needs the exact ACE (or its vMenu parent); exact reasons, 4 s, numeric source");

        // --- server: noclip lifecycle ---------------------------------------------------------------
        Reset();
        var s = new vMenuServer.GodmodeTelemetry();
        var p = new CitizenFX.Core.Player { Handle = "3" };
        Check(Renew(s).Count == 0 && World.NativeCalls == 0, "nothing requested: no natives, no export calls");
        Online("3", "vMenu.NoClip");
        World.Invincible.Add("3");
        s.OnNoclipState(p, true);
        Check(Renew(s).SequenceEqual(new[] { "3:4:vMenu noclip" }), "fresh heartbeat renews");
        Check(Renew(s, 5).SequenceEqual(new[] { "3:4:vMenu noclip" }), "6 s after the heartbeat still renews");
        Check(Renew(s, 1).Count == 0, "7 s after the heartbeat: stale, no lease");
        World.NativeCalls = 0;
        Check(Renew(s).Count == 0 && World.NativeCalls == 0, "a stale request is cleared, not re-checked");
        s.OnNoclipState(p, true);
        Check(Renew(s).SequenceEqual(new[] { "3:4:vMenu noclip" }), "the next heartbeat renews again");
        Console.WriteLine("PASS: heartbeat freshness (<= 6 s), stale requests cleared");

        // Noclip in a vehicle: the vehicle is invincible, not the player, so no lease.
        World.Invincible.Remove("3");
        s.OnNoclipState(p, true);
        Check(Renew(s).Count == 0, "player not invincible: no lease");
        World.Invincible.Add("3");
        Check(Renew(s).Count == 0, "a request that failed the bit check was cleared");
        s.OnNoclipState(p, true);
        Check(Renew(s).SequenceEqual(new[] { "3:4:vMenu noclip" }), "back on foot, the next heartbeat renews");
        Console.WriteLine("PASS: the OneSync invincibility bit is required (vehicle noclip gets no lease)");

        // Inactive, ACE revoked, dropped, offline, telemetry stopped.
        s.OnNoclipState(p, false);
        World.NativeCalls = 0;
        Check(Renew(s).Count == 0 && World.NativeCalls == 0, "inactive clears the request");
        s.OnNoclipState(p, true);
        World.Aces.Remove("3|vMenu.NoClip");
        Check(Renew(s).Count == 0, "ACE revoked: no lease");
        World.Aces.Add("3|vMenu.NoClip");
        Check(Renew(s).Count == 0, "the revoked request was cleared");
        s.OnNoclipState(p, true);
        s.OnPlayerDropped(p, "quit");
        World.NativeCalls = 0;
        Check(Renew(s).Count == 0 && World.NativeCalls == 0, "playerDropped clears the request");
        s.OnNoclipState(p, true);
        World.Online.Remove("3");
        Check(Renew(s).Count == 0, "player gone: no lease");
        World.Online.Add("3");
        Check(Renew(s).Count == 0, "the gone player's request was cleared");
        s.OnNoclipState(p, true);
        World.TelemetryState = "stopped";
        Check(Renew(s).Count == 0, "psrp_telemetry not started: no export call");
        World.TelemetryState = "started";
        Check(Renew(s).SequenceEqual(new[] { "3:4:vMenu noclip" }), "the request survives a telemetry restart");
        Console.WriteLine("PASS: cleared on inactive, revoked ACE, playerDropped and a missing player");

        // God mode and noclip together, and the older client's god mode event on its own.
        Reset();
        s = new vMenuServer.GodmodeTelemetry();
        p = new CitizenFX.Core.Player { Handle = "9" };
        Online("9", "vMenu.Everything");
        World.Invincible.Add("9");
        s.OnGodmodeState(p, true);
        Check(Renew(s).SequenceEqual(new[] { "9:4:vMenu god mode" }), "older client: god mode event alone still renews");
        s.OnNoclipState(p, true);
        Check(Renew(s).SequenceEqual(new[] { "9:4:vMenu god mode", "9:4:vMenu noclip" }), "both modes: both leases");
        s.OnNoclipState(p, false);
        Check(Renew(s).SequenceEqual(new[] { "9:4:vMenu god mode" }), "noclip off keeps god mode");
        s.OnNoclipState(p, true);
        s.OnGodmodeState(p, true);
        Check(Renew(s, 4).Count == 2, "both still fresh after 4 s");
        s.OnNoclipState(p, true);
        Check(Renew(s, 3).SequenceEqual(new[] { "9:4:vMenu noclip" }), "a stale god mode heartbeat drops only god mode");
        s.OnGodmodeState(p, false);
        Check(Renew(s).SequenceEqual(new[] { "9:4:vMenu noclip" }), "god mode off keeps noclip");
        s.OnNoclipState(p, false);
        World.NativeCalls = 0;
        Check(Renew(s).Count == 0 && World.NativeCalls == 0, "both off: nothing left to check");
        Console.WriteLine("PASS: god mode and noclip leases are independent; the old god mode event still works");

        // --- client: one poll publishes both heartbeats -----------------------------------------
        var c = new vMenuClient.GodmodeTelemetry();
        World.Now = 1000;
        List<string> Poll() { World.Sent.Clear(); c.PublishGodmodeState().Wait(); return World.Sent.ToList(); }
        Check(Poll().SequenceEqual(new[] { "vMenu:PSRP:GodmodeState=False", "vMenu:PSRP:NoclipState=False" }), "first poll reports both, off");
        Check(World.Now == 1500, "one 500 ms poll");
        Check(Poll().Count == 0, "no change, nothing sent");

        vMenuClient.NoClip.Active = true;
        Check(Poll().Count == 0, "permissions not set up: noclip not reported");
        vMenuClient.MainMenu.PermissionsSetupComplete = true;
        Check(Poll().Count == 0, "without the NoClip permission: not reported");
        PermissionsManager.Granted.Add(Permission.NoClip);
        Check(Poll().SequenceEqual(new[] { "vMenu:PSRP:NoclipState=True" }), "noclip on is sent at once");
        Check(Poll().Count == 0 && Poll().Count == 0 && Poll().Count == 0, "not again within 2 s");
        Check(Poll().SequenceEqual(new[] { "vMenu:PSRP:NoclipState=True" }), "heartbeat every 2 s");

        vMenuClient.MainMenu.PlayerOptionsMenu = new vMenuClient.FakePlayerOptions { PlayerGodMode = true };
        Check(Poll().Count == 0, "god mode without POGod: not reported");
        PermissionsManager.Granted.Add(Permission.POGod);
        Check(Poll().SequenceEqual(new[] { "vMenu:PSRP:GodmodeState=True" }), "god mode on is sent at once, noclip unaffected");
        Check(Poll().Count == 0, "neither heartbeat due yet");
        Check(Poll().SequenceEqual(new[] { "vMenu:PSRP:NoclipState=True" }), "noclip keeps its own 2 s heartbeat");

        vMenuClient.NoClip.Active = false;
        Check(Poll().SequenceEqual(new[] { "vMenu:PSRP:NoclipState=False" }), "noclip off is sent at once");
        Check(Poll().SequenceEqual(new[] { "vMenu:PSRP:GodmodeState=True" }), "god mode heartbeat continues");
        for (var i = 0; i < 8; i++) Check(!Poll().Any(e => e.StartsWith("vMenu:PSRP:NoclipState")), "an off state is not repeated");
        Console.WriteLine("PASS: client heartbeats (first poll, on change, every 2 s while on, NoClip permission)");
    }
}
'''

with tempfile.TemporaryDirectory(prefix='vmenu-telemetry-leases-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    clocked = server.replace('DateTime.UtcNow', 'FakeClock.UtcNow')
    assert clocked.count('FakeClock.UtcNow') == 3, 'server reads the clock in both handlers and the renewal tick'
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><LangVersion>latest</LangVersion></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'Fakes.cs').write_text(fakes, encoding='utf-8')
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    (work / 'ClientGodmodeTelemetry.cs').write_text(client, encoding='utf-8')
    (work / 'ServerGodmodeTelemetry.cs').write_text(clocked, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
