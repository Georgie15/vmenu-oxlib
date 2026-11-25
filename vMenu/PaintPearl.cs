using System.Collections.Generic;

namespace vMenuClient
{
    // The pearlescent is the coat over Classic and Metallic paint (the game's first "extra colour").
    // vMenu used to reset it on every primary colour change: a Metallic colour set a matching
    // pearlescent and any other paint set none, so a pearlescent picked first, or before touching up
    // the paint, was lost. Now the one the player picked, or the one a saved vehicle came with, stays
    // on that vehicle. This file has no game calls, so tests/vehicle-pearlescent can run it.
    internal sealed class PaintPearl
    {
        private int keptVehicle;
        private int keptPearl;

        // The player picked this pearlescent on this vehicle, or a saved vehicle came with it.
        public void Keep(int vehicle, int pearl)
        {
            keptVehicle = vehicle;
            keptPearl = pearl;
        }

        // Only while the vehicle still has that pearlescent, so a preset colour combination, or a new
        // vehicle that got an old handle, goes back to the automatic pearlescent.
        public bool IsKept(int vehicle, int pearl) => vehicle != 0 && vehicle == keptVehicle && pearl == keptPearl;

        // The pearlescent after the primary paint changes to newPrimary. metallic: picked from the
        // Metallic row, which matches the pearlescent to the paint. Chameleon paints take none.
        public int AfterPrimaryChange(int vehicle, int pearl, int newPrimary, bool metallic, bool chameleon)
        {
            if (chameleon)
            {
                return 0;
            }
            if (IsKept(vehicle, pearl))
            {
                return pearl;
            }
            return metallic ? newPrimary : 0;
        }

        // A saved pearlescent the paint wouldn't have set by itself, so the player chose it.
        public static bool IsOwnChoice(int primary, int pearl) => pearl != 0 && pearl != primary;

        // The row of a colour list that shows colour id, or the first row when the list hasn't got it.
        public static int RowOf(IList<int> ids, int id)
        {
            var row = ids.IndexOf(id);
            return row < 0 ? 0 : row;
        }
    }
}
