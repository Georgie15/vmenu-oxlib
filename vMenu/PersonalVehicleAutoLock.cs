namespace vMenuClient
{
    /// <summary>
    /// Personal Vehicle > Auto-Lock When I Walk Away. The car locks once when the player walks from
    /// within <see cref="NearDistance"/> of it to beyond <see cref="FarDistance"/>, if nobody is in it
    /// and it isn't already locked. The gap between the two stops it flickering at the edge, and it
    /// only re-arms once the player comes back. Kept free of game calls so it can be tested.
    /// </summary>
    internal sealed class PersonalVehicleAutoLock
    {
        internal const float NearDistance = 10f;
        internal const float FarDistance = 15f;

        private bool armed;

        /// <summary>
        /// True when the car should lock now.
        /// </summary>
        internal bool Update(bool enabled, float distance, bool occupied, bool locked)
        {
            if (!enabled)
            {
                armed = false;
                return false;
            }
            if (distance <= NearDistance)
            {
                armed = true;
                return false;
            }
            if (distance < FarDistance || !armed)
            {
                return false;
            }
            armed = false;
            return !occupied && !locked;
        }
    }
}
