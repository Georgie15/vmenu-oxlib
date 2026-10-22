using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

namespace vMenuClient
{
    /// <summary>
    /// A short list of vehicle model hashes: the spawner's Favourite Vehicles (added by the player)
    /// and Recent Vehicles (most recent first). Stored as a JSON array in one KVP each; the keys
    /// start with "vmenu" so MainMenu's one-off KVP cleanup keeps them, and save backups include
    /// them. Kept free of game calls so it can be tested.
    /// </summary>
    internal sealed class VehicleShortlist
    {
        internal const string FavouritesKey = "vmenu_vehicle_favourites";
        internal const string RecentKey = "vmenu_vehicle_recent";
        internal const int MaxFavourites = 50;
        internal const int MaxRecent = 10;

        private readonly List<uint> models;
        private readonly int capacity;

        private VehicleShortlist(IEnumerable<uint> models, int capacity)
        {
            this.capacity = capacity;
            this.models = models.Distinct().Take(capacity).ToList();
        }

        internal IReadOnlyList<uint> Models => models;

        internal bool Contains(uint model) => models.Contains(model);

        /// <summary>
        /// Adds a favourite at the end. False if it's already there or the list is full.
        /// </summary>
        internal bool Add(uint model)
        {
            if (model == 0 || models.Contains(model) || models.Count >= capacity)
            {
                return false;
            }
            models.Add(model);
            return true;
        }

        /// <summary>
        /// Moves a model to the front (most recent), dropping the oldest past capacity.
        /// </summary>
        internal void Touch(uint model)
        {
            if (model == 0)
            {
                return;
            }
            models.Remove(model);
            models.Insert(0, model);
            if (models.Count > capacity)
            {
                models.RemoveRange(capacity, models.Count - capacity);
            }
        }

        internal bool Remove(uint model) => models.Remove(model);

        internal void Clear() => models.Clear();

        internal string ToJson() => JsonConvert.SerializeObject(models);

        /// <summary>
        /// Reads a stored list; a missing or damaged one is empty.
        /// </summary>
        internal static VehicleShortlist Parse(string json, int capacity)
        {
            List<uint> stored = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    stored = JsonConvert.DeserializeObject<List<uint>>(json);
                }
                catch (JsonException)
                {
                }
            }
            return new VehicleShortlist((stored ?? new List<uint>()).Where(model => model != 0), capacity);
        }
    }
}
