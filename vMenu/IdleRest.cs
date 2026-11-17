using System.Collections.Generic;
using System.Threading.Tasks;

namespace vMenuClient
{
    // An idle rest that ends early when a menu opens.
    //
    // The loops that rest while idle (see FunctionsController) run every frame again while any
    // menu is open. A rest that only ended on its timer would leave them up to 250 ms behind a
    // menu opening. That is too late for the character creator coming back after its "exit
    // without saving" prompt: its back button guard, camera and movement lock are all needed on
    // the next frame, as before. MainMenu's menu watcher, which runs every frame, calls
    // MenuOpened when a menu opens, and that ends every rest in progress.
    //
    // Each rest is listed only while it is in progress and leaves the list when it ends either
    // way, so the list holds at most one entry per resting loop however long the menu stays shut.
    internal static class IdleRest
    {
        private static readonly object gate = new object();
        private static readonly List<TaskCompletionSource<bool>> resting = new List<TaskCompletionSource<bool>>();

        // Rests in progress (for tests).
        internal static int Resting { get { lock (gate) { return resting.Count; } } }

        // Completes when the delay does, or when a menu opens, whichever is first.
        public static Task Until(Task delay)
        {
            var rest = new TaskCompletionSource<bool>();
            lock (gate)
            {
                resting.Add(rest);
            }
            delay.ContinueWith(_ => End(rest), TaskContinuationOptions.ExecuteSynchronously);
            return rest.Task;
        }

        public static void MenuOpened()
        {
            TaskCompletionSource<bool>[] woken;
            lock (gate)
            {
                if (resting.Count == 0)
                {
                    return;
                }
                woken = resting.ToArray();
                resting.Clear();
            }
            foreach (var rest in woken)
            {
                rest.TrySetResult(true);
            }
        }

        private static void End(TaskCompletionSource<bool> rest)
        {
            lock (gate)
            {
                resting.Remove(rest);
            }
            rest.TrySetResult(true);
        }
    }
}
