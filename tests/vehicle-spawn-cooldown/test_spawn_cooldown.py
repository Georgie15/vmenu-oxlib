"""Run the production gate, both SpawnVehicle entry points, the model loader and the blocked
messages with a fake clock/core and frame scheduler.

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
    'private static async Task<T> WaitForVehicleIdentity<T>(',
    'private static void NotifySpawnBlocked('))
constants = '\n'.join(re.findall(
    r'^        private const (?:uint|int) Vehicle(?:ModelLoad|Identity)TimeoutMilliseconds = \d+;$',
    common, re.M) + re.findall(r'^        private static VehicleSpawnAttempt _loadingSpinnerOwner;$', common, re.M))
assert constants.count('\n') == 2
# The core must not re-check the gate it already owns or manage a second timer.
core = member('private static async Task<int> SpawnVehicleCore(')
assert 'VehicleSpawnerCooldownEnabled' not in core
assert 'StartVehicleCooldown' not in common
assert 'await IdentityBridge.BeginVehicleIdentity' not in core
assert 'await IdentityBridge.ClaimVehicleIdentity' not in core
assert core.count('await WaitForVehicleIdentity(') == 2
# A request can only be replaced while it is loading: it must commit before it deletes the old
# vehicle, asks CAD for an identity or creates anything.
assert core.count('attempt.TryBeginCreating()') == 1
commit = core.index('attempt.TryBeginCreating()')
assert all(core.index(step) > commit for step in ('.Delete()', 'BeginVehicleIdentity(', 'CreateVehicle('))

program = r'''
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using vMenuClient;

class Program
{
    static uint now;
    static VehicleSpawnGate SpawnGate = new VehicleSpawnGate(() => now);
    static int VehicleSpawnerCooldown = 1000;
    static Func<VehicleSpawnAttempt, Task<int>> spawn;
    static int calls, inputs;
    static string notice;
    static Func<Task<string>> input = () => Task.FromResult("adder");
    static bool modelValid, modelLoaded, manualFrames;
    static HashSet<uint> loadedModels = new HashSet<uint>();
    static List<uint> created = new List<uint>();
    static List<TaskCompletionSource<bool>> frames = new List<TaskCompletionSource<bool>>();
    static int requested, released, pollingFrames, spinnersShown;
    static bool spinnerOn;
    static string spinnerText;
    static TaskCompletionSource<bool> identityTimer;
    static bool IsModelInCdimage(uint hash) => modelValid;
    static bool HasModelLoaded(uint hash) => modelLoaded || loadedModels.Contains(hash);
    static void RequestModel(uint hash) { requested++; }
    static void SetModelAsNoLongerNeeded(uint hash) { released++; }
    static void BeginTextCommandBusyspinnerOn(string entry) => Check(entry == "STRING", "spinner shows a plain string");
    static void AddTextComponentSubstringPlayerName(string text) { spinnerText = text; }
    static void EndTextCommandBusyspinnerOn(int type) { spinnerOn = true; spinnersShown++; }
    static void BusyspinnerOff() { spinnerOn = false; }
    static int GetGameTimer() => unchecked((int)now);
    static Task Delay(int milliseconds)
    {
        if (milliseconds == 0)
        {
            if (manualFrames)
            {
                // Like FiveM's Delay(0): resumes on the next frame, when NextFrame() runs.
                var frame = new TaskCompletionSource<bool>();
                frames.Add(frame);
                return frame.Task;
            }
            // Deterministically reproduce a streamed model that never loads.
            if (++pollingFrames > 16) throw new Exception("unbounded model-loading wait holds spawn gate forever");
            now = unchecked(now + 1000);
            return Task.CompletedTask;
        }
        Check(milliseconds == 10000, "identity timeout is 10 seconds");
        identityTimer = new TaskCompletionSource<bool>();
        return identityTimer.Task;
    }
    static void NextFrame()
    {
        now = unchecked(now + 16);
        var waiting = frames.ToArray();
        frames.Clear();
        foreach (var frame in waiting) frame.SetResult(true);
    }
    static class Debug { public static void WriteLine(string message) { } }
    public struct VehicleInfo { }
    enum CommonErrors { InvalidInput }
    static class Notify
    {
        public static void Error(string message) { notice = "error: " + message; }
        public static void Info(string message) { notice = "info: " + message; }
        public static void Error(CommonErrors error) { }
    }
    static class VehicleSpawner { public static string GetVehicleName(uint model) => "car" + model; }
    static bool CanDoInteraction(string action) => true;
    static int GetHashKey(string name) => 123;
    static Task<string> GetUserInput(string windowTitle) { inputs++; return input(); }
    static Task<int> SpawnVehicleCore(VehicleSpawnAttempt attempt, uint hash, bool inside, bool replace, bool skip,
        VehicleInfo info, string save, float x, float y, float z, float heading)
    {
        calls++;
        Check(attempt.Model == hash && attempt.SaveName == save, "attempt describes the request");
        return spawn(attempt);
    }
    // A core that is past loading, then finishes when the task does.
    static Func<VehicleSpawnAttempt, Task<int>> Creating(Task<int> result)
        => attempt => { Check(attempt.TryBeginCreating(), "unreplaced attempt may create"); return result; };
    // The production core's loading steps: load, report a timeout, stop if replaced, create.
    static async Task<int> LoadThenCreate(VehicleSpawnAttempt attempt)
    {
        if (!await LoadModel(attempt.Model, attempt) && !attempt.IsCancelled) return 0;
        if (!attempt.TryBeginCreating()) return 0;
        created.Add(attempt.Model);
        return (int)attempt.Model;
    }
    static Task<int> Saved() => SpawnVehicle(123u, true, true, false, new VehicleInfo(), "saved");
    static Task<int> Model(uint hash, string save = null) => SpawnVehicle(hash, true, true, false, new VehicleInfo(), save);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Reset()
    {
        now = 0; calls = 0; inputs = 0; notice = null; VehicleSpawnerCooldown = 1000;
        SpawnGate = new VehicleSpawnGate(() => now);
        spawn = Creating(Task.FromResult(42));
        input = () => Task.FromResult("adder");
        modelValid = true; modelLoaded = false; manualFrames = false;
        loadedModels.Clear(); created.Clear(); frames.Clear();
        requested = 0; released = 0; pollingFrames = 0; spinnersShown = 0; identityTimer = null;
        spinnerOn = false; spinnerText = null; _loadingSpinnerOwner = null;
    }
    static async Task Main()
    {
        Reset();
        var pending = new TaskCompletionSource<int>();
        spawn = Creating(pending.Task);
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
        spawn = Creating(Task.FromResult(43));
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
        spawn = a => Task.FromResult(0);
        Check(await SpawnVehicle("invalid") == 0, "invalid model rejected");
        spawn = Creating(Task.FromResult(42));
        Check(await Saved() == 42, "failed named spawn does not start wrapper cooldown");
        Reset();
        spawn = a => Task.FromException<int>(new InvalidOperationException());
        try { await Saved(); throw new Exception("expected spawn exception"); }
        catch (InvalidOperationException) { }
        spawn = Creating(Task.FromResult(42));
        Check(await Saved() == 42, "exception releases lock");
        Console.WriteLine("PASS: validation failure and exception recovery");

        foreach (int duration in new[] { 0, -20 })
        {
            Reset(); VehicleSpawnerCooldown = duration;
            pending = new TaskCompletionSource<int>(); spawn = Creating(pending.Task);
            first = Saved();
            Check(await Saved() == 0, "zero/negative cooldown still locks pending spawn");
            pending.SetResult(42); await first;
            spawn = Creating(Task.FromResult(43));
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
            spawn = async a => await LoadModel(123, a) ? 42 : 0;
            Check(await Saved() == 0, "stalled model cancels without creating a vehicle");
            Check(unchecked(now - start) == 15000 && released == 1, "timeout bounds streaming wait and releases model");
            Check(spinnersShown == 1 && spinnerText == "Loading car123" && !spinnerOn, "loading spinner shown for the wait and removed on timeout");
            modelLoaded = true;
            Check(await Saved() == 42, "model timeout releases gate with no success cooldown");
        }
        Reset(); modelValid = false;
        Check(!await LoadModel(123, new VehicleSpawnAttempt(123, null)) && requested == 0, "invalid model does not request streaming");
        Reset(); modelLoaded = true;
        Check(await LoadModel(123, new VehicleSpawnAttempt(123, null)) && pollingFrames == 0 && released == 0 && spinnersShown == 0,
            "loaded model stays available for creation, without a spinner");
        Console.WriteLine("PASS: stalled/invalid/loaded models, timeout recovery and timer wrap");

        foreach (bool begin in new[] { true, false })
        {
            Reset();
            var ticket = new TaskCompletionSource<string>();
            var claim = new TaskCompletionSource<bool>();
            string resultTicket = null; bool resultClaim = true;
            spawn = async a =>
            {
                a.TryBeginCreating();
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
            var next = new TaskCompletionSource<int>(); spawn = Creating(next.Task);
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

        Reset(); manualFrames = true; spawn = LoadThenCreate;
        var slow = Model(1);
        Check(!slow.IsCompleted && SpawnGate.Block == VehicleSpawnBlock.Loading, "a streaming model holds the gate as loading");
        Check(spinnerOn && spinnerText == "Loading car1", "the player sees which vehicle is loading");
        Check(await Model(1) == 0 && calls == 1 && notice == "info: Still loading car1. Choosing a different vehicle cancels it.",
            "the same vehicle again says it is still loading, not on cooldown");
        input = () => Task.FromResult("");
        Check(await SpawnVehicle() == 0 && inputs == 1 && calls == 1, "spawn by name can still be opened while a vehicle loads");
        Check(SpawnGate.Current.Model == 1, "closing the prompt leaves the loading vehicle alone");
        var other = Model(2);
        Check(calls == 2 && SpawnGate.Current.Model == 2 && SpawnGate.Block == VehicleSpawnBlock.Loading, "a different vehicle replaces the loading one");
        Check(spinnerText == "Loading car2", "the spinner names the replacement");
        NextFrame();
        Check(slow.IsCompleted && slow.Result == 0 && created.Count == 0, "the replaced request stops before creating anything");
        Check(spinnerOn && SpawnGate.Current.Model == 2, "the replaced request leaves the replacement's spinner and gate alone");
        loadedModels.Add(2); NextFrame();
        Check(await other == 2 && created.Count == 1 && created[0] == 2 && !spinnerOn, "the replacement loads and spawns");
        Check(SpawnGate.Block == VehicleSpawnBlock.Cooldown && await Model(3) == 0 && notice == "error: Vehicle spawner is on cooldown.",
            "only a completed spawn starts the cooldown, and says so");
        Console.WriteLine("PASS: loading message, spinner, replacing a loading vehicle, cooldown after completion");

        Reset(); manualFrames = true; spawn = LoadThenCreate; loadedModels.Add(3);
        slow = Model(1);
        Check(await Model(3) == 3 && created.Count == 1, "an already-loaded replacement spawns at once");
        Check(spinnerOn, "the replaced request still owns the spinner until it stops");
        NextFrame();
        Check(slow.Result == 0 && !spinnerOn && created.Count == 1, "the replaced request removes its own spinner and creates nothing");
        Check(SpawnGate.Block == VehicleSpawnBlock.Cooldown, "the replaced request does not clear the replacement's cooldown");
        Reset(); manualFrames = true; spawn = LoadThenCreate;
        slow = Model(5, "blue");
        var red = Model(5, "red");
        Check(calls == 2 && SpawnGate.Current.SaveName == "red", "another saved vehicle of the same model replaces the loading one");
        NextFrame();
        Check(slow.Result == 0 && !red.IsCompleted, "the replaced save stops; the newer one keeps loading");
        Console.WriteLine("PASS: cached replacement, spinner ownership, saved vehicles of the same model");

        Reset(); var creating = new TaskCompletionSource<int>(); spawn = Creating(creating.Task);
        var busy = Model(7);
        Check(await Model(8) == 0 && calls == 1 && notice == "info: car7 is still spawning.", "a request past loading cannot be replaced");
        Check(await SpawnVehicle() == 0 && inputs == 0 && notice == "info: car7 is still spawning.", "spawn by name says the vehicle is still spawning");
        creating.SetResult(7); await busy;
        Console.WriteLine("PASS: requests past loading are not replaced and say they are still spawning");
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
