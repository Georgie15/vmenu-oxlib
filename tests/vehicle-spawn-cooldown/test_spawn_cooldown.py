"""Run the production gate and both SpawnVehicle entry points with a fake clock/core.

Requires Python and the .NET 9 SDK; no FiveM runtime or external test packages.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
common = (root / 'vMenu/CommonFunctions.cs').read_text(encoding='utf-8-sig')


def member(signature):
    return re.search(r'^        ' + re.escape(signature) + r'.*?^        }$',
                     common, re.M | re.S)[0]


wrappers = '\n'.join(member('public static async Task<int> SpawnVehicle(' + kind)
                     for kind in ('string', 'uint'))
waits = '\n'.join(member(signature) for signature in (
    'private static async Task<bool> LoadModel(',
    'private static async Task<T> WaitForVehicleIdentity<T>('))
constants = '\n'.join(re.findall(
    r'^        private const (?:uint|int) Vehicle(?:ModelLoad|Identity)TimeoutMilliseconds = \d+;$',
    common, re.M))
# The core must not re-check the gate it already owns or manage a second timer.
core = member('private static async Task<int> SpawnVehicleCore(')
assert 'VehicleSpawnerCooldownEnabled' not in core
assert 'StartVehicleCooldown' not in common
assert 'await IdentityBridge.BeginVehicleIdentity' not in core
assert 'await IdentityBridge.ClaimVehicleIdentity' not in core
assert core.count('await WaitForVehicleIdentity(') == 2

program = r'''
using System;
using System.Threading.Tasks;
using vMenuClient;

class Program
{
    static uint now;
    static VehicleSpawnGate SpawnGate = new VehicleSpawnGate(() => now);
    static bool VehicleSpawnerCooldownEnabled => SpawnGate.IsBlocked;
    static int VehicleSpawnerCooldown = 1000;
    static Func<Task<int>> spawn;
    static int calls, blocked;
    static Func<Task<string>> input = () => Task.FromResult("adder");
    static bool modelValid, modelLoaded;
    static int requested, released, pollingFrames;
    static TaskCompletionSource<bool> identityTimer;
    static bool IsModelInCdimage(uint hash) => modelValid;
    static bool HasModelLoaded(uint hash) => modelLoaded;
    static void RequestModel(uint hash) { requested++; }
    static void SetModelAsNoLongerNeeded(uint hash) { released++; }
    static int GetGameTimer() => unchecked((int)now);
    static Task Delay(int milliseconds)
    {
        if (milliseconds == 0)
        {
            // Deterministically reproduce a streamed model that never loads.
            if (++pollingFrames > 16) throw new Exception("unbounded model-loading wait holds spawn gate forever");
            now = unchecked(now + 1000);
            return Task.CompletedTask;
        }
        Check(milliseconds == 10000, "identity timeout is 10 seconds");
        identityTimer = new TaskCompletionSource<bool>();
        return identityTimer.Task;
    }
    static class Debug { public static void WriteLine(string message) { } }
    public struct VehicleInfo { }
    enum CommonErrors { InvalidInput }
    static class Notify
    {
        public static void Error(string message) { blocked++; }
        public static void Error(CommonErrors error) { }
    }
    static bool CanDoInteraction(string action) => true;
    static int GetHashKey(string name) => 123;
    static Task<string> GetUserInput(string windowTitle) => input();
    static Task<int> SpawnVehicleCore(uint hash, bool inside, bool replace, bool skip,
        VehicleInfo info, string save, float x, float y, float z, float heading)
    {
        calls++;
        return spawn();
    }
    static Task<int> Saved() => SpawnVehicle(123u, true, true, false, new VehicleInfo(), "saved");
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Reset()
    {
        now = 0; calls = 0; blocked = 0; VehicleSpawnerCooldown = 1000;
        SpawnGate = new VehicleSpawnGate(() => now);
        spawn = () => Task.FromResult(42);
        input = () => Task.FromResult("adder");
        modelValid = true; modelLoaded = false;
        requested = 0; released = 0; pollingFrames = 0; identityTimer = null;
    }
    static async Task Main()
    {
        Reset();
        var pending = new TaskCompletionSource<int>();
        spawn = () => pending.Task;
        var first = Saved();
        Check(!first.IsCompleted && calls == 1, "first spawn reaches asynchronous setup");
        foreach (uint t in new uint[] { 0, 100, 650, 4000 })
        {
            now = t;
            Check(await Saved() == 0, "saved spam rejected throughout setup");
            Check(await SpawnVehicle("adder") == 0, "model name shares the same gate");
        }
        Check(calls == 1, "no duplicate entity-creation attempt");
        pending.SetResult(42);
        Check(await first == 42, "successful spawn returns without waiting for cooldown");
        spawn = () => Task.FromResult(43);
        now = 4999;
        Check(await Saved() == 0, "full cooldown measured from completion");
        now = 5000;
        Check(await SpawnVehicle("adder") == 43, "exact boundary admits next spawn");
        now = 5500;
        Check(await Saved() == 0, "later spawn owns a fresh cooldown");
        now = 6000;
        Check(await Saved() == 43, "rejected clicks do not extend cooldown");
        Console.WriteLine("PASS: overlapping saved/model spawns, long setup, boundaries, no sliding timer");

        Reset();
        spawn = () => Task.FromResult(0);
        Check(await SpawnVehicle("invalid") == 0, "invalid model rejected");
        spawn = () => Task.FromResult(42);
        Check(await Saved() == 42, "failed named spawn does not start wrapper cooldown");
        Reset();
        spawn = () => Task.FromException<int>(new InvalidOperationException());
        try { await Saved(); throw new Exception("expected spawn exception"); }
        catch (InvalidOperationException) { }
        spawn = () => Task.FromResult(42);
        Check(await Saved() == 42, "exception releases lock");
        Console.WriteLine("PASS: validation failure and exception recovery");

        foreach (int duration in new[] { 0, -20 })
        {
            Reset(); VehicleSpawnerCooldown = duration;
            pending = new TaskCompletionSource<int>(); spawn = () => pending.Task;
            first = Saved();
            Check(await Saved() == 0, "zero/negative cooldown still locks pending spawn");
            pending.SetResult(42); await first;
            spawn = () => Task.FromResult(43);
            Check(await Saved() == 43, "zero/negative cooldown allows immediate next completion");
        }
        Reset(); now = uint.MaxValue - 499;
        await Saved();
        now = 499;
        Check(await Saved() == 0, "cooldown survives game timer wrap");
        now = 500;
        Check(await Saved() == 42, "wrapped cooldown expires at correct boundary");
        Console.WriteLine("PASS: zero/negative cooldown and timer rollover");

        Reset(); input = () => Task.FromResult("");
        Check(await SpawnVehicle() == 0 && calls == 0, "cancelled prompt does not spawn");
        Check(await Saved() == 42, "cancelled prompt does not lock spawner");
        Reset();
        var prompt = new TaskCompletionSource<string>(); input = () => prompt.Task;
        var custom = SpawnVehicle();
        Check(await Saved() == 42, "prompt alone does not reserve spawner");
        prompt.SetResult("adder");
        Check(await custom == 0 && calls == 1, "submitted prompt rechecks shared gate");
        Reset();
        Check(await SpawnVehicle() == 42 && await Saved() == 0, "custom spawn consumes shared cooldown");
        Console.WriteLine("PASS: custom prompt cancellation, submission race, shared cooldown");

        foreach (uint start in new uint[] { 0, uint.MaxValue - 7499 })
        {
            Reset(); now = start;
            spawn = async () => await LoadModel(123) ? 42 : 0;
            Check(await Saved() == 0, "stalled model cancels without creating a vehicle");
            Check(unchecked(now - start) == 15000 && released == 1, "timeout bounds streaming wait and releases model");
            modelLoaded = true;
            Check(await Saved() == 42, "model timeout releases gate with no success cooldown");
        }
        Reset(); modelValid = false;
        Check(!await LoadModel(123) && requested == 0, "invalid model does not request streaming");
        Reset(); modelLoaded = true;
        Check(await LoadModel(123) && pollingFrames == 0 && released == 0, "loaded model stays available for creation");
        Console.WriteLine("PASS: stalled/invalid/loaded models, timeout recovery and timer wrap");

        foreach (bool begin in new[] { true, false })
        {
            Reset();
            var ticket = new TaskCompletionSource<string>();
            var claim = new TaskCompletionSource<bool>();
            string resultTicket = null; bool resultClaim = true;
            spawn = async () =>
            {
                if (begin) resultTicket = await WaitForVehicleIdentity(ticket.Task, "", "begin");
                else resultClaim = await WaitForVehicleIdentity(claim.Task, false, "claim");
                return 42;
            };
            first = Saved();
            Check(!first.IsCompleted && await Saved() == 0, "CAD wait retains single-spawn protection");
            now = 10000; identityTimer.SetResult(true);
            Check(await first == 42 && (begin ? resultTicket == "" : !resultClaim), "stalled CAD call uses existing fail-open fallback");
            Check(await Saved() == 0, "spawn completing after CAD timeout still consumes cooldown");
            now = 11000;
            var next = new TaskCompletionSource<int>(); spawn = () => next.Task;
            var nextSpawn = Saved();
            ticket.SetResult("late ticket"); claim.SetResult(true);
            Check(await Saved() == 0 && calls == 2, "late CAD completion cannot unlock or repeat a newer spawn");
            next.SetResult(43); Check(await nextSpawn == 43, "newer spawn finishes normally");
        }
        Reset();
        Check(await WaitForVehicleIdentity(Task.FromResult("ticket"), "", "begin") == "ticket" && identityTimer == null,
            "completed identity response avoids timeout timer");
        var healthy = new TaskCompletionSource<string>();
        var response = WaitForVehicleIdentity(healthy.Task, "", "begin");
        healthy.SetResult("healthy ticket");
        Check(await response == "healthy ticket", "identity response before timeout preserved");
        Console.WriteLine("PASS: stalled CAD begin/claim, healthy responses, late responses and shared cooldown");
    }
''' + constants + '\n' + waits + '\n' + wrappers + '\n}'

with tempfile.TemporaryDirectory(prefix='vmenu-spawn-cooldown-') as temp:
    work = Path(temp).resolve()
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    (work / 'Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>''', encoding='utf-8')
    (work / 'VehicleSpawnGate.cs').write_bytes((root / 'vMenu/VehicleSpawnGate.cs').read_bytes())
    (work / 'Program.cs').write_text(program, encoding='utf-8')
    subprocess.run(['dotnet', 'run', '--project', str(work / 'Tests.csproj'),
                    '--configuration', 'Release', '--verbosity', 'quiet'], check=True)
