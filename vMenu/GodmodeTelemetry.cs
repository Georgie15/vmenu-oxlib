using System.Threading.Tasks;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;
using static vMenuShared.PermissionsManager;

namespace vMenuClient
{
    /// <summary>
    /// Publishes the state of vMenu's own player-godmode option and of vMenu
    /// noclip. These are only heartbeats: the vMenu server independently
    /// verifies the exact ACE and the synchronized invincibility bit before
    /// telemetry receives any grace. One tick and one poll serve both.
    /// </summary>
    public sealed class GodmodeTelemetry : BaseScript
    {
        private const int PollIntervalMs = 500;
        private const int HeartbeatIntervalMs = 2000;

        private readonly Heartbeat godmode = new("vMenu:PSRP:GodmodeState");
        private readonly Heartbeat noclip = new("vMenu:PSRP:NoclipState");

        [Tick]
        internal async Task PublishGodmodeState()
        {
            await Delay(PollIntervalMs);

            var godmodeActive = MainMenu.PermissionsSetupComplete
                && MainMenu.PlayerOptionsMenu != null
                && MainMenu.PlayerOptionsMenu.PlayerGodMode
                && IsAllowed(Permission.POGod);
            // Noclip sets the ped invincible every frame while on foot (Noclip.cs).
            var noclipActive = MainMenu.PermissionsSetupComplete
                && NoClip.IsNoclipActive()
                && IsAllowed(Permission.NoClip);
            var now = GetGameTimer();

            godmode.Publish(godmodeActive, now);
            noclip.Publish(noclipActive, now);
        }

        /// <summary>
        /// Sends a state on its first poll and on every change, and repeats an
        /// active state every <see cref="HeartbeatIntervalMs"/>.
        /// </summary>
        private sealed class Heartbeat
        {
            private readonly string eventName;
            private bool hasReported;
            private bool lastReportedState;
            private int lastReportedAt;

            internal Heartbeat(string eventName)
            {
                this.eventName = eventName;
            }

            internal void Publish(bool active, int now)
            {
                var heartbeatDue = active && unchecked(now - lastReportedAt) >= HeartbeatIntervalMs;

                if (!hasReported || active != lastReportedState || heartbeatDue)
                {
                    TriggerServerEvent(eventName, active);
                    hasReported = true;
                    lastReportedState = active;
                    lastReportedAt = now;
                }
            }
        }
    }
}
