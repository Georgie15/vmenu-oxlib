using System;

namespace vMenuClient
{
    internal sealed class PersonalVehicleLockInput
    {
        private const int DoubleTapWindowMs = 500;
        private KeyTap? mappedTap;
        private KeyTap? hornTap;
        private int lastToggleFrame = -1;

        private readonly struct KeyTap
        {
            internal readonly int Frame;
            internal readonly int Time;

            internal KeyTap(int frame, int time)
            {
                Frame = frame;
                Time = time;
            }
        }

        internal bool RegisterTap(int frame, int time, bool fromHorn, Func<int, bool> tackleClaimed)
        {
            if (frame <= lastToggleFrame || tackleClaimed(frame))
            {
                return false;
            }

            // Different keys cannot form a pair. If both shortcuts share a key,
            // the frame still identifies one physical press across the two paths.
            ref var firstTap = ref (fromHorn ? ref hornTap : ref mappedTap);
            if (firstTap is { } previous)
            {
                if (frame <= previous.Frame)
                {
                    return false;
                }
                if (time - previous.Time < DoubleTapWindowMs && !tackleClaimed(previous.Frame))
                {
                    lastToggleFrame = frame;
                    mappedTap = null;
                    hornTap = null;
                    return true;
                }
            }

            firstTap = new KeyTap(frame, time);
            return false;
        }

        internal void ResetHornTap() => hornTap = null;
    }
}
