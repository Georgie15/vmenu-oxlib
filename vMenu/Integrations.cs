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
            });

            EventHandlers["vMenu:Integrations:SetPlayerNames"] += new Action<bool>(enabled =>
            {
                MiscSettings.Instance?.ForceSetPlayerNames(enabled);
            });
        }
    }
}
