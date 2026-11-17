using System;
using System.Collections.Generic;
using System.Globalization;

// The parts of the request guard that don't touch FiveM: rate limits, argument checks and event
// sources. tests/request-guard compiles this file on its own. The FiveM side is RequestGuard.cs.
namespace vMenuServer
{
    /// <summary>
    /// A token bucket per player for one kind of client request: a player can send
    /// <c>burst</c> requests at once, then <c>perSecond</c> a second. Refusals are handed back
    /// for logging at most once a second per player, with how many there were, so a flood
    /// costs one log entry a second instead of one per request.
    /// </summary>
    internal sealed class RequestRateLimiter
    {
        internal const long ReportIntervalMs = 1000;

        private sealed class Bucket
        {
            public double Tokens;
            public long Stamp;
            public int Refused;
            public long ReportedAt;
        }

        private readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>();

        internal int Tracked => buckets.Count;

        /// <summary>
        /// Takes one request from the player's bucket and returns whether it may run. Limits of 0
        /// or less mean no limit. <paramref name="refusalsToReport"/> is how many refused requests
        /// to log now (0 = none): the first refusal at once, then the ones since, with the player's
        /// next request at least a second later.
        /// </summary>
        public bool TryTake(string player, long nowMs, int burst, double perSecond, out int refusalsToReport)
        {
            refusalsToReport = 0;
            if (burst <= 0 || perSecond <= 0)
            {
                return true;
            }

            if (!buckets.TryGetValue(player, out var bucket))
            {
                buckets[player] = new Bucket { Tokens = burst - 1, Stamp = nowMs, ReportedAt = nowMs - ReportIntervalMs };
                return true;
            }

            var elapsed = Math.Max(0, nowMs - bucket.Stamp);
            bucket.Tokens = Math.Min(burst, bucket.Tokens + elapsed * perSecond / 1000.0);
            bucket.Stamp = Math.Max(bucket.Stamp, nowMs);

            var allowed = bucket.Tokens >= 1;
            if (allowed)
            {
                bucket.Tokens -= 1;
            }
            else
            {
                bucket.Refused++;
            }

            if (bucket.Refused > 0 && nowMs - bucket.ReportedAt >= ReportIntervalMs)
            {
                refusalsToReport = bucket.Refused;
                bucket.Refused = 0;
                bucket.ReportedAt = nowMs;
            }
            return allowed;
        }

        public void Forget(string player)
        {
            buckets.Remove(player);
        }
    }

    /// <summary>
    /// Type checks for arguments a client sends. Guarded handlers take them as <c>object</c>: when
    /// the runtime can't convert an argument to a typed parameter it throws while binding, and it
    /// removes a handler that throws, for every player, until vMenu restarts.
    /// </summary>
    internal static class RequestPayload
    {
        /// <summary>A whole number in int range: any integer type, or a whole float/double.</summary>
        public static bool TryInt(object value, out int result)
        {
            result = 0;
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case long l when l >= int.MinValue && l <= int.MaxValue:
                    result = (int)l;
                    return true;
                case uint u when u <= int.MaxValue:
                    result = (int)u;
                    return true;
                case ulong ul when ul <= int.MaxValue:
                    result = (int)ul;
                    return true;
                case short s:
                    result = s;
                    return true;
                case ushort us:
                    result = us;
                    return true;
                case byte b:
                    result = b;
                    return true;
                case sbyte sb:
                    result = sb;
                    return true;
                case double d when d >= int.MinValue && d <= int.MaxValue && Math.Floor(d) == d:
                    result = (int)d;
                    return true;
                case float f when f >= int.MinValue && f <= int.MaxValue && Math.Floor(f) == f:
                    result = (int)f;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryBool(object value, out bool result)
        {
            if (value is bool b)
            {
                result = b;
                return true;
            }
            result = false;
            return false;
        }

        public static bool TryString(object value, int maxLength, out string result)
        {
            result = value as string;
            return result != null && result.Length <= maxLength;
        }
    }

    /// <summary>
    /// Event sources as the server runtime passes them to <c>[FromSource] string</c>: a client's
    /// TriggerServerEvent is "net:&lt;id&gt;", the server's own player events (playerJoining,
    /// playerDropped) are "internal-net:&lt;id&gt;", and TriggerEvent from a resource on this
    /// server has no source (null).
    /// </summary>
    internal static class RequestSource
    {
        public static bool IsClient(string source)
        {
            return source != null && source.StartsWith("net:", StringComparison.Ordinal);
        }

        public static bool IsServerPlayerEvent(string source)
        {
            return source != null && source.StartsWith("internal-net:", StringComparison.Ordinal);
        }

        /// <summary>The player's server id in a "net:" or "internal-net:" source, or null.</summary>
        public static string PlayerHandle(string source)
        {
            if (!IsClient(source) && !IsServerPlayerEvent(source))
            {
                return null;
            }
            var handle = source.Substring(source.IndexOf(':') + 1);
            return IsPlayerHandle(handle) ? handle : null;
        }

        /// <summary>A real player's server id: a whole number above 0.</summary>
        public static bool IsPlayerHandle(string handle)
        {
            return handle != null
                && int.TryParse(handle, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id > 0;
        }
    }
}
