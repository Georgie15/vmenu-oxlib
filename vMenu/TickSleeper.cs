using System;
using System.Threading.Tasks;

namespace vMenuClient
{
    // Takes a tick handler off its script's tick list while it has nothing to do, and puts it
    // back when Wake() is called.
    //
    // FiveM's C# runtime starts every registered tick handler again each frame once its last
    // run has finished, and that start costs more than most handlers' own work. A handler that
    // is waiting for something only a call can switch on (noclip, prop placement) sleeps here
    // instead, so it costs nothing per frame. Wake() registers it again and it runs from the
    // next frame, the same frame it would have noticed the change when it checked every frame.
    //
    // The runtime matches handlers by method and target, and never starts a second run while
    // one is still in progress, so waking a handler that is still finishing its last run is
    // safe: it simply runs again once that run ends.
    internal sealed class TickSleeper
    {
        private readonly Func<Task> handler;
        private readonly Action<Func<Task>> register;
        private readonly Action<Func<Task>> unregister;

        public TickSleeper(Func<Task> handler, Action<Func<Task>> register, Action<Func<Task>> unregister)
        {
            this.handler = handler;
            this.register = register;
            this.unregister = unregister;
        }

        public bool Asleep { get; private set; }

        // Called by the handler itself when it finds nothing to do.
        public void Sleep()
        {
            if (Asleep)
            {
                return;
            }
            Asleep = true;
            unregister(handler);
        }

        public void Wake()
        {
            if (!Asleep)
            {
                return;
            }
            Asleep = false;
            register(handler);
        }
    }
}
