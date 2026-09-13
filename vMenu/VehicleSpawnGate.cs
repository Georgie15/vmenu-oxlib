using System;
using System.Threading.Tasks;

namespace vMenuClient
{
    // Used on the CitizenFX client script thread. Acquire before the first await;
    // a timestamp replaces detached timers so old attempts cannot unlock new ones.
    internal sealed class VehicleSpawnGate
    {
        private readonly Func<uint> _clock;
        private bool _spawning;
        private uint _finishedAt;
        private int _cooldownMilliseconds;

        public VehicleSpawnGate(Func<uint> clock)
        {
            _clock = clock;
        }

        public bool IsBlocked => _spawning || unchecked(_clock() - _finishedAt) < (uint)_cooldownMilliseconds;

        public async Task<int> TrySpawnAsync(Func<Task<int>> spawn, int cooldownMilliseconds, Action onBlocked)
        {
            if (IsBlocked)
            {
                onBlocked();
                return 0;
            }

            _spawning = true;
            var handle = 0;
            try
            {
                handle = await spawn();
                return handle;
            }
            finally
            {
                // Invalid/rejected requests and exceptions must not leave the
                // spawner locked. Only completed spawns consume the cooldown.
                _cooldownMilliseconds = handle != 0 ? Math.Max(0, cooldownMilliseconds) : 0;
                _finishedAt = _clock();
                _spawning = false;
            }
        }
    }
}
