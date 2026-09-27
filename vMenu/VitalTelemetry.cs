using System.Threading.Tasks;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;
using static vMenuShared.PermissionsManager;

namespace vMenuClient
{
    public sealed class VitalTelemetry : BaseScript
    {
        private bool? lastStaminaMode;

        [Tick]
        internal async Task PublishStaminaMode()
        {
            await Delay(2000);
            var active = MainMenu.PermissionsSetupComplete
                && MainMenu.PlayerOptionsMenu != null
                && MainMenu.PlayerOptionsMenu.PlayerStamina
                && IsAllowed(Permission.POUnlimitedStamina);
            // Renew active modes, but report an inactive mode only at startup
            // or on a transition. Idle players do not need a network heartbeat.
            if (!lastStaminaMode.HasValue || active || lastStaminaMode.Value != active)
                TriggerServerEvent("vMenu:PSRP:StaminaMode", active);
            lastStaminaMode = active;
        }

        // Menu actions wait for server authorization before changing the ped.
        public static void RequestVital(string kind, int target)
        {
            TriggerServerEvent("vMenu:PSRP:RequestVital", kind, target);
        }

        [EventHandler("vMenu:PSRP:ApplyVital")]
        internal void ApplyVital(string kind, int target)
        {
            if (kind == "health" && target == 200)
            {
                SetEntityHealth(PlayerPedId(), target);
                Notify.Success("Player healed.");
            }
            else if (kind == "armour" && target >= 0 && target <= 100)
            {
                SetPedArmour(PlayerPedId(), target);
            }
        }
    }
}
