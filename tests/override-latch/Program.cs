// Run: dotnet run --project tests/override-latch
// Drives the production OverrideLatch with a simulated world in which another resource
// also writes the same setting, and checks FunctionsController no longer writes those
// settings unconditionally every frame.
using System;
using System.Collections.Generic;
using System.IO;
using vMenuClient;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    // One boolean setting per entity (e.g. invincible). Default false; the vMenu override
    // sets it true. Counts vMenu's writes so "never touches" can be asserted.
    sealed class World
    {
        public readonly Dictionary<int, bool> Value = new();
        public readonly HashSet<int> Exists = new();
        public int VMenuWrites;

        public void Spawn(int entity) { Exists.Add(entity); Value[entity] = false; }

        // Mirrors FunctionsController.ApplyOverride.
        public void Tick(OverrideLatch latch, bool enabled, int target)
        {
            var apply = latch.Update(enabled, target, out var previous);
            if (previous != 0 && Exists.Contains(previous)) { Value[previous] = false; VMenuWrites++; }
            if (apply) { Value[target] = true; VMenuWrites++; }
        }
    }

    static int Main()
    {
        // Option never enabled: another resource's value survives and vMenu never writes.
        var w = new World(); w.Spawn(1);
        var latch = new OverrideLatch();
        w.Value[1] = true; // e.g. a downed-state script makes the player invincible
        for (var i = 0; i < 100; i++) w.Tick(latch, false, 1);
        Check(w.Value[1] && w.VMenuWrites == 0, "option never on: vMenu leaves other resources' setting alone");

        // Option on: enforced every tick, even if something else resets it.
        w = new World(); w.Spawn(1); latch = new OverrideLatch();
        w.Tick(latch, true, 1);
        Check(w.Value[1], "option on: applied");
        w.Value[1] = false;
        w.Tick(latch, true, 1);
        Check(w.Value[1], "option on: re-applied the next tick");

        // Option turned off: restored once, then left alone.
        w.Tick(latch, false, 1);
        Check(!w.Value[1], "option off: restored to default");
        var writes = w.VMenuWrites;
        w.Value[1] = true;
        for (var i = 0; i < 50; i++) w.Tick(latch, false, 1);
        Check(w.Value[1] && w.VMenuWrites == writes, "after restoring once, vMenu stops writing");

        // Target changes while on (new ped or vehicle): old one restored, new one applied.
        w = new World(); w.Spawn(1); w.Spawn(2); latch = new OverrideLatch();
        w.Tick(latch, true, 1);
        w.Tick(latch, true, 2);
        Check(!w.Value[1] && w.Value[2], "switching target restores the old entity and applies the new one");

        // No current target (on foot) while on: the last entity keeps the override.
        w.Tick(latch, true, 0);
        Check(w.Value[2], "target 0 while on: last entity untouched");
        // Turning the option off while on foot restores the last entity.
        w.Tick(latch, false, 0);
        Check(!w.Value[2], "option off with no target: last entity restored");

        // A deleted entity is not written to.
        w = new World(); w.Spawn(1); w.Spawn(2); latch = new OverrideLatch();
        w.Tick(latch, true, 1);
        w.Exists.Remove(1);
        writes = w.VMenuWrites;
        w.Tick(latch, true, 2);
        Check(w.VMenuWrites == writes + 1 && w.Value[2], "restore skips an entity that no longer exists");
        Console.WriteLine("PASS: latch (never on, enforce, restore once, retarget, no target, deleted)");

        // Wiring: the old unconditional per-frame writes are gone from FunctionsController.
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        var fc = File.ReadAllText(Path.Combine(dir, "vMenu", "FunctionsController.cs"));
        foreach (var removed in new[]
        {
            "SetEntityInvincible(Game.PlayerPed.Handle, MainMenu.PlayerOptionsMenu.PlayerGodMode",
            "SetPedCanRagdoll(Game.PlayerPed.Handle, (!",
            "Game.PlayerPed.CanBeDraggedOutOfVehicle = !",
            "veh.IsInvincible = invincibleGod",
            "veh.CanBeVisiblyDamaged = !visualGod",
            "SetRampVehicleReceivesRampDamage(veh.Handle, !rampGod)",
            "ped.CanWearHelmet = true;",
            "SetVehicleEnginePowerMultiplier(veh.Handle, 1f)",
        })
        {
            Check(!fc.Contains(removed), "unconditional write removed: " + removed);
        }
        foreach (var latchName in new[]
        {
            "godModeLatch", "noRagdollLatch", "dragOutLatch", "shotInVehicleLatch", "knockOffBikeLatch",
            "rampGodLatch", "visualGodLatch", "engineGodLatch", "strongWheelsGodLatch", "invincibleGodLatch",
            "noHelmetLatch", "powerMultiplierLatch", "noSirenLatch", "planeTurbulenceLatch", "heliTurbulenceLatch",
        })
        {
            Check(fc.Contains("ApplyOverride(" + latchName + ","), "setting goes through its latch: " + latchName);
        }
        Console.WriteLine("PASS: FunctionsController wiring");
        return 0;
    }
}
