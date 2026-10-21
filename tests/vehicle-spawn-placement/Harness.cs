using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using static CitizenFX.Core.Native.API;

namespace vMenuClient
{
    partial class Program
    {
        static Vehicle _previousVehicle;
        static readonly IdentityStub IdentityBridge = new IdentityStub();
        public struct VehicleInfo { public string cadSaveToken { get; set; } }
        enum Permission { VSDisableReplacePrevious }
        enum CommonErrors { InvalidModel }
        static class Notify
        {
            public static void Alert(string text) { }
            public static void Error(string text) { }
            public static void Error(CommonErrors error) { }
            public static void Info(string text) { }
        }
        static class VehicleSpawner { public static bool[] allowedCategories = Enumerable.Repeat(true, 22).ToArray(); }
        static class UserDefaults { public static int VehicleDefaultRadio = 0; }
        class IdentityStub
        {
            public Task<string> BeginVehicleIdentity(uint hash, string token) => Task.FromResult("ticket");
            public Task<bool> ClaimVehicleIdentity(string ticket, int handle, string token)
            { IdentityClaimed = true; return Task.FromResult(true); }
        }
        static Task<T> WaitForVehicleIdentity<T>(Task<T> task, T fallback, string stage) => task;
        static bool CanDoInteraction(string action) => true;
        static bool IsAllowed(Permission permission) => true;
        static Task<bool> LoadModel(uint model) => Task.FromResult(true);
        static Vehicle GetVehicle() => Game.PlayerPed.CurrentVehicle;
        static void SendNotification(string text) { }
        static void Log(string text) { }
        static void ApplyVehicleModsDelayed(Vehicle vehicle, VehicleInfo info, int delay) { ModsApplied++; }
        static Task ApplyRespawnSpeedDebuff(Vehicle vehicle) => Task.CompletedTask;
        // Vehicles the player's model preset was applied to.
        static readonly List<int> PresetsApplied = new List<int>();
        static bool ApplyVehiclePreset(int vehicle) { PresetsApplied.Add(vehicle); return true; }
        static Task Delay(int milliseconds) => BaseScript.Delay(milliseconds);
        // Spawn provenance events, with the number of vehicles that existed when each fired.
        static readonly List<string> Events = new List<string>();
        static void TriggerEvent(string name, params object[] args)
            => Events.Add($"{name}({string.Join(",", args)})@{Positions.Count}");
        internal static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
        }
        static void Near(float actual, float expected, string description) => Check(Math.Abs(actual - expected) < 0.001f, description);
        static void Reset(float playerZ = -19f)
        {
            ResetWorld(playerZ);
            _previousVehicle = null;
            Events.Clear();
            PresetsApplied.Clear();
        }
        static Task<int> Spawn(bool inside = true, bool replace = true, string save = null,
            float x = 0, float y = 0, float z = 0)
            => SpawnVehicleCore(123, inside, replace, false, new VehicleInfo(), save, x, y, z, -1);

        static async Task Main()
        {
            // Multiple collision surfaces: underground floor, upper deck, roof, lower deck.
            foreach (float floor in new[] { -20f, 0f, 12f })
            {
                foreach (float wrongFloor in new[] { floor + 4f, floor + 20f, floor - 4f })
                {
                    Reset(floor + 1f);
                    Surfaces.AddRange(new[] { new Surface(floor), new Surface(floor + 4), new Surface(floor + 20), new Surface(floor - 4) });
                    Snap = v => { Positions[v.Handle] = new Vector3(100, 200, wrongFloor + 0.55f); return true; };
                    var handle = await Spawn();
                    Near(Positions[handle].Z, floor + 0.55f, "wrong floor snap restored to the local floor");
                    Near(SeatedAt.Z, floor + 0.55f, "player seated only after restoring the correct floor");
                    Check(PlacementBeforeSeating && PlaceCalls == 1, "placement completed before player warp");
                    Near(LastRayTop, floor + 1.25f, "probe begins near player, below next deck");
                }
            }
            Console.WriteLine("PASS: underground, ground and upper decks; roof/upper/lower snap rejected before seating");

            Reset(); Surfaces.Add(new Surface(-20));
            Snap = v => { Positions[v.Handle] = new Vector3(100, 200, -19.6f); Rotations[v.Handle] = new Vector3(10, 2, 90); return true; };
            var ramp = await Spawn(save: "saved");
            Near(Positions[ramp].Z, -19.6f, "nearby wheel alignment preserved");
            Near(Rotations[ramp].X, 10, "native ramp pitch preserved");
            Check(ModsApplied == 1, "saved vehicle setup still runs");
            Check(ModelReleased && IdentityClaimed, "normal model cleanup and identity claim still run");

            foreach (bool succeeded in new[] { true, false })
            {
                Reset(); Surfaces.Add(new Surface(-20));
                Snap = v => { Positions[v.Handle] = new Vector3(120, 200, -19.45f); Rotations[v.Handle] = new Vector3(60, 0, 0); return succeeded; };
                var handle = await Spawn();
                Near(Positions[handle].X, 100, "horizontal relocation rejected");
                Near(Rotations[handle].X, 0, "rejected placement restores rotation");
                Near(Rotations[handle].Z, 90, "original heading preserved");
            }
            Reset(); Surfaces.Add(new Surface(-20));
            Snap = v => { Positions[v.Handle] = new Vector3(100, 200, -19.5f); Rotations[v.Handle] = new Vector3(60, 0, 0); return false; };
            var failed = await Spawn();
            Near(Positions[failed].Z, -19.45f, "failed native restores position even after a small displacement");
            Near(Rotations[failed].X, 0, "failed native restores rotation");
            Console.WriteLine("PASS: ramp alignment, saved setup, failed native, horizontal relocation and rotation rollback");

            // Simulate collision streaming in later, and a pending asynchronous probe.
            Reset(); Surfaces.Add(new Surface(-20)); CollisionReadyAt = 300; PendingPolls = 2;
            var streamed = await Spawn();
            Check(Now >= 300 && Now < 1000 && ProbeStarts > 1, "streaming is retried with a bounded wait");
            Near(Positions[streamed].Z, -19.45f, "streamed floor found");
            Check(Requests > 1 && LastFlags == 17 && LastIgnored == Game.PlayerPed.Handle,
                "requests collision and excludes vehicles, peds and water");

            foreach (string failure in new[] { "missing", "pending", "invalid", "wall", "above", "below", "airborne" })
            {
                Reset(failure == "airborne" ? 100 : -19);
                if (failure == "pending") PendingPolls = int.MaxValue;
                if (failure == "invalid") InvalidRay = true;
                if (failure == "wall") Surfaces.Add(new Surface(-20, normalZ: 0.1f));
                if (failure == "above") ForcedHit = new Surface(-15);
                if (failure == "below") ForcedHit = new Surface(-30);
                if (failure == "airborne") Surfaces.Add(new Surface(0));
                var handle = await Spawn();
                Near(Positions[handle].Z, failure == "airborne" ? 100 : -19, "no local floor preserves requested altitude: " + failure);
                Check(PlaceCalls == 0 && Now == 1001, "failed/pending collision times out without global ground placement: " + failure);
            }
            Reset(); Now = uint.MaxValue - 499;
            await Spawn(); Check(PlaceCalls == 0 && Now == 501, "collision timeout survives game timer wrap");
            Console.WriteLine("PASS: streamed collision, pending/invalid rays, walls, out-of-range hits, airborne and timer wrap");

            Reset(); Surfaces.Add(new Surface(-20)); Surfaces.Add(new Surface(-19.2f, flags: 2)); Surfaces.Add(new Surface(-19.1f, flags: 4));
            var traffic = await Spawn(); Near(Positions[traffic].Z, -19.45f, "peds and vehicles do not become the floor");
            Reset(); Surfaces.Add(new Surface(-20, flags: 16));
            var platform = await Spawn(); Near(Positions[platform].Z, -19.45f, "object-based floor collision supported");
            Reset(); Surfaces.Add(new Surface(-20)); MinimumZ = -1.5f;
            var truck = await Spawn(); Near(Positions[truck].Z, -18.45f, "placement accounts for model height");

            Reset(); Surfaces.Add(new Surface(-20));
            var outside = await Spawn(inside: false);
            Near(Positions[outside].Y, 208, "outside spawn remains eight metres ahead");
            Near(Positions[outside].Z, -19.45f, "outside spawn also stays on local floor");
            Check(SeatCalls == 0, "outside spawn does not warp player");

            Reset(); Surfaces.Add(new Surface(-20));
            var oldHandle = CreateVehicle(123, 100, 200, -19, 90, true, false);
            var oldVehicle = new Vehicle(oldHandle) { PreviouslyOwnedByPlayer = true };
            Game.PlayerPed.SetIntoVehicle(oldVehicle, VehicleSeat.Driver);
            _previousVehicle = oldVehicle; SeatCalls = 0;
            var replacement = await Spawn();
            Check(!oldVehicle.Exists() && Game.PlayerPed.CurrentVehicle.Handle == replacement, "previous vehicle replaced normally");
            Near(SeatedAt.Z, -19.45f, "replacement uses local floor");

            Reset(); Surfaces.Add(new Surface(-20));
            var explicitHandle = await Spawn(inside: false, x: 5, y: 6, z: 7);
            Near(Positions[explicitHandle].Z, 7, "explicit entity-tool altitude preserved");
            Check(ProbeStarts == 0 && PlaceCalls == 0, "explicit coordinates skip automatic placement");
            foreach (VehicleClass type in new[] { VehicleClass.Boats, VehicleClass.Trains })
            {
                Reset(); ModelClass = (int)type; Surfaces.Add(new Surface(-20));
                var handle = await Spawn(); Near(Positions[handle].Z, -19, "water/track altitude preserved");
                Check(ProbeStarts == 0 && PlaceCalls == 0, "boats/trains skip terrain snap");
            }
            Reset(100); ModelClass = (int)VehicleClass.Helicopters; Surfaces.Add(new Surface(0));
            await Spawn(); Check(HeliBlades && PlaceCalls == 0, "airborne helicopter retains full-speed blades");
            Console.WriteLine("PASS: traffic, object floors, model dimensions, outside spawn, replacement, explicit coords, boats/trains and helicopter");

            Reset(); Surfaces.Add(new Surface(-20));
            var tracked = await Spawn();
            Check(Events.Count == 2
                && Events[0] == "vMenu:telemetry:queueVehicleSpawn()@0"
                && Events[1] == $"vMenu:telemetry:vehicleSpawned({tracked})@1",
                "telemetry ticket requested once before the vehicle exists, then handed the new vehicle: " + string.Join(" | ", Events));
            Console.WriteLine("PASS: spawn provenance asked once before creation and given the created vehicle");

            Check(PresetsApplied.Count == 1 && PresetsApplied[0] == tracked, "a fresh spawn gets the player's preset for its model");
            Reset(); Surfaces.Add(new Surface(-20));
            await Spawn(save: "saved");
            Check(PresetsApplied.Count == 0 && ModsApplied == 1, "a saved vehicle keeps its own setup, not the preset");
            Console.WriteLine("PASS: model presets apply to fresh spawns only");
        }
    }
}

namespace CitizenFX.Core
{
    public struct Vector3
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vector3 Zero => new Vector3();
        public bool IsZero => X == 0 && Y == 0 && Z == 0;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }
    public enum VehicleClass { Boats = 14, Helicopters = 15, Trains = 21 }
    public enum VehicleSeat { Driver }
    public enum RadioStation { Default }
    public class Model { public bool IsTrain => Native.API.ModelClass == 21; }
    public class Vehicle
    {
        public Vehicle(int handle) { Handle = handle; }
        public int Handle;
        public bool NeedsToBeHotwired, PreviouslyOwnedByPlayer, IsPersistent, IsStolen, IsWanted, IsEngineRunning;
        public float CurrentRPM, BodyHealth = 1000, EngineHealth = 1000;
        public RadioStation RadioStation;
        public Model Model = new Model();
        public VehicleClass ClassType => (VehicleClass)Native.API.ModelClass;
        public Ped Driver => Game.PlayerPed;
        public Ped[] Occupants => Game.PlayerPed.CurrentVehicle?.Handle == Handle ? new[] { Game.PlayerPed } : Array.Empty<Ped>();
        public bool Exists() => Native.API.Positions.ContainsKey(Handle);
        public void Delete()
        {
            Native.API.Positions.Remove(Handle);
            if (Game.PlayerPed.CurrentVehicle?.Handle == Handle) Game.PlayerPed.CurrentVehicle = null;
        }
        public bool PlaceOnGround()
        {
            Native.API.PlaceCalls++;
            Native.API.PlacementBeforeSeating &= Game.PlayerPed.CurrentVehicle?.Handle != Handle;
            return Native.API.Snap(this);
        }
    }
    public class Ped
    {
        public int Handle => 1;
        public Vehicle CurrentVehicle;
        public bool IsInVehicle() => CurrentVehicle != null;
        public void SetIntoVehicle(Vehicle vehicle, VehicleSeat seat)
        {
            Native.API.SeatCalls++;
            Native.API.SeatedAt = Native.API.Positions[vehicle.Handle];
            CurrentVehicle = vehicle;
        }
    }
    public static class Game { public static Ped PlayerPed = new Ped(); }
    public class BaseScript
    {
        public static Task Delay(int milliseconds)
        {
            Native.API.Now = unchecked(Native.API.Now + (uint)(milliseconds == 0 ? 100 : milliseconds));
            return Task.CompletedTask;
        }
    }
}

namespace CitizenFX.Core.Native
{
    public class Surface
    {
        public float Z, NormalZ;
        public int Flags;
        public Surface(float z, float normalZ = 1, int flags = 1) { Z = z; NormalZ = normalZ; Flags = flags; }
    }
    public static class API
    {
        public static uint Now, CollisionReadyAt;
        public static int ProbeStarts, Requests, LastFlags, LastIgnored, PendingPolls, PlaceCalls, SeatCalls, ModelClass, ModsApplied;
        static int polls, nextHandle;
        public static float LastRayTop, LastRayBottom, MinimumZ;
        public static bool InvalidRay, PlacementBeforeSeating, HeliBlades, ModelReleased, IdentityClaimed;
        public static Surface ForcedHit;
        public static Vector3 PlayerPosition, SeatedAt;
        public static List<Surface> Surfaces = new List<Surface>();
        public static Dictionary<int, Vector3> Positions = new Dictionary<int, Vector3>();
        public static Dictionary<int, Vector3> Rotations = new Dictionary<int, Vector3>();
        public static Func<Vehicle, bool> Snap;
        public static void ResetWorld(float playerZ)
        {
            Now = 0; CollisionReadyAt = 0; ProbeStarts = Requests = LastFlags = LastIgnored = PendingPolls = PlaceCalls = SeatCalls = ModelClass = ModsApplied = polls = 0;
            MinimumZ = -0.5f; InvalidRay = HeliBlades = ModelReleased = IdentityClaimed = false; PlacementBeforeSeating = true;
            nextHandle = 100; ForcedHit = null; Surfaces.Clear(); Positions.Clear(); Rotations.Clear();
            PlayerPosition = new Vector3(100, 200, playerZ); SeatedAt = Vector3.Zero; Game.PlayerPed = new Ped();
            Snap = v => true;
        }
        public static int GetGameTimer() => unchecked((int)Now);
        public static void RequestCollisionAtCoord(float x, float y, float z) { Requests++; }
        public static int StartShapeTestRay(float x1, float y1, float z1, float x2, float y2, float z2, int flags, int ignore, int options)
        {
            ProbeStarts++; LastRayTop = z1; LastRayBottom = z2; LastFlags = flags; LastIgnored = ignore; polls = 0;
            return ProbeStarts;
        }
        public static int GetShapeTestResult(int handle, ref bool hit, ref Vector3 position, ref Vector3 normal, ref int entity)
        {
            if (InvalidRay) return 0;
            if (polls++ < PendingPolls) return 1;
            if (Now < CollisionReadyAt) return 2;
            var floor = ForcedHit ?? Surfaces.Where(s => (s.Flags & LastFlags) != 0 && s.Z <= LastRayTop && s.Z >= LastRayBottom)
                .OrderByDescending(s => s.Z).FirstOrDefault();
            if (floor != null) { hit = true; position = new Vector3(100, 200, floor.Z); normal = new Vector3(0, 0, floor.NormalZ); }
            return 2;
        }
        public static void GetModelDimensions(uint hash, ref Vector3 min, ref Vector3 max)
        { min = new Vector3(-1, -2, MinimumZ); max = new Vector3(1, 2, 1); }
        public static int CreateVehicle(uint hash, float x, float y, float z, float heading, bool networked, bool mission)
        { var handle = nextHandle++; Positions[handle] = new Vector3(x, y, z); Rotations[handle] = new Vector3(0, 0, heading); return handle; }
        public static Vector3 GetEntityCoords(int handle, bool alive) => handle == 1
            ? (Game.PlayerPed.IsInVehicle() ? Positions[Game.PlayerPed.CurrentVehicle.Handle] : PlayerPosition) : Positions[handle];
        public static void SetEntityCoordsNoOffset(int handle, float x, float y, float z, bool tasks, bool ik, bool warp)
        { Positions[handle] = new Vector3(x, y, z); }
        public static Vector3 GetEntityRotation(int handle, int order) => Rotations[handle];
        public static void SetEntityRotation(int handle, float x, float y, float z, int order, bool physics) { Rotations[handle] = new Vector3(x, y, z); }
        public static Vector3 GetOffsetFromEntityInWorldCoords(int handle, float x, float y, float z) => GetEntityCoords(handle, true) + new Vector3(x, y, z);
        public static float GetEntityHeading(int handle) => 90;
        public static Vector3 GetEntitySpeedVector(int handle, bool relative) => Vector3.Zero;
        public static int GetVehicleClassFromName(uint hash) => ModelClass;
        public static bool IsModelAVehicle(uint hash) => true;
        public static bool IsModelInCdimage(uint hash) => true;
        public static void SetEntityAsMissionEntity(int handle, bool mission, bool grab) { }
        public static void SetVehicleHasBeenOwnedByPlayer(int handle, bool owned) { }
        public static float GetEntityHeightAboveGround(int handle) => PlayerPosition.Z;
        public static void SetHeliBladesFullSpeed(int handle) { HeliBlades = true; }
        public static void SetVehicleForwardSpeed(int handle, float speed) { }
        public static void SetModelAsNoLongerNeeded(uint hash) { ModelReleased = true; }
    }
}

namespace vMenuShared
{
    public static class ConfigManager
    {
        public enum Setting { vmenu_keep_spawned_vehicles_persistent }
        public static bool GetSettingsBool(Setting setting) => true;
    }
}
