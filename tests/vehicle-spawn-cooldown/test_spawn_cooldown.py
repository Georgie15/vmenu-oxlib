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
# The core must not re-check the gate it already owns or manage a second timer.
core = member('private static async Task<int> SpawnVehicleCore(')
assert 'VehicleSpawnerCooldownEnabled' not in core
assert 'StartVehicleCooldown' not in common

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
    }
''' + wrappers + '\n}'

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
