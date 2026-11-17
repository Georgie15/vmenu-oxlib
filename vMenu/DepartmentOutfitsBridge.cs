using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CitizenFX.Core;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static CitizenFX.Core.Native.API;

namespace vMenuClient
{
    /// <summary>
    /// Sends Department Outfits requests to build/vMenu/server/deptOutfits.lua and waits for the
    /// answer. Each request carries a number; the server answers with the same number.
    /// </summary>
    public class DepartmentOutfitsBridge : BaseScript
    {
        private const int TimeoutMs = 15000;
        private const int PollMs = 50;

        /// <summary>
        /// deptOutfits.lua refuses a list within 300 ms, or a wear within 1 s, of the last one. These
        /// are sent by opening the menu or choosing an outfit, so they wait their turn instead.
        /// </summary>
        private static readonly Dictionary<string, int> SpacingMs = new() { ["list"] = 350, ["wear"] = 1050 };
        private static readonly Dictionary<string, int> lastSentAt = new();

        private static int lastRequest;
        private static readonly HashSet<int> waiting = new();
        private static readonly Dictionary<int, string> replies = new();

        public DepartmentOutfitsBridge()
        {
            EventHandlers[DepartmentOutfits.ReplyEvent] += new Action<int, string>(OnReply);
        }

        /// <summary>Department Outfits are switched on with <c>setr vmenu_dept_outfits true</c>.</summary>
        internal static bool Enabled => GetConvar(DepartmentOutfits.Convar, "false") == "true";

        private static void OnReply(int request, string json)
        {
            if (waiting.Contains(request))
            {
                replies[request] = json;
            }
        }

        internal static async Task<DepartmentOutfitReply> Request(string action, JObject args = null)
        {
            if (SpacingMs.TryGetValue(action, out var spacing) && lastSentAt.TryGetValue(action, out var sentAt))
            {
                var wait = sentAt + spacing - GetGameTimer();
                if (wait > 0)
                {
                    await Delay(Math.Min(wait, spacing));
                }
            }
            lastSentAt[action] = GetGameTimer();
            var request = ++lastRequest;
            waiting.Add(request);
            try
            {
                TriggerServerEvent(DepartmentOutfits.RequestEvent, request, action, (args ?? new JObject()).ToString(Formatting.None));
                var deadline = GetGameTimer() + TimeoutMs;
                while (!replies.ContainsKey(request) && GetGameTimer() < deadline)
                {
                    await Delay(PollMs);
                }
                return replies.TryGetValue(request, out var json)
                    ? DepartmentOutfitReply.Parse(json)
                    : DepartmentOutfitReply.Failed("The server didn't answer. Please try again.");
            }
            finally
            {
                waiting.Remove(request);
                replies.Remove(request);
            }
        }
    }
}
