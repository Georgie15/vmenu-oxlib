using System;
using System.Threading.Tasks;

namespace vMenuClient
{
    /// <summary>
    /// Why <see cref="VehicleSpawnGate"/> turned a request away.
    /// </summary>
    internal enum VehicleSpawnBlock
    {
        None,
        /// <summary>The previous request is still streaming its model; nothing has been created for it.</summary>
        Loading,
        /// <summary>The previous request is past loading and creating its vehicle.</summary>
        Spawning,
        /// <summary>The interval after a completed spawn.</summary>
        Cooldown,
    }

    /// <summary>
    /// One spawn request holding the gate. It starts out loading its model; until it calls
    /// <see cref="TryBeginCreating"/> a request for a different vehicle may replace it.
    /// </summary>
    internal sealed class VehicleSpawnAttempt
    {
        internal VehicleSpawnAttempt(uint model, string saveName)
        {
            Model = model;
            SaveName = saveName;
        }

        public uint Model { get; }
        public string SaveName { get; }
        public bool IsLoading { get; private set; } = true;
        public bool IsCancelled { get; private set; }

        internal void Cancel() => IsCancelled = true;

        /// <summary>
        /// Call once the model is ready, before anything is deleted or created. False if a newer
        /// request replaced this one; it must then stop. After true it can no longer be replaced.
        /// </summary>
        public bool TryBeginCreating()
        {
            if (IsCancelled)
            {
                return false;
            }
            IsLoading = false;
            return true;
        }
    }

    // Used on the CitizenFX client script thread. Acquire before the first await;
    // a timestamp replaces detached timers so old attempts cannot unlock new ones.
    internal sealed class VehicleSpawnGate
    {
        private readonly Func<uint> _clock;
        private VehicleSpawnAttempt _current;
        private uint _finishedAt;
        private int _cooldownMilliseconds;

        public VehicleSpawnGate(Func<uint> clock)
        {
            _clock = clock;
        }

        /// <summary>The request holding the gate, or null.</summary>
        public VehicleSpawnAttempt Current => _current;

        public VehicleSpawnBlock Block => _current != null
            ? (_current.IsLoading ? VehicleSpawnBlock.Loading : VehicleSpawnBlock.Spawning)
            : unchecked(_clock() - _finishedAt) < (uint)_cooldownMilliseconds ? VehicleSpawnBlock.Cooldown : VehicleSpawnBlock.None;

        public bool IsBlocked => Block != VehicleSpawnBlock.None;

        public async Task<int> TrySpawnAsync(uint model, string saveName, Func<VehicleSpawnAttempt, Task<int>> spawn, int cooldownMilliseconds,
            Action<VehicleSpawnBlock, VehicleSpawnAttempt> onBlocked)
        {
            // A model can take seconds to stream, or never load. Until then the request has
            // deleted and created nothing, so choosing a different vehicle replaces it instead of
            // waiting it out. The replaced request stops at its next check (TryBeginCreating).
            if (_current != null && _current.IsLoading && (_current.Model != model || _current.SaveName != saveName))
            {
                _current.Cancel();
                _current = null;
            }

            var block = Block;
            if (block != VehicleSpawnBlock.None)
            {
                onBlocked(block, _current);
                return 0;
            }

            var attempt = new VehicleSpawnAttempt(model, saveName);
            _current = attempt;
            var handle = 0;
            try
            {
                handle = await spawn(attempt);
                return handle;
            }
            finally
            {
                // Invalid/rejected requests and exceptions must not leave the
                // spawner locked. Only completed spawns consume the cooldown.
                // A replaced request must not unlock or time the one that replaced it.
                if (_current == attempt)
                {
                    _cooldownMilliseconds = handle != 0 ? Math.Max(0, cooldownMilliseconds) : 0;
                    _finishedAt = _clock();
                    _current = null;
                }
            }
        }
    }
}
