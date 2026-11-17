using System;
using System.Collections.Generic;

using CitizenFX.Core;

using MenuAPI;

using static CitizenFX.Core.Native.API;

namespace vMenuClient
{
    // Extra keys and button hints for single menus, on MenuAPI 4.
    //
    // MenuAPI 3 read in-menu keys as game controls. MenuAPI 4 turns each one into a FiveM key
    // binding that the player can change in Settings > Key Bindings > FiveM. Two things it no
    // longer does are kept here; MainMenu calls OnFrame every frame with the open menu:
    //  - MenuAPI 3 could disable the game control on the same key while the menu was open, so
    //    Space filtered the ped list without making the player jump. A key binding is not a game
    //    control, so the control is disabled here.
    //  - MenuAPI 3 drew hints for game controls by control id, looked up every frame. MenuAPI 4
    //    takes the button string, which only names the key bound when it is made, so hints are
    //    rebuilt when their menu opens and when the player switches between keyboard and
    //    controller while it is open.
    internal static class MenuKeys
    {
        private static readonly Dictionary<Menu, List<Control>> shadowedControls = new();
        private static readonly Dictionary<Menu, Action<Menu>> hintRefreshers = new();
        private static Menu hintMenu;
        private static bool hintMenuOnKeyboard;

        // A key for one menu. `name` is the key binding's id: menus that share a name share the
        // key, and the player's choice is saved under it, so it must never change.
        // `controllerButton` is the default pad button (MenuAPI's names, such as "RLEFT_INDEX").
        public static void Add(Menu menu, string name, string description, string keyboardKey, string controllerButton,
            MenuKeyPressType pressType, Action<Menu> handler, string hint, Control? shadowedControl = null)
        {
            menu.AddKeyBinding(name, description, keyboardKey, controllerButton, pressType, (m, _) => handler(m), hint);
            if (shadowedControl is Control control)
            {
                if (!shadowedControls.TryGetValue(menu, out var controls))
                {
                    shadowedControls[menu] = controls = new List<Control>();
                }
                controls.Add(control);
            }
        }

        // Called every frame with the open menu, or null.
        public static void OnFrame(Menu openMenu)
        {
            if (openMenu == null)
            {
                hintMenu = null;
                return;
            }

            if (shadowedControls.TryGetValue(openMenu, out var controls))
            {
                foreach (var control in controls)
                {
                    DisableControlAction(0, (int)control, true);
                }
            }

            if (hintRefreshers.TryGetValue(openMenu, out var refresh))
            {
                var onKeyboard = IsUsingKeyboard(2);
                if (openMenu != hintMenu || onKeyboard != hintMenuOnKeyboard)
                {
                    hintMenu = openMenu;
                    hintMenuOnKeyboard = onKeyboard;
                    refresh(openMenu);
                }
            }
            else
            {
                hintMenu = null;
            }
        }

        // A hint for a game control that vMenu reads itself. (FiveM's client runtime has no ValueTuple.)
        public readonly struct Hint
        {
            public readonly Control Control;
            public readonly string Text;

            public Hint(Control control, string text)
            {
                Control = control;
                Text = text;
            }
        }

        // Hints for game controls that vMenu reads itself, such as the character creator's camera keys.
        public static void AddControlHints(Menu menu, params Hint[] hints)
        {
            var added = new List<Menu.InstructionalButton>();

            void Refresh(Menu m)
            {
                foreach (var hint in added)
                {
                    m.CustomInstructionalButtons.Remove(hint);
                }
                added.Clear();
                foreach (var hint in hints)
                {
                    var button = new Menu.InstructionalButton(GetControlInstructionalButton(0, (int)hint.Control, 1), hint.Text);
                    added.Add(button);
                    m.CustomInstructionalButtons.Add(button);
                }
            }

            Refresh(menu);
            hintRefreshers[menu] = Refresh;
        }
    }
}
