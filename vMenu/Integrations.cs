using System;

using CitizenFX.Core;

using vMenuClient.menus;  // access MiscSettings.Instance

namespace vMenuClient
{
    public class Integrations : BaseScript
    {
        public Integrations()
        {
            EventHandlers["vMenu:Integrations:SetPlayerBlips"] += new Action<bool>(enabled =>
            {
                MiscSettings.Instance?.ForceSetPlayerBlips(enabled);
                IdleRest.WakeAll();
            });

            EventHandlers["vMenu:Integrations:SetPlayerNames"] += new Action<bool>(enabled =>
            {
                MiscSettings.Instance?.ForceSetPlayerNames(enabled);
                // The names loop rests while names are off.
                IdleRest.WakeAll();
            });
        }
    }
}
