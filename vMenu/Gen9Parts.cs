using System;

using CitizenFX.Core;
using CitizenFX.Core.Native;

using static CitizenFX.Core.Native.API;

namespace vMenuClient
{
    // Asks the game which clothing pieces and vehicle upgrades only GTA V Enhanced (gen9) can
    // use. See Gen9Skip for why vMenu leaves them out.
    internal static class Gen9Parts
    {
        // IS_VEHICLE_MOD_GEN9_EXCLUSIVE. CitizenFX.Core.Client 1.0.10188 has no wrapper for it.
        private const Hash IsVehicleModGen9ExclusiveHash = (Hash)0x00834EAC4A96E010;

        private static bool vehicleCheckMissing;

        // A FiveM native that exists on every game build; it answers false below 2699.
        public static bool IsClothing(int ped, int component, int drawable) =>
            drawable >= 0 && IsPedComponentVariationGen9Exclusive(ped, component, drawable);

        public static bool IsVehicleMod(int vehicle, int modType, int modIndex)
        {
            if (vehicleCheckMissing || modIndex < 0)
            {
                return false;
            }

            try
            {
                return Function.Call<bool>(IsVehicleModGen9ExclusiveHash, vehicle, modType, modIndex);
            }
            catch (Exception e)
            {
                // Game builds older than the native don't have it, and have no gen9 upgrades either.
                vehicleCheckMissing = true;
                Debug.WriteLine($"[vMenu] Can't check for GTA V Enhanced only upgrades on this game build: {e.Message}");
                return false;
            }
        }
    }
}
