using System;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace vMenuClient
{
    /// <summary>
    /// A player's own extras and livery for one vehicle model (Vehicle Extras > Save As My Preset),
    /// stored as <c>vmenu_vehicle_preset_&lt;model hash&gt;</c> and applied when that model is spawned
    /// from the spawner or by name. Saved vehicles keep their own setup. Kept free of game calls so
    /// it can be tested.
    /// </summary>
    internal sealed class VehiclePreset
    {
        // Starts with "vmenu" so the one-off KVP cleanup in MainMenu keeps it.
        internal const string KeyPrefix = "vmenu_vehicle_preset_";

        // Same range saved vehicles store.
        internal const int ExtraCount = 20;

        [JsonProperty("extras")]
        public Dictionary<int, bool> Extras { get; set; } = new Dictionary<int, bool>();

        // -1 when the model has no liveries of that kind.
        [JsonProperty("livery")]
        public int Livery { get; set; } = -1;

        [JsonProperty("modLivery")]
        public int ModLivery { get; set; } = -1;

        internal static string KeyFor(uint model) => KeyPrefix + model;

        internal static VehiclePreset Capture(Func<int, bool> extraExists, Func<int, bool> isExtraOn, int livery, int modLivery)
        {
            var preset = new VehiclePreset { Livery = livery < 0 ? -1 : livery, ModLivery = modLivery < 0 ? -1 : modLivery };
            for (var extra = 0; extra < ExtraCount; extra++)
            {
                if (extraExists(extra))
                {
                    preset.Extras[extra] = isExtraOn(extra);
                }
            }
            return preset;
        }

        /// <summary>
        /// Sets the saved extras the vehicle has, and the liveries if they are within its range.
        /// </summary>
        internal void Apply(Func<int, bool> extraExists, Action<int, bool> setExtraOn,
            int liveryCount, Action<int> setLivery, int modLiveryCount, Action<int> setModLivery)
        {
            foreach (var extra in Extras)
            {
                if (extra.Key >= 0 && extra.Key < ExtraCount && extraExists(extra.Key))
                {
                    setExtraOn(extra.Key, extra.Value);
                }
            }
            if (Livery >= 0 && Livery < liveryCount)
            {
                setLivery(Livery);
            }
            if (ModLivery >= 0 && ModLivery < modLiveryCount)
            {
                setModLivery(ModLivery);
            }
        }

        internal bool IsEmpty => Extras.Count == 0 && Livery < 0 && ModLivery < 0;

        internal string ToJson() => JsonConvert.SerializeObject(this);

        /// <summary>
        /// Reads a stored preset. Null when there is none or it can't be read.
        /// </summary>
        internal static VehiclePreset Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                var preset = JsonConvert.DeserializeObject<VehiclePreset>(json);
                if (preset == null)
                {
                    return null;
                }
                preset.Extras ??= new Dictionary<int, bool>();
                return preset;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
