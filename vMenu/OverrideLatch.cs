namespace vMenuClient
{
    // Decides when vMenu may write a game setting that other resources also change
    // (player invincibility and ragdoll, vehicle proofs, helmets, turbulence...).
    // vMenu used to write the default value every frame, which undid those resources
    // for every player who never used the vMenu option.
    //
    // Now vMenu only touches entities it changed itself. While the option is on it is
    // enforced on the current target. When the option goes off, or the target changes,
    // the entity vMenu last changed is put back to its default once. Target 0 means
    // "no current target" (for example on foot) and leaves the last one as it is.
    internal sealed class OverrideLatch
    {
        private int _applied;

        // Returns true when the override should be applied to target now. restore is the
        // entity to put back to its default first, or 0.
        public bool Update(bool enabled, int target, out int restore)
        {
            if (!enabled)
            {
                restore = _applied;
                _applied = 0;
                return false;
            }

            if (target == 0)
            {
                restore = 0;
                return false;
            }

            restore = _applied != target ? _applied : 0;
            _applied = target;
            return true;
        }
    }
}
