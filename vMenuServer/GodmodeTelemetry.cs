using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;

namespace vMenuServer
{
    /// <summary>
    /// Converts vMenu's client option heartbeat into a narrow, renewable
    /// psrp_telemetry invincibility-state lease. The client cannot grant the
    /// lease itself: this server validates vMenu entitlement, heartbeat age,
    /// and the OneSync invincibility bit on every renewal.
    /// </summary>
    public sealed class GodmodeTelemetry : BaseScript
    {
        private const int RenewalIntervalMs = 1000;
        private const int LeaseSeconds = 4;
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(6);

        private readonly Dictionary<string, DateTime> requested = new();

        [EventHandler("vMenu:PSRP:GodmodeState")]
        internal void OnGodmodeState([FromSource] Player source, bool active)
        {
            if (source == null)
            {
                return;
            }

            if (!active)
            {
                requested.Remove(source.Handle);
                return;
            }

            if (HasGodmodePermission(source.Handle))
            {
                requested[source.Handle] = DateTime.UtcNow;
            }
            else
            {
                requested.Remove(source.Handle);
            }
        }

        [EventHandler("playerDropped")]
        internal void OnPlayerDropped([FromSource] Player source, string reason)
        {
            if (source != null)
            {
                requested.Remove(source.Handle);
            }
        }

        [Tick]
        internal async Task RenewValidatedLeases()
        {
            await Delay(RenewalIntervalMs);

            var now = DateTime.UtcNow;
            foreach (var entry in requested.ToList())
            {
                var source = Players[entry.Key];
                var valid = source != null
                    && now - entry.Value <= HeartbeatTimeout
                    && HasGodmodePermission(entry.Key)
                    && GetPlayerInvincible(entry.Key);

                if (!valid)
                {
                    requested.Remove(entry.Key);
                    continue;
                }

                if (GetResourceState("psrp_telemetry") != "started")
                {
                    continue;
                }

                try
                {
                    Exports["psrp_telemetry"].GrantInvincibilityGrace(
                        int.Parse(entry.Key), LeaseSeconds, "vMenu god mode");
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"^3[vMenu] Could not renew psrp_telemetry god-mode lease: {exception.Message}^7");
                }
            }
        }

        private static bool HasGodmodePermission(string source)
        {
            return IsPlayerAceAllowed(source, "vMenu.PlayerOptions.God")
                || IsPlayerAceAllowed(source, "vMenu.PlayerOptions.All")
                || IsPlayerAceAllowed(source, "vMenu.Everything");
        }
    }
}
