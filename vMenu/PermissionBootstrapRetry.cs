namespace vMenuClient
{
    // Schedule for re-sending vMenu:RequestPermissions when the reply was lost. The first
    // request still goes out at resource start. A retry only happens while the menu is
    // not built yet (SetPermissions/SetAddons never arrived), which otherwise left F1,
    // noclip and all of vMenu dead for the whole session.
    //
    // The waits are long on purpose. The deployed server builds one player's permissions
    // at a time and does not merge duplicate requests, so after a vMenu restart with a
    // full server the honest reply can take a while; short retries would only add to
    // that line. A genuinely lost reply is recovered within the first wait.
    internal sealed class PermissionBootstrapRetry
    {
        // Waits after request 1, 2, 3; every later request waits the last value.
        internal static readonly int[] WaitsMilliseconds = { 45000, 90000, 180000, 300000 };

        public int Requests { get; private set; }

        // Call once per scheduling step. Returns -1 when ready (stop), otherwise records one
        // request and returns how long to wait before the next check.
        public int Next(bool ready)
        {
            if (ready)
            {
                return -1;
            }
            Requests++;
            var index = Requests - 1;
            return WaitsMilliseconds[index < WaitsMilliseconds.Length ? index : WaitsMilliseconds.Length - 1];
        }

        // True when this request is a retry (the reply to an earlier one never completed setup).
        public bool IsRetry => Requests > 1;
    }
}
