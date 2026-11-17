using System;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;

namespace vMenuServer
{
    /// <summary>One kind of client request (one event) and its per-player rate limit.</summary>
    internal sealed class GuardedRequest
    {
        public readonly string EventName;

        /// <summary>Uses the permission bootstrap's limits instead of the default ones.</summary>
        internal readonly bool PermissionBootstrap;

        internal readonly RequestRateLimiter Limiter = new RequestRateLimiter();

        internal GuardedRequest(string eventName, bool permissionBootstrap = false)
        {
            EventName = eventName;
            PermissionBootstrap = permissionBootstrap;
        }
    }

    /// <summary>
    /// The server-side guard for every request a client sends to vMenu's C# handlers: it drops
    /// requests with no real player, rate-limits each player per event, and logs denied,
    /// rate-limited, malformed, sourceless and spoofed requests. Handlers keep their own
    /// permission checks and report a refusal with <see cref="Denied"/>.
    ///
    /// The Lua handlers use server/guard/requestGuard.lua, which reads the same convars. That file
    /// also writes the security log (console line and optional Discord webhook); this class hands
    /// each entry to it with a local event, so both sides log the same way.
    /// </summary>
    internal static class RequestGuard
    {
        public static readonly GuardedRequest RequestPermissions = new GuardedRequest("vMenu:RequestPermissions", permissionBootstrap: true);
        public static readonly GuardedRequest SetPersonalVehicle = new GuardedRequest("vMenu:SetPersonalVehicle");
        public static readonly GuardedRequest GetOutOfCar = new GuardedRequest("vMenu:GetOutOfCar");
        public static readonly GuardedRequest ClearArea = new GuardedRequest("vMenu:ClearArea");
        public static readonly GuardedRequest UpdateServerWeather = new GuardedRequest("vMenu:UpdateServerWeather");
        public static readonly GuardedRequest UpdateServerBlackout = new GuardedRequest("vMenu:UpdateServerBlackout");
        public static readonly GuardedRequest UpdateServerVehicleBlackout = new GuardedRequest("vMenu:UpdateServerVehicleBlackout");
        public static readonly GuardedRequest UpdateServerWeatherCloudsType = new GuardedRequest("vMenu:UpdateServerWeatherCloudsType");
        public static readonly GuardedRequest UpdateServerTime = new GuardedRequest("vMenu:UpdateServerTime");
        public static readonly GuardedRequest SaveTeleportLocation = new GuardedRequest("vMenu:SaveTeleportLocation");
        public static readonly GuardedRequest GodmodeState = new GuardedRequest("vMenu:PSRP:GodmodeState");
        public static readonly GuardedRequest NoclipState = new GuardedRequest("vMenu:PSRP:NoclipState");
        public static readonly GuardedRequest RequestVital = new GuardedRequest("vMenu:PSRP:RequestVital");

        private static readonly GuardedRequest[] All =
        {
            RequestPermissions, SetPersonalVehicle, GetOutOfCar, ClearArea, UpdateServerWeather, UpdateServerBlackout,
            UpdateServerVehicleBlackout, UpdateServerWeatherCloudsType, UpdateServerTime, SaveTeleportLocation,
            GodmodeState, NoclipState, RequestVital,
        };

        // Handled by server/guard/requestGuard.lua (AddEventHandler only, so clients can't send it).
        private const string ReportEvent = "vMenu:security:report";

        // Convars are read at most every 5 s, not on every request.
        private const long SettingsTtlMs = 5000;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static long settingsReadAt = -SettingsTtlMs;
        private static bool rateLimits = true;
        private static int burst = 10;
        private static int perSecond = 3;
        private static int permissionBurst = 3;
        private static int permissionPerMinute = 3;

        /// <summary>
        /// Checks a client request before its handler runs. False (and logged) when it has to be
        /// dropped: no real player sent it, or the player is over this event's rate limit.
        /// </summary>
        public static bool Admit(Player source, GuardedRequest request)
        {
            var handle = source?.Handle;
            if (!RequestSource.IsPlayerHandle(handle))
            {
                Report("no-source", request.EventName, null, "no player sent this request", 1);
                return false;
            }

            var now = Clock.ElapsedMilliseconds;
            RefreshSettings(now);
            if (!rateLimits)
            {
                return true;
            }

            int limit;
            double rate;
            string describe;
            if (request.PermissionBootstrap)
            {
                limit = permissionBurst;
                rate = permissionPerMinute / 60.0;
                describe = $"limit {permissionBurst} at once, {permissionPerMinute} a minute";
            }
            else
            {
                limit = burst;
                rate = perSecond;
                describe = $"limit {burst} at once, {perSecond} a second";
            }

            var allowed = request.Limiter.TryTake(handle, now, limit, rate, out var refused);
            if (refused > 0)
            {
                Report("rate-limited", request.EventName, handle, describe, refused);
            }
            return allowed;
        }

        /// <summary>Logs a request the handler's own permission check refused.</summary>
        public static void Denied(Player source, GuardedRequest request, string permission)
        {
            Report("denied", request.EventName, source?.Handle, "missing " + permission, 1);
        }

        /// <summary>Logs a request whose arguments failed the handler's type or size checks.</summary>
        public static void Malformed(Player source, GuardedRequest request, string what)
        {
            Report("malformed", request.EventName, source?.Handle, what, 1);
        }

        /// <summary>
        /// A guarded handler threw. It is caught so the runtime doesn't remove the handler for
        /// everyone; this is an error, not a security entry.
        /// </summary>
        public static void Failed(Player source, GuardedRequest request, Exception exception)
        {
            Debug.WriteLine($"^1[vMenu] [ERROR]^7 {request.EventName} from {source?.Handle ?? "-"} failed: {exception}");
        }

        /// <summary>
        /// For the server's own player events (playerJoining, playerDropped): true only for the
        /// server's event. A copy a client sent is logged and ignored.
        /// </summary>
        public static bool IsServerPlayerEvent(string source, string eventName)
        {
            if (RequestSource.IsServerPlayerEvent(source))
            {
                return true;
            }
            if (RequestSource.IsClient(source))
            {
                Report("spoofed", eventName, RequestSource.PlayerHandle(source), "a client sent the server's own player event; ignored", 1);
            }
            return false;
        }

        /// <summary>Forgets a player's rate limits when they leave.</summary>
        public static void Forget(string handle)
        {
            if (handle == null)
            {
                return;
            }
            foreach (var request in All)
            {
                request.Limiter.Forget(handle);
            }
        }

        private static void RefreshSettings(long now)
        {
            if (now - settingsReadAt < SettingsTtlMs)
            {
                return;
            }
            settingsReadAt = now;
            rateLimits = GetConvar("vmenu_guard_rate_limits", "true").Trim().ToLowerInvariant() != "false";
            burst = GetConvarInt("vmenu_guard_burst", 10);
            perSecond = GetConvarInt("vmenu_guard_per_second", 3);
            permissionBurst = GetConvarInt("vmenu_guard_permissions_burst", 3);
            permissionPerMinute = GetConvarInt("vmenu_guard_permissions_per_minute", 3);
        }

        private static void Report(string kind, string eventName, string handle, string detail, int count)
        {
            var serverId = 0;
            if (RequestSource.IsPlayerHandle(handle))
            {
                serverId = int.Parse(handle);
            }
            try
            {
                BaseScript.TriggerEvent(ReportEvent, kind, eventName, serverId, detail, count);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"^3[vMenu] [SECURITY]^7 {kind} {eventName} from {serverId} ({detail}); the security log is unavailable: {exception.Message}");
            }
        }
    }
}
