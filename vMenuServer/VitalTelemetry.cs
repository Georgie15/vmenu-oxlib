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
        internal void RequestVital([FromSource] Player source, string kind, int target)
        {
            if (source == null) return;
            if (kind == "health")
            {
                if (target != 200 || !Allowed(source.Handle, "MaxHealth")) return;
            }
            else if (kind == "armour")
            {
                if (target < 0 || target > 100 || target % 20 != 0
                    || !Allowed(source.Handle, "MaxArmor")) return;
            }
            else return;
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
        [EventHandler("playerDropped")]
        internal void Drop([FromSource] Player source, string reason)
        {
            if (source == null) return;
            lastVital.Remove(source.Handle);
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
