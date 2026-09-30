// Run: dotnet run --project tests/permission-bootstrap-retry
// Drives the production retry schedule with a simulated clock, and checks MainMenu
// wires it the way the upgrade plan (4.2) requires.
using System;
using System.IO;
using System.Linq;
using vMenuClient;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    // Simulates the Tick: each step asks the schedule, "sends" a request, then waits.
    // The first `lost` replies never arrive; each later reply builds the menu
    // `replyDelayMs` after its request. lost = int.MaxValue: nothing ever arrives.
    static (int requests, int[] sendTimes) Simulate(int lost, int replyDelayMs, int horizonMs)
    {
        var retry = new PermissionBootstrapRetry();
        var now = 0;
        int? readyAt = null;
        var sends = new System.Collections.Generic.List<int>();
        while (now <= horizonMs)
        {
            var wait = retry.Next(readyAt.HasValue && now >= readyAt.Value);
            if (wait < 0) break;
            sends.Add(now);
            if (retry.Requests > lost && !readyAt.HasValue) readyAt = now + replyDelayMs;
            now += wait;
        }
        return (retry.Requests, sends.ToArray());
    }

    static int Main()
    {
        // Normal join: the first reply arrives within seconds; exactly one request.
        var normal = Simulate(lost: 0, replyDelayMs: 2000, horizonMs: 3_600_000);
        Check(normal.requests == 1 && normal.sendTimes[0] == 0, "one request, sent immediately, when the reply arrives");

        // Slow server (restart backlog, reply after 40 s): still only one request.
        var slow = Simulate(lost: 0, replyDelayMs: 40_000, horizonMs: 3_600_000);
        Check(slow.requests == 1, "a reply within the first wait never causes a retry");

        // Lost reply: recovered by the first retry at 45 s.
        var lostOnce = Simulate(lost: 1, replyDelayMs: 2000, horizonMs: 3_600_000);
        Check(lostOnce.requests == 2 && lostOnce.sendTimes[1] == 45_000, "lost reply retried once at 45 s");

        // Never answered: keeps trying (never silently gives up), backing off to 5 min.
        var never = Simulate(lost: int.MaxValue, replyDelayMs: 0, horizonMs: 3_600_000);
        Check(never.sendTimes.Take(5).SequenceEqual(new[] { 0, 45_000, 135_000, 315_000, 615_000 }),
            "backoff 45 s, 90 s, 180 s, then 300 s");
        Check(never.requests <= 16, $"at most ~16 requests in an hour, got {never.requests}");
        for (var i = 4; i < never.sendTimes.Length; i++)
            Check(never.sendTimes[i] - never.sendTimes[i - 1] == 300_000, "steady 5 min cadence");

        // IsRetry is false only for the first request.
        var r = new PermissionBootstrapRetry();
        r.Next(false); Check(!r.IsRetry, "first request is not a retry");
        r.Next(false); Check(r.IsRetry, "second request is a retry");
        Check(r.Next(true) == -1 && r.Requests == 2, "ready stops without another request");
        Console.WriteLine("PASS: schedule (normal, slow, lost once, never answered)");

        // Wiring: MainMenu must request via the tick only, and ready must mean the menu exists.
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        var main = File.ReadAllText(Path.Combine(dir, "vMenu", "MainMenu.cs"));
        var requestCount = main.Split("TriggerServerEvent(\"vMenu:RequestPermissions\")").Length - 1;
        Check(requestCount == 1, "exactly one RequestPermissions call site (inside the tick)");
        Check(main.Contains("Tick += PermissionBootstrapTick;"), "tick registered at startup");
        Check(main.Contains("permissionBootstrap.Next(ArePermissionsSetup && ConfigOptionsSetupComplete && Menu != null)"),
            "ready = permissions + config applied + menu built");
        Check(main.Contains("Tick -= PermissionBootstrapTick;"), "tick removes itself when ready");
        Check(main.Contains("if (Menu != null)"), "PostPermissionsSetup still ignores a duplicate SetPermissions");
        Console.WriteLine("PASS: MainMenu wiring");
        return 0;
    }
}
