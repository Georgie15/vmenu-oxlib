using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;

namespace vMenuServer
{
    /// <summary>
    /// Converts vMenu's client heartbeats (the god mode option and noclip) into
    /// narrow, renewable psrp_telemetry invincibility-state leases. The client
    /// cannot grant a lease itself: this server validates vMenu entitlement,
    /// heartbeat age, and the OneSync invincibility bit on every renewal.
    /// Noclip in a vehicle makes the vehicle invincible, not the player, so the
    /// bit check fails and no noclip lease is issued there.
    /// </summary>
    public sealed class GodmodeTelemetry : BaseScript
    {
        private const int RenewalIntervalMs = 1000;
        private const int LeaseSeconds = 4;
        private const string GodmodeReason = "vMenu god mode";
        private const string NoclipReason = "vMenu noclip";
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(6);

        /// <summary>
        /// When each mode's last active heartbeat arrived; null when not requested.
        /// </summary>
        private sealed class Heartbeats
        {
            public DateTime? Godmode;
            public DateTime? Noclip;
        }

        private readonly Dictionary<string, Heartbeats> requested = new();

        [EventHandler("vMenu:PSRP:GodmodeState")]
        internal void OnGodmodeState([FromSource] Player source, bool active)
        {
            if (source == null)
            {
                return;
            }

            var beats = Entry(source.Handle);
            beats.Godmode = active && HasGodmodePermission(source.Handle) ? DateTime.UtcNow : null;
            ForgetIfIdle(source.Handle, beats);
        }

        [EventHandler("vMenu:PSRP:NoclipState")]
        internal void OnNoclipState([FromSource] Player source, bool active)
        {
            if (source == null)
            {
                return;
            }

            var beats = Entry(source.Handle);
            beats.Noclip = active && HasNoclipPermission(source.Handle) ? DateTime.UtcNow : null;
            ForgetIfIdle(source.Handle, beats);
        }

        private Heartbeats Entry(string handle)
        {
            if (!requested.TryGetValue(handle, out var beats))
            {
                beats = new Heartbeats();
                requested[handle] = beats;
            }
            return beats;
        }

        // A player with neither mode requested costs the renewal tick nothing.
        private void ForgetIfIdle(string handle, Heartbeats beats)
        {
            if (beats.Godmode == null && beats.Noclip == null)
            {
                requested.Remove(handle);
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

            if (requested.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var telemetryStarted = GetResourceState("psrp_telemetry") == "started";
            foreach (var entry in requested.ToList())
            {
                var handle = entry.Key;
                var beats = entry.Value;
                var invincible = Players[handle] != null && GetPlayerInvincible(handle);

                var godmodeValid = invincible
                    && IsFresh(beats.Godmode, now)
                    && HasGodmodePermission(handle);
                var noclipValid = invincible
                    && IsFresh(beats.Noclip, now)
                    && HasNoclipPermission(handle);

                if (!godmodeValid)
                {
                    beats.Godmode = null;
                }
                if (!noclipValid)
                {
                    beats.Noclip = null;
                }
                if (!godmodeValid && !noclipValid)
                {
                    requested.Remove(handle);
                    continue;
                }

                if (!telemetryStarted)
                {
                    continue;
                }

                if (godmodeValid)
                {
                    GrantLease(handle, GodmodeReason);
                }
                if (noclipValid)
                {
                    GrantLease(handle, NoclipReason);
                }
            }
        }

        private static bool IsFresh(DateTime? heartbeat, DateTime now)
        {
            return heartbeat.HasValue && now - heartbeat.Value <= HeartbeatTimeout;
        }

        private void GrantLease(string handle, string reason)
        {
            try
            {
                Exports["psrp_telemetry"].GrantInvincibilityGrace(int.Parse(handle), LeaseSeconds, reason);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"^3[vMenu] Could not renew psrp_telemetry {reason} lease: {exception.Message}^7");
            }
        }

        private static bool HasGodmodePermission(string source)
        {
            return IsPlayerAceAllowed(source, "vMenu.PlayerOptions.God")
                || IsPlayerAceAllowed(source, "vMenu.PlayerOptions.All")
                || IsPlayerAceAllowed(source, "vMenu.Everything");
        }

        // vMenu.NoClip is a global permission: its only parent is Everything
        // (PermissionsManager.GetPermissionAndParentPermissions).
        private static bool HasNoclipPermission(string source)
        {
            return IsPlayerAceAllowed(source, "vMenu.NoClip")
                || IsPlayerAceAllowed(source, "vMenu.Everything");
        }
    }
}
