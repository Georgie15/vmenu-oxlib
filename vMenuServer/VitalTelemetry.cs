using System;
using System.Collections.Generic;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;

namespace vMenuServer
{
    public sealed class VitalTelemetry : BaseScript
    {
        private readonly Dictionary<string, DateTime> lastVital = new();
        private DateTime lastWarning = DateTime.MinValue;
        private static bool Allowed(string source, string option)
        {
            return IsPlayerAceAllowed(source, "vMenu.PlayerOptions." + option)
                || IsPlayerAceAllowed(source, "vMenu.PlayerOptions.All")
                || IsPlayerAceAllowed(source, "vMenu.Everything");
        }
        // Recurring stamina leases run in server/staminaTelemetry.lua. Repeated
        // dynamic C# -> Lua export resolution dominated the supplied profiler.
        [EventHandler("vMenu:PSRP:RequestVital")]
        internal void RequestVital([FromSource] Player source, object kindArg, object targetArg)
        {
            var request = RequestGuard.RequestVital;
            if (!RequestGuard.Admit(source, request)) return;
            if (!RequestPayload.TryString(kindArg, 16, out var kind) || !RequestPayload.TryInt(targetArg, out var target))
            {
                RequestGuard.Malformed(source, request, "expected a vital name and a whole number");
                return;
            }
            if (kind == "health")
            {
                if (target != 200)
                {
                    RequestGuard.Malformed(source, request, "health can only be set to 200");
                    return;
                }
                if (!Allowed(source.Handle, "MaxHealth"))
                {
                    RequestGuard.Denied(source, request, "vMenu.PlayerOptions.MaxHealth");
                    return;
                }
            }
            else if (kind == "armour")
            {
                if (target < 0 || target > 100 || target % 20 != 0)
                {
                    RequestGuard.Malformed(source, request, "armour must be 0-100 in steps of 20");
                    return;
                }
                if (!Allowed(source.Handle, "MaxArmor"))
                {
                    RequestGuard.Denied(source, request, "vMenu.PlayerOptions.MaxArmor");
                    return;
                }
            }
            else
            {
                RequestGuard.Malformed(source, request, "unknown vital");
                return;
            }
            try
            {
                var now = DateTime.UtcNow;
                if (lastVital.TryGetValue(source.Handle, out var last)
                    && now - last < TimeSpan.FromSeconds(1)) return;
                var ped = GetPlayerPed(source.Handle);
                if (ped == 0 || !DoesEntityExist(ped)) return;
                lastVital[source.Handle] = now;
                Telemetry(() => Exports["psrp_telemetry"].AuthorizeVitalChange(
                    int.Parse(source.Handle), kind, target, 5, "validated vMenu player option"));
                source.TriggerEvent("vMenu:PSRP:ApplyVital", kind, target);
            }
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }
        // Only the server's own event ("internal-net:<id>"); MainServer logs a client's copy.
        [EventHandler("playerDropped")]
        internal void Drop([FromSource] string source)
        {
            if (!RequestSource.IsServerPlayerEvent(source)) return;
            var handle = RequestSource.PlayerHandle(source);
            if (handle != null) lastVital.Remove(handle);
        }
        private void Telemetry(Action action)
        {
            if (GetResourceState("psrp_telemetry") != "started") return;
            try { action(); }
            catch (Exception exception)
            {
                if (DateTime.UtcNow - lastWarning < TimeSpan.FromSeconds(60)) return;
                lastWarning = DateTime.UtcNow;
                Debug.WriteLine("^3[vMenu] Vital telemetry integration unavailable: " + exception.Message + "^7");
            }
        }
    }
}
