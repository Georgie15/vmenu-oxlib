using System.Threading.Tasks;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;
using static vMenuShared.PermissionsManager;

namespace vMenuClient
{
    /// <summary>
    /// Publishes the state of vMenu's own player-godmode option. This is only a
    /// heartbeat: the vMenu server independently verifies the exact ACE and the
    /// synchronized invincibility bit before telemetry receives any grace.
    /// </summary>
    public sealed class GodmodeTelemetry : BaseScript
    {
        private const int PollIntervalMs = 500;
        private const int HeartbeatIntervalMs = 2000;

        private bool hasReported;
        private bool lastReportedState;
        private int lastReportedAt;

        [Tick]
        internal async Task PublishGodmodeState()
        {
            await Delay(PollIntervalMs);

            var active = MainMenu.PermissionsSetupComplete
                && MainMenu.PlayerOptionsMenu != null
                && MainMenu.PlayerOptionsMenu.PlayerGodMode
                && IsAllowed(Permission.POGod);
            var now = GetGameTimer();
            var heartbeatDue = active && unchecked(now - lastReportedAt) >= HeartbeatIntervalMs;

            if (!hasReported || active != lastReportedState || heartbeatDue)
            {
                TriggerServerEvent("vMenu:PSRP:GodmodeState", active);
                hasReported = true;
                lastReportedState = active;
                lastReportedAt = now;
            }
        }
    }
}
