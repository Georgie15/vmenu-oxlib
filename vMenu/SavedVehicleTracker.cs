using System;

namespace vMenuClient
{
    /// <summary>
    /// Notices when a saved vehicle was changed in the menu, so vMenu can offer to update its save.
    /// It remembers which save the vehicle under the player came from (spawned from Saved Vehicles,
    /// or just saved), records how it looks when Vehicle Options opens, and compares when every menu
    /// closes. Looks are opaque strings (the serialized saved setup). Kept free of game calls so it
    /// can be tested.
    /// </summary>
    internal sealed class SavedVehicleTracker
    {
        private int vehicle;
        private string saveName;
        private string category;
        private string baseline;

        /// <summary>
        /// The player spawned this vehicle from a save, or saved it.
        /// </summary>
        internal void Track(int vehicle, string saveName, string category)
        {
            this.vehicle = vehicle;
            this.saveName = saveName;
            this.category = category;
            baseline = null;
        }

        /// <summary>
        /// Vehicle Options opened: remember the tracked vehicle's look, once per menu visit.
        /// </summary>
        internal void BeginVisit(int currentVehicle, Func<string> look)
        {
            if (baseline == null && vehicle != 0 && currentVehicle == vehicle)
            {
                baseline = look();
            }
        }

        /// <summary>
        /// Every menu closed. True when the tracked vehicle is still the player's, its save still
        /// exists, and it looks different from the start of the visit. Ends the visit either way.
        /// </summary>
        internal bool EndVisit(int currentVehicle, Func<string> look, Func<string, bool> saveExists, out string name, out string savedCategory)
        {
            name = saveName;
            savedCategory = category;
            var start = baseline;
            baseline = null;
            return start != null && currentVehicle != 0 && currentVehicle == vehicle && saveExists(saveName) && look() != start;
        }
    }
}
