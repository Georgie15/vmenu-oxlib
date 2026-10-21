using System;
using System.Collections.Generic;

namespace vMenuClient
{
    // Keeps one overhead name tag per nearby player. Each update used to create a new tag for
    // every nearby player, and a player who left kept their dictionary entry (and tag) for good.
    internal sealed class OverheadNameTags
    {
        private readonly Dictionary<int, int> tags = new Dictionary<int, int>();
        private readonly HashSet<int> seen = new HashSet<int>();

        public int Count => tags.Count;

        // Call once per update before visiting players.
        public void BeginUpdate()
        {
            seen.Clear();
        }

        // Call for every other player in scope. Returns the tag to style, or -1 if this player
        // shows no tag. A tag is only created when the player has none that is still active.
        public int Visit(int serverId, bool closeEnough, Func<int, bool> isActive, Func<int> create, Action<int> remove)
        {
            seen.Add(serverId);
            var hasTag = tags.TryGetValue(serverId, out var tag);
            if (!closeEnough)
            {
                if (hasTag)
                {
                    remove(tag);
                    tags.Remove(serverId);
                }
                return -1;
            }

            if (!hasTag || !isActive(tag))
            {
                tag = create();
                tags[serverId] = tag;
            }
            return tag;
        }

        // Call after visiting players: removes the tags of players not seen in this update.
        public void EndUpdate(Action<int> remove)
        {
            var gone = new List<int>();
            foreach (var entry in tags)
            {
                if (!seen.Contains(entry.Key))
                {
                    remove(entry.Value);
                    gone.Add(entry.Key);
                }
            }
            foreach (var serverId in gone)
            {
                tags.Remove(serverId);
            }
        }

        public void Clear(Action<int> remove)
        {
            foreach (var entry in tags)
            {
                remove(entry.Value);
            }
            tags.Clear();
        }
    }
}
