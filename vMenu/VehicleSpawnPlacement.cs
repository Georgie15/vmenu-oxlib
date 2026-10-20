using System;
using System.Threading.Tasks;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;

namespace vMenuClient
{
    internal static class VehicleSpawnPlacement
    {
        private const uint CollisionTimeoutMilliseconds = 1000;
        private const float ProbeAbove = 0.25f;
        private const float ProbeBelow = 3f;
        private const float PlacementTolerance = 0.75f;

        internal static async Task<Vector3?> FindGroundedPositionAsync(uint model, Vector3 position, int playerPed)
        {
            // Trace only nearby world/object collision, never vehicles, peds or water.
            // Starting near the requested height prevents a roof/upper floor from
            // becoming the ground in an underground or multi-storey garage.
            var startedAt = unchecked((uint)GetGameTimer());
            var ray = 0;
            while (unchecked((uint)GetGameTimer() - startedAt) < CollisionTimeoutMilliseconds)
            {
                RequestCollisionAtCoord(position.X, position.Y, position.Z);
                if (ray == 0)
                {
                    ray = StartShapeTestRay(position.X, position.Y, position.Z + ProbeAbove,
                        position.X, position.Y, position.Z - ProbeBelow, 1 | 16, playerPed, 7);
                }

                var hit = false;
                var floor = Vector3.Zero;
                var normal = Vector3.Zero;
                var entity = 0;
                var state = GetShapeTestResult(ray, ref hit, ref floor, ref normal, ref entity);
                if (state == 2 && hit && normal.Z >= 0.5f
                    && floor.Z <= position.Z + ProbeAbove && floor.Z >= position.Z - ProbeBelow)
                {
                    var min = Vector3.Zero;
                    var max = Vector3.Zero;
                    GetModelDimensions(model, ref min, ref max);
                    // Leave the model's lowest point just above the local floor.
                    return new Vector3(position.X, position.Y, floor.Z - min.Z + 0.05f);
                }

                if (state != 1)
                {
                    ray = 0;
                }
                await BaseScript.Delay(0);
            }

            // Collision may be missing, or this may be an airborne spawn. Do not
            // fall back to a global ground search that could select another floor.
            return null;
        }

        internal static void PlaceOnFloor(Vehicle vehicle, Vector3 position)
        {
            SetEntityCoordsNoOffset(vehicle.Handle, position.X, position.Y, position.Z, false, false, true);
            var rotation = GetEntityRotation(vehicle.Handle, 2);

            // Retain GTA's wheel/slope alignment only when it stays on the floor
            // we actually traced. Restore before seating the player, in this frame.
            var placed = vehicle.PlaceOnGround();
            var actual = GetEntityCoords(vehicle.Handle, true);
            if (!placed || Math.Abs(actual.Z - position.Z) > PlacementTolerance
                || Math.Abs(actual.X - position.X) > PlacementTolerance
                || Math.Abs(actual.Y - position.Y) > PlacementTolerance)
            {
                SetEntityCoordsNoOffset(vehicle.Handle, position.X, position.Y, position.Z, false, false, true);
                SetEntityRotation(vehicle.Handle, rotation.X, rotation.Y, rotation.Z, 2, true);
            }
        }
    }
}
