using System;
using System.Collections.Generic;

namespace vMenuClient
{
    // FiveM Legacy on game build 2699 or newer lists some clothing and vehicle upgrades that
    // were made for GTA V Enhanced (gen9), but the game refuses to put them on. Picking one
    // in a menu did nothing. These helpers leave them out. Gen9Parts asks the game which
    // pieces they are; this file has no game calls, so tests/gen9-parts can run it.
    internal static class Gen9Skip
    {
        // The indexes 0..count-1 the game will fit, in order.
        public static List<int> Kept(int count, Func<int, bool> gen9Only)
        {
            var kept = new List<int>(Math.Max(0, count));
            for (var index = 0; index < count; index++)
            {
                if (!gen9Only(index))
                {
                    kept.Add(index);
                }
            }
            return kept;
        }

        // A list moved one step from `from` to `to` (wrapping at either end). Returns `to`,
        // or the next index in the same direction the game will fit. Stays on `from` when
        // every other index is gen9 only.
        public static int Step(int from, int to, int count, Func<int, bool> gen9Only)
        {
            if (count <= 1 || to < 0 || to >= count || !gen9Only(to))
            {
                return to;
            }

            var step = to == (from + 1) % count ? 1 : -1;
            var next = to;
            for (var tries = 1; tries < count; tries++)
            {
                next = (next + step + count) % count;
                if (next == from || !gen9Only(next))
                {
                    return next;
                }
            }
            return from;
        }
    }
}
