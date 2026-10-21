using System.Collections.Generic;

namespace vMenuServer
{
    // Which vehicle each player set as their personal vehicle, recorded only once the server saw
    // them in its driver seat. Kick Passengers used to accept any vehicle net ID, so anyone could
    // empty anyone's car. The entity handle and model are kept as well, because a net ID is reused
    // once its vehicle is deleted.
    internal sealed class PersonalVehicleRegistry
    {
        private sealed class Entry
        {
            public int NetId;
            public int Entity;
            public uint Model;
        }

        private readonly Dictionary<string, Entry> vehicles = new Dictionary<string, Entry>();
        private readonly Dictionary<string, int> requests = new Dictionary<string, int>();

        // Starts a new registration for this player and forgets the old vehicle. Returns a token;
        // a slower, older registration that finishes later must not overwrite a newer one.
        public int Begin(string player)
        {
            vehicles.Remove(player);
            requests.TryGetValue(player, out var token);
            token++;
            requests[player] = token;
            return token;
        }

        public bool IsCurrent(string player, int token)
        {
            return requests.TryGetValue(player, out var current) && current == token;
        }

        public void Set(string player, int token, int netId, int entity, uint model)
        {
            if (IsCurrent(player, token))
            {
                vehicles[player] = new Entry { NetId = netId, Entity = entity, Model = model };
            }
        }

        public bool Owns(string player, int netId, int entity, uint model)
        {
            return vehicles.TryGetValue(player, out var entry)
                && entry.NetId == netId && entry.Entity == entity && entry.Model == model;
        }

        public void Remove(string player)
        {
            vehicles.Remove(player);
            requests.Remove(player);
        }
    }
}
