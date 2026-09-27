using System;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;

namespace vMenuClient
{
    public class LeoSpawncodeBridge : BaseScript
    {
        public LeoSpawncodeBridge()
        {
            EventHandlers["vMenu:ApplyAllVehicleExtras"] += new Action<int>(ApplyAllVehicleExtras);
        }

        private static void ApplyAllVehicleExtras(int vehicleHandle)
        {
            if (vehicleHandle == 0 || !DoesEntityExist(vehicleHandle) || !IsEntityAVehicle(vehicleHandle))
            {
                return;
            }

            for (var extra = 0; extra < 20; extra++)
            {
                if (DoesExtraExist(vehicleHandle, extra))
                {
                    SetVehicleExtra(vehicleHandle, extra, false);
                }
            }

            SetVehicleSiren(vehicleHandle, true);
        }
    }
}
