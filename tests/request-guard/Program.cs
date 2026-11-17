// Run: dotnet run --project tests/request-guard
// Drives the production RequestGuardCore.cs (the C# guard's rate limiter, argument checks and event
// sources) and checks the wiring of every request a client can send to vMenu's server side: each
// C# handler goes through RequestGuard with untyped (object) client arguments, the server's player
// events check their source, and each Lua handler registers through VMenuGuard.
// The Lua guard and security log have their own test: lua tests/request-guard/test_request_guard.lua
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vMenuServer;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        return dir;
    }

    static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    static int Main()
    {
        RateLimiter();
        Payload();
        Sources();
        CSharpWiring();
        LuaWiring();
        return 0;
    }

    static void RateLimiter()
    {
        // The shared scenario (test_request_guard.lua runs the same one through the Lua guard).
        var limiter = new RequestRateLimiter();
        var allowed = new List<bool>();
        var reports = new List<int>();
        void Take(long now)
        {
            allowed.Add(limiter.TryTake("1", now, 10, 3, out var report));
            reports.Add(report);
        }
        for (var i = 0; i < 15; i++) Take(0);
        Check(allowed.Take(10).All(a => a) && allowed.Skip(10).All(a => !a), "10 at once, the 11th is refused");
        Check(reports.Take(10).All(r => r == 0) && reports[10] == 1 && reports.Skip(11).All(r => r == 0), "the first refusal is reported at once, the rest held");
        Take(500);
        Check(allowed[15] && reports[15] == 0, "1.5 tokens after 500 ms: allowed; the 4 held refusals wait (reported less than a second ago)");
        Take(600);
        Check(!allowed[16] && reports[16] == 0, "0.8 tokens: refused and held");
        Take(1700);
        Check(allowed[17] && reports[17] == 5, "1.1 s later (4.1 tokens): allowed, and the 5 held refusals are reported");
        for (var i = 0; i < 4; i++) Take(1700);
        Check(allowed.Skip(18).Take(3).All(a => a) && !allowed[21], "tokens refill at 3 a second");
        for (var i = 0; i < 11; i++) Take(600000);
        Check(allowed.Skip(22).Take(10).All(a => a) && !allowed[32], "never above the burst, however long the wait");
        Console.WriteLine("PASS: token bucket (burst, refill, refusals reported first at once, then at most once a second)");

        // A flood: 1000 requests in 5 s get through 10 + 15 and are logged about once a second.
        var flood = new RequestRateLimiter();
        int through = 0, logs = 0, logged = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (flood.TryTake("9", i * 5, 10, 3, out var report)) through++;
            if (report > 0) { logs++; logged += report; }
        }
        Check(through <= 10 + 15, "a flood gets through at most the burst plus the refill: " + through);
        Check(logs <= 5, "a flood is logged once a second, not per request: " + logs);
        Check(logged >= 1000 - through - 200, "the logged counts cover the refusals (all but the last second's): " + logged);

        // Players and events are independent; leaving forgets; 0 means no limit; a lowered limit applies at once.
        var shared = new RequestRateLimiter();
        for (var i = 0; i < 10; i++) shared.TryTake("1", 0, 10, 3, out _);
        Check(!shared.TryTake("1", 0, 10, 3, out _) && shared.TryTake("2", 0, 10, 3, out _), "each player has their own bucket");
        shared.Forget("1");
        Check(shared.TryTake("1", 0, 10, 3, out _) && shared.Tracked == 2, "a player who left starts afresh");
        Check(Enumerable.Range(0, 100).All(_ => shared.TryTake("3", 0, 0, 3, out _)), "burst 0: no limit");
        Check(Enumerable.Range(0, 100).All(_ => shared.TryTake("4", 0, 10, 0, out _)), "rate 0: no limit");
        var lowered = new RequestRateLimiter();
        lowered.TryTake("1", 0, 10, 3, out _);
        Check(lowered.TryTake("1", 60000, 2, 3, out _) && lowered.TryTake("1", 60000, 2, 3, out _) && !lowered.TryTake("1", 60000, 2, 3, out _),
            "a lowered burst applies to a full bucket at once");
        Check(new RequestRateLimiter().TryTake("1", 1000, 10, 3, out _), "a clock that went backwards doesn't break it");

        // What the menu really sends is never limited.
        var heartbeat = new RequestRateLimiter();
        Check(Enumerable.Range(0, 1800).All(i => heartbeat.TryTake("1", i * 2000L, 10, 3, out _)), "a heartbeat every 2 s for an hour (god mode, noclip, stamina)");
        var toggles = new RequestRateLimiter();
        Check(Enumerable.Range(0, 200).All(i => toggles.TryTake("1", i * 400L, 10, 3, out _)), "200 menu actions in a row, 400 ms apart");
        var bursty = new RequestRateLimiter();
        Check(Enumerable.Range(0, 60).All(i => bursty.TryTake("1", (i / 8) * 3000L + (i % 8) * 50L, 10, 3, out _)), "bursts of 8 quick clicks every 3 s");
        var permissions = new RequestRateLimiter();
        var retries = new long[] { 0, 45000, 90000, 180000, 480000, 780000, 1080000 };
        Check(retries.All(t => permissions.TryTake("1", t, 3, 3 / 60.0, out _)), "the client's permission request and its retries (3 at once, 3 a minute)");
        Check(permissions.TryTake("1", 1080001, 3, 3 / 60.0, out _) && permissions.TryTake("1", 1080002, 3, 3 / 60.0, out _)
            && !permissions.TryTake("1", 1080003, 3, 3 / 60.0, out _), "a flood of permission requests is held to 3, then 3 a minute");
        Console.WriteLine("PASS: floods are cut and logged once a second; players, leaving, no-limit and lowered limits; real menu use never limited");
    }

    static void Payload()
    {
        foreach (var value in new object[] { 5, 5L, 5u, 5UL, (short)5, (ushort)5, (byte)5, (sbyte)5, 5.0, 5f })
        {
            Check(RequestPayload.TryInt(value, out var i) && i == 5, "a whole number arrives as " + value.GetType().Name);
        }
        Check(RequestPayload.TryInt(-3, out var negative) && negative == -3, "negative ints");
        foreach (var value in new object[] { null, "5", 5.5, double.NaN, double.PositiveInfinity, 1e12, long.MaxValue, ulong.MaxValue, true,
            new Dictionary<string, object>(), new List<object>() })
        {
            Check(!RequestPayload.TryInt(value, out _), "not a whole number in int range: " + (value?.GetType().Name ?? "null") + " " + value);
        }
        Check(RequestPayload.TryBool(true, out var t) && t && RequestPayload.TryBool(false, out var f) && !f, "true and false");
        foreach (var value in new object[] { null, 1, 0, "true", new Dictionary<string, object>() })
        {
            Check(!RequestPayload.TryBool(value, out _), "not a bool: " + value);
        }
        Check(RequestPayload.TryString("CLEAR", 32, out var s) && s == "CLEAR" && RequestPayload.TryString("", 32, out _), "text up to the limit");
        Check(!RequestPayload.TryString(new string('x', 33), 32, out _) && !RequestPayload.TryString(null, 32, out _)
            && !RequestPayload.TryString(5, 32, out _) && !RequestPayload.TryString(new List<object>(), 32, out _), "too long, missing or not text");
        Console.WriteLine("PASS: argument checks (whole numbers of any integer type, bools, bounded text; tables and strings refused)");
    }

    static void Sources()
    {
        Check(RequestSource.IsClient("net:5") && !RequestSource.IsClient("internal-net:5") && !RequestSource.IsClient("") && !RequestSource.IsClient(null),
            "a client's request is net:<id>");
        Check(RequestSource.IsServerPlayerEvent("internal-net:5") && !RequestSource.IsServerPlayerEvent("net:5") && !RequestSource.IsServerPlayerEvent(null),
            "the server's player events are internal-net:<id>");
        Check(RequestSource.PlayerHandle("net:5") == "5" && RequestSource.PlayerHandle("internal-net:12") == "12", "the player's server id");
        foreach (var bad in new[] { null, "", "5", "net:", "net:0", "net:-1", "net:abc", "net:5 ", "internal-net:x", "other:5" })
        {
            Check(RequestSource.PlayerHandle(bad) == null, "no player in " + (bad ?? "null"));
        }
        Check(RequestSource.IsPlayerHandle("7") && !RequestSource.IsPlayerHandle("0") && !RequestSource.IsPlayerHandle("-7") && !RequestSource.IsPlayerHandle(null),
            "a real player's handle is a whole number above 0");
        Console.WriteLine("PASS: event sources (net: from clients, internal-net: from the server, nothing from other resources)");
    }

    // Every [EventHandler] in vMenuServer, with the GuardedRequest it uses (null: the server's own player events).
    static readonly Dictionary<string, string> ClientEvents = new Dictionary<string, string>
    {
        ["vMenu:RequestPermissions"] = "RequestPermissions",
        ["vMenu:SetPersonalVehicle"] = "SetPersonalVehicle",
        ["vMenu:GetOutOfCar"] = "GetOutOfCar",
        ["vMenu:ClearArea"] = "ClearArea",
        ["vMenu:UpdateServerWeather"] = "UpdateServerWeather",
        ["vMenu:UpdateServerBlackout"] = "UpdateServerBlackout",
        ["vMenu:UpdateServerVehicleBlackout"] = "UpdateServerVehicleBlackout",
        ["vMenu:UpdateServerWeatherCloudsType"] = "UpdateServerWeatherCloudsType",
        ["vMenu:UpdateServerTime"] = "UpdateServerTime",
        ["vMenu:SaveTeleportLocation"] = "SaveTeleportLocation",
        ["vMenu:PSRP:GodmodeState"] = "GodmodeState",
        ["vMenu:PSRP:NoclipState"] = "NoclipState",
        ["vMenu:PSRP:RequestVital"] = "RequestVital",
    };

    static void CSharpWiring()
    {
        var guard = Read("vMenuServer", "RequestGuard.cs");
        var fields = Regex.Matches(guard, @"public static readonly GuardedRequest (\w+) = new GuardedRequest\(""([^""]+)""")
            .Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Check(fields.Count == ClientEvents.Count && ClientEvents.All(e => fields.TryGetValue(e.Value, out var name) && name == e.Key),
            "one GuardedRequest per client event, named after it");
        var all = Regex.Match(guard, @"private static readonly GuardedRequest\[\] All =\s*\{(.*?)\};", RegexOptions.Singleline).Groups[1].Value;
        Check(fields.Keys.All(f => Regex.IsMatch(all, @"\b" + f + @"\b")), "every GuardedRequest is forgotten when its player leaves (All)");
        Check(guard.Contains("GetConvar(\"vmenu_guard_rate_limits\", \"true\")") && guard.Contains("GetConvarInt(\"vmenu_guard_burst\", 10)")
            && guard.Contains("GetConvarInt(\"vmenu_guard_per_second\", 3)") && guard.Contains("GetConvarInt(\"vmenu_guard_permissions_burst\", 3)")
            && guard.Contains("GetConvarInt(\"vmenu_guard_permissions_per_minute\", 3)"), "C# convars and defaults");
        Check(guard.Contains("BaseScript.TriggerEvent(ReportEvent, kind, eventName, serverId, detail, count)") && guard.Contains("\"vMenu:security:report\""),
            "C# log entries go to the Lua security log");

        var seen = new HashSet<string>();
        var droppedHandlers = 0;
        foreach (var file in new[] { "MainServer.cs", "GodmodeTelemetry.cs", "VitalTelemetry.cs" })
        {
            var source = Read("vMenuServer", file);
            foreach (Match m in Regex.Matches(source, @"\[EventHandler\(""([^""]+)""\)\]\s*internal (?:async )?void (\w+)\(([^)]*)\)\s*\{(.*?)\n        \}", RegexOptions.Singleline))
            {
                var name = m.Groups[1].Value;
                var parameters = m.Groups[3].Value.Split(',').Select(p => p.Trim()).ToList();
                var body = m.Groups[4].Value;
                var where = $"{file} {m.Groups[2].Value} ({name})";
                if (name == "playerDropped" || name == "playerJoining")
                {
                    droppedHandlers++;
                    Check(parameters.Contains("[FromSource] string source"), where + ": reads the raw source");
                    var check = body.IndexOf("IsServerPlayerEvent(source", StringComparison.Ordinal);
                    Check(check >= 0 && check < 80, where + ": checks it is the server's own event before anything else");
                    Check(parameters.All(p => p.StartsWith("[FromSource]") || p.StartsWith("object ")), where + ": no typed argument a client copy could break");
                    continue;
                }

                Check(ClientEvents.TryGetValue(name, out var field), where + ": a known client event (add it to the inventory)");
                seen.Add(name);
                Check(parameters[0] == "[FromSource] Player source" || parameters[0] == "[FromSource] Player sourcePlayer", where + ": the sender first");
                Check(parameters.Skip(1).All(p => p.StartsWith("object ") || p == "[FromSource] string source"),
                    where + ": client arguments are object (a typed one the runtime can't convert removes the handler)");
                Check(body.Contains("RequestGuard.Admit(") && body.Contains("RequestGuard." + field), where + ": goes through RequestGuard." + field);
                var admit = body.IndexOf("RequestGuard.Admit(", StringComparison.Ordinal);
                foreach (var work in new[] { "IsPlayerAceAllowed(", "PermissionsManager.", "personalVehicles.", "JsonConvert.", "Current", "TriggerClientEvent(", "Exports[" })
                {
                    var at = body.IndexOf(work, StringComparison.Ordinal);
                    Check(at < 0 || at > admit, where + ": the guard runs before " + work);
                }
                if (parameters.Count > 1 && name != "vMenu:RequestPermissions")
                {
                    Check(body.Contains("RequestPayload.Try"), where + ": checks its arguments");
                    Check(body.Contains("RequestGuard.Malformed("), where + ": logs malformed arguments");
                }
                if (file != "GodmodeTelemetry.cs" && name != "vMenu:RequestPermissions")
                {
                    Check(body.Contains("catch (Exception exception)") && body.Contains("RequestGuard.Failed(source, request, exception)"),
                        where + ": an exception is caught (the runtime would remove the handler for everyone)");
                }
            }
        }
        Check(seen.SetEquals(ClientEvents.Keys), "every client event has a guarded handler: missing " + string.Join(", ", ClientEvents.Keys.Except(seen)));
        Check(droppedHandlers == 4, "playerJoining and three playerDropped handlers check their source: " + droppedHandlers);

        var main = Read("vMenuServer", "MainServer.cs");
        var permissions = Regex.Match(main, @"internal void RequestPermissions\(.*?\n        \}", RegexOptions.Singleline).Value;
        Check(permissions.Contains("if (RequestSource.IsClient(source) && !RequestGuard.Admit(sourcePlayer, RequestGuard.RequestPermissions))"),
            "RequestPermissions: only a client's request is rate-limited; other resources' calls are left alone");
        var dropped = Regex.Match(main, @"internal void OnPlayerDropped\(.*?\n        \}", RegexOptions.Singleline).Value;
        Check(dropped.Contains("RequestGuard.Forget(sourcePlayer.Handle);"), "a player's rate limits are forgotten when they leave");
        Check(!main.Contains("LogUnauthorized"), "refusals are logged by the guard");
        foreach (var (handler, permission) in new[] { ("GetOutOfCar", "vMenu.PersonalVehicle.KickPassengers"), ("ClearAreaNearPos", "vMenu.MiscSettings.ClearArea"),
            ("UpdateWeather", "vMenu.WeatherOptions.Menu"), ("UpdateBlackout", "vMenu.WeatherOptions.Blackout"), ("UpdateVehicleBlackout", "vMenu.WeatherOptions.VehicleBlackout"),
            ("UpdateTime", "vMenu.TimeOptions.Menu"), ("AddTeleportLocation", "vMenu.MiscSettings.TeleportSaveLocation") })
        {
            var body = Regex.Match(main, @"internal (?:async )?void " + handler + @"\(.*?\n        \}", RegexOptions.Singleline).Value;
            Check(body.Contains($"RequestGuard.Denied(source, request, \"{permission}\")"), handler + ": a refusal is logged as denied");
        }
        // The vmenuserver console command triggers weather and time with no source; that skips the guard.
        foreach (var handler in new[] { "UpdateWeather", "UpdateBlackout", "UpdateVehicleBlackout", "UpdateWeatherCloudsType", "UpdateTime" })
        {
            var body = Regex.Match(main, @"internal void " + handler + @"\(.*?\n        \}", RegexOptions.Singleline).Value;
            Check(body.Contains("if (source != null && !RequestGuard.Admit(source, request))"), handler + ": the server's own calls still work");
        }
        Console.WriteLine($"PASS: C# wiring ({seen.Count} client events guarded with object arguments and caught exceptions; {droppedHandlers} player-event handlers check their source)");
    }

    // Every request a client can send to vMenu's Lua (net events and ox_lib callbacks).
    static readonly string[] LuaNetEvents =
    {
        "vMenu:backups:upload", "vMenu:backups:download", "vMenu:ClothingCapture:Save",
        "vMenu:telemetry:spawnProvenanceReady", "vMenu:telemetry:beginVehicleSpawn", "vMenu:PSRP:StaminaMode",
        "vMenu:DeptOutfits:Request",
    };
    static readonly string[] LuaCallbacks =
    {
        "vMenu:backups:info", "vMenu:Outfits:Request", "vMenu:Outfits:Generate", "vMenu:Vehicles:Request", "vMenu:Vehicles:Generate",
        "vMenu:vehicleIdentityTicket",
    };

    static void LuaWiring()
    {
        var root = Root();
        var serverDir = Path.Combine(root, "build", "vMenu", "server");
        var netEvents = new List<string>();
        var callbacks = new List<string>();
        foreach (var path in Directory.GetFiles(serverDir, "*.lua", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(serverDir, path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            var raw = Regex.Matches(text, @"\b(RegisterNetEvent|RegisterServerEvent|lib\.callback\.register)\s*\(").Count;
            if (rel == "guard/requestGuard.lua")
            {
                Check(raw == 2, "the guard itself registers once of each kind");
                continue;
            }
            Check(raw == 0, rel + ": registers client requests only through VMenuGuard");
            netEvents.AddRange(Regex.Matches(text, @"VMenuGuard\.netEvent\(\s*['""]([^'""]+)['""]").Cast<Match>().Select(m => m.Groups[1].Value));
            callbacks.AddRange(Regex.Matches(text, @"VMenuGuard\.callback\(\s*['""]([^'""]+)['""]").Cast<Match>().Select(m => m.Groups[1].Value));
            foreach (Match handler in Regex.Matches(text, @"AddEventHandler\(\s*['""]([^'""]+)['""]"))
            {
                Check(handler.Groups[1].Value == "playerDropped", rel + ": local handlers are only playerDropped (not net-safe, so clients can't send them)");
            }
        }
        Check(netEvents.OrderBy(e => e).SequenceEqual(LuaNetEvents.OrderBy(e => e)), "Lua net events: " + string.Join(", ", netEvents));
        Check(callbacks.OrderBy(e => e).SequenceEqual(LuaCallbacks.OrderBy(e => e)), "Lua callbacks: " + string.Join(", ", callbacks));

        var lua = Read("build", "vMenu", "server", "guard", "requestGuard.lua");
        Check(lua.Contains("GetConvar('vmenu_guard_rate_limits', 'true')") && lua.Contains("GetConvarInt('vmenu_guard_burst', 10)")
            && lua.Contains("GetConvarInt('vmenu_guard_per_second', 3)"), "Lua reads the same convars and defaults as C#");
        Check(lua.Contains("AddEventHandler('vMenu:security:report'") && !lua.Contains("RegisterNetEvent('vMenu:security:report'"),
            "the C# log entries arrive on a local event only");

        var manifest = Read("build", "vMenu", "fxmanifest.lua");
        var guardAt = manifest.IndexOf("'server/guard/requestGuard.lua'", StringComparison.Ordinal);
        var scriptsAt = manifest.IndexOf("'server/*.lua'", StringComparison.Ordinal);
        Check(guardAt > 0 && guardAt < scriptsAt, "fxmanifest loads the guard before server/*.lua");

        var cfg = Read("build", "vMenu", "config", "permissions.cfg");
        Check(cfg.Contains("#set vmenu_security_webhook \"\"") && !Regex.IsMatch(cfg, @"^\s*#?\s*setr\s+vmenu_security_webhook", RegexOptions.Multiline),
            "permissions.cfg: the webhook example uses set (setr would send the URL to every player)");
        foreach (var convar in new[] { "vmenu_guard_rate_limits", "vmenu_guard_burst", "vmenu_guard_per_second", "vmenu_guard_permissions_burst",
            "vmenu_guard_permissions_per_minute", "vmenu_security_webhook_interval" })
        {
            Check(cfg.Contains("#set " + convar + " "), "permissions.cfg documents " + convar);
        }
        Console.WriteLine($"PASS: Lua wiring ({netEvents.Count} net events and {callbacks.Count} callbacks through VMenuGuard), manifest order, convars");
    }
}
