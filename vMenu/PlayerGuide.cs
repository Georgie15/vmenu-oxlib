using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using MenuAPI;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static CitizenFX.Core.Native.API;
using static vMenuClient.CommonFunctions;

namespace vMenuClient
{
    /// <summary>
    /// The Player Guide: a walkthrough of the menu for new players, offered the first time they open
    /// it and kept in Main Menu > Player Guide, with single "How do I…?" answers. It works like the
    /// CAD tour and EasyAdmin's staff guide: Enter next, Backspace back, Esc skip. Each card opens the
    /// real menu page and lights up the rows it talks about; storage.html draws the card beside the
    /// menu. The text is config/player-guide.json.
    ///
    /// While a card is up, the menu's own buttons are off (MenuAPI drains their presses), and this
    /// reads the game's frontend controls itself. Nothing runs while the guide is off.
    /// </summary>
    public class PlayerGuide : BaseScript
    {
        internal const string ContentFile = "config/player-guide.json";
        /// <summary>The guide version the player has finished or skipped (resource KVP, on their PC).</summary>
        internal const string SeenKvp = "vmenu_player_guide_seen";

        // The game controls the guide reads while it runs.
        internal const int ControlPause = 199;          // P / Start: kept from opening the pause menu
        internal const int ControlPauseAlternate = 200; // Esc: skip
        internal const int ControlAccept = 201;         // Enter / A: next
        internal const int ControlCancel = 202;         // Backspace / B (Esc too, which counts as skip)
        internal const int ControlX = 203;              // X on a controller: skip
        internal const int ControlLeft = 189;           // ←: back
        internal const int ControlRight = 190;          // →: next
        internal const int ControlJump = 22;            // Space / X: kept from jumping when X skips
        private static readonly int[] GuideControls =
            { ControlPause, ControlPauseAlternate, ControlAccept, ControlCancel, ControlX, ControlLeft, ControlRight, ControlJump };

        /// <summary>Keys pressed this soon after a card opens are ignored, so the press that opened it can't also move it on.</summary>
        internal const int InputDelayMs = 200;

        private static GuideContent content;
        private static PlayerGuideFlow flow;
        private static Menu guideMenu;
        private static MenuItem startItem;
        private static readonly Dictionary<MenuItem, GuideCard> questionItems = new();

        private static TickSleeper inputSleeper;
        private static GuideView shown;
        private static List<string> shownLabels = new();
        private static bool? shownForPad;
        private static readonly GuideButtonHold hold = new();
        private static int acceptInputAt;
        /// <summary>Set after an error: the guide stays off for the rest of the session.</summary>
        private static bool broken;

        public PlayerGuide()
        {
            inputSleeper = new TickSleeper(InputTick, t => Tick += t, t => Tick -= t);
            Tick += InputTick;
        }

        #region Menu
        /// <summary>
        /// Adds Main Menu > Player Guide, as the first row. Called before the other submenus are made;
        /// <see cref="AddQuestions"/> fills in the questions once they exist.
        /// </summary>
        internal static void AddToMainMenu(Menu mainMenu)
        {
            content = GuideContent.Parse(LoadResourceFile(GetCurrentResourceName(), ContentFile), out var error);
            if (content == null)
            {
                Debug.WriteLine($"[vMenu] [Player Guide] {ContentFile} can't be used, so the guide is off: {error}");
                return;
            }
            flow = new PlayerGuideFlow(content, Available);

            guideMenu = new Menu(" ", "Player Guide");
            var button = new MenuItem("Player Guide", "New here? Learn how to make a character, get dressed, get a car and keep your saves.")
            {
                Label = "→→→"
            };
            mainMenu.AddMenuItem(button);
            MenuController.AddSubmenu(mainMenu, guideMenu);
            MenuController.BindMenuItem(mainMenu, guideMenu, button);

            startItem = new MenuItem("Start the guide", "Walks you through the menu, one page at a time. Enter goes on, Backspace goes back, Esc stops.")
            {
                Label = "→→→"
            };
            guideMenu.AddMenuItem(startItem);

            guideMenu.OnItemSelect += (sender, item, index) =>
            {
                if (broken || flow == null || flow.Mode != GuideMode.Off)
                {
                    if (broken)
                    {
                        Notify.Error("The Player Guide hit an error and is off until you reconnect.");
                    }
                    return;
                }
                if (item == startItem)
                {
                    Begin(flow.StartTour(startedFromMenu: true));
                }
                else if (questionItems.TryGetValue(item, out var question))
                {
                    Begin(flow.StartQuestion(question));
                }
            };
        }

        /// <summary>Adds the questions this player gets, once every menu exists.</summary>
        internal static void AddQuestions()
        {
            if (guideMenu == null)
            {
                return;
            }
            var questions = flow.AvailableQuestions();
            if (questions.Count > 0)
            {
                guideMenu.AddMenuItem(GetSpacerMenuItem("↓ How do I… ↓"));
            }
            foreach (var question in questions)
            {
                var item = new MenuItem(question.Title, "Shows you where it is in the menu.");
                questionItems[item] = question;
                guideMenu.AddMenuItem(item);
            }
            guideMenu.RefreshIndex();
        }

        /// <summary>The real menu for a card's page, or null when this player doesn't have it.</summary>
        internal static Menu MenuFor(string id)
        {
            switch (id)
            {
                case "main": return MainMenu.Menu;
                case "guide": return guideMenu;
                case "player": return MainMenu.PlayerSubmenu != null && MainMenu.PlayerSubmenu.Size > 0 ? MainMenu.PlayerSubmenu : null;
                case "vehicle": return MainMenu.VehicleSubmenu != null && MainMenu.VehicleSubmenu.Size > 0 ? MainMenu.VehicleSubmenu : null;
                case "playerOptions": return MainMenu.PlayerOptionsMenu?.GetMenu();
                case "appearance": return MainMenu.PlayerAppearanceMenu?.GetMenu();
                case "mpPed": return MainMenu.MpPedCustomizationMenu?.GetMenu();
                case "vehicleOptions": return MainMenu.VehicleOptionsMenu?.GetMenu();
                case "vehicleSpawner": return MainMenu.VehicleSpawnerMenu?.GetMenu();
                case "savedVehicles": return MainMenu.SavedVehiclesMenu?.GetTypeMenu();
                case "personalVehicle": return MainMenu.PersonalVehicleMenu?.GetMenu();
                case "misc": return MainMenu.MiscSettingsMenu?.GetMenu();
                default: return null;
            }
        }

        /// <summary>Whether this player gets a card: they have its menu, and its convar (if any) is on.</summary>
        private static bool Available(GuideCard card)
        {
            if (MenuFor(card.Menu) == null)
            {
                return false;
            }
            return card.RequiresConvar == null || GetConvar(card.RequiresConvar, "false") == "true";
        }
        #endregion

        #region Starting and ending
        /// <summary>
        /// Called by the menu watcher when a menu opens. The first time (or after the guide's version
        /// goes up), it offers the guide. Returns whether it did: the watcher passes that on with
        /// vMenu:MenuStateChanged, so What's New doesn't pop up over the card.
        /// </summary>
        internal static bool MenuOpened()
        {
            if (broken || flow == null || flow.Mode != GuideMode.Off || hold.Holding)
            {
                return false;
            }
            try
            {
                if (GetResourceKvpInt(SeenKvp) >= content.Version)
                {
                    return false;
                }
                return Begin(flow.Welcome());
            }
            catch (Exception e)
            {
                Fail(e);
                return false;
            }
        }

        private static bool Begin(GuideView view)
        {
            if (view == null)
            {
                return false;
            }
            hold.Start(MenuController.DisableMenuButtons);
            MenuController.DisableMenuButtons = true;
            // Woken before anything can go wrong: the input loop is what gives the menu its buttons back.
            inputSleeper?.Wake();
            return TryShow(view);
        }

        private static bool TryShow(GuideView view)
        {
            try
            {
                Show(view);
                return true;
            }
            catch (Exception e)
            {
                Fail(e);
                return false;
            }
        }

        /// <summary>
        /// Something went wrong. The guide stops for the rest of the session and the menu gets its
        /// buttons back, so a bug in the guide can never leave the menu unusable. The error goes to F8.
        /// </summary>
        private static void Fail(Exception e)
        {
            broken = true;
            Debug.WriteLine($"[vMenu] [Player Guide] stopped for this session after an error: {e}");
            flow?.MenuClosed();
            shown = null;
            shownLabels = new List<string>();
            try
            {
                SendNuiMessage("{\"action\":\"vmenuGuide\",\"visible\":false}");
            }
            catch (Exception)
            {
                // The card stays hidden on the next message; the buttons matter more.
            }
            if (inputSleeper == null)
            {
                ReleaseButtonsNow();
            }
            else
            {
                // The input loop hands the buttons back once the key that was down is let go.
                inputSleeper.Wake();
            }
        }

        private static void ReleaseButtonsNow()
        {
            if (hold.Holding)
            {
                MenuController.DisableMenuButtons = hold.ButtonsBefore;
                hold.Abandon();
            }
        }

        private static void Show(GuideView view)
        {
            shown = view;
            acceptInputAt = GetGameTimer() + InputDelayMs;
            var menu = MenuFor(view.Card.Menu) ?? MainMenu.Menu;
            Open(menu);
            shownLabels = SelectRows(menu, view.Card.Highlight);
            SendCard();
        }

        private static void End(GuideEnd end)
        {
            var last = shown;
            shown = null;
            shownLabels = new List<string>();
            SendNuiMessage("{\"action\":\"vmenuGuide\",\"visible\":false}");

            if (PlayerGuideFlow.CountsAsSeen(end))
            {
                SetResourceKvpInt(SeenKvp, content.Version);
            }

            if (end == GuideEnd.BackToGuideMenu)
            {
                OpenGuideMenu(startItem);
            }
            else if (end == GuideEnd.BackToQuestions && last != null)
            {
                OpenGuideMenu(questionItems.FirstOrDefault(q => q.Value == last.Card).Key);
            }

            // InputTick hands the buttons back once the key that ended the guide is let go (and any
            // on-screen keyboard is done with), whether or not a menu is still open.
            inputSleeper?.Wake();
        }

        private static void OpenGuideMenu(MenuItem select)
        {
            Open(guideMenu);
            var index = select == null ? -1 : guideMenu.GetMenuItems().IndexOf(select);
            if (index >= 0)
            {
                guideMenu.RefreshIndex(index);
            }
        }

        private static void Open(Menu menu)
        {
            if (menu == null || menu.Visible)
            {
                return;
            }
            MenuController.CloseAllMenus();
            menu.OpenMenu();
        }
        #endregion

        #region Rows and the card
        /// <summary>
        /// Selects the first of the rows to light up and scrolls so they are all in view where they fit.
        /// Returns the ones this player's menu has, for the page to light up.
        /// </summary>
        internal static List<string> SelectRows(Menu menu, List<string> highlight)
        {
            var items = menu.GetMenuItems();
            var found = new List<int>();
            for (var i = 0; i < items.Count; i++)
            {
                if (highlight.Contains(GuideInput.PlainText(items[i].Text)))
                {
                    found.Add(i);
                }
            }
            if (found.Count == 0)
            {
                return new List<string>();
            }
            menu.RefreshIndex(found[0], GuideInput.ViewOffset(found[0], found[found.Count - 1], menu.MaxItemsOnScreen, items.Count));
            return found.Select(i => GuideInput.PlainText(items[i].Text)).Distinct().ToList();
        }

        private static void SendCard()
        {
            if (shown == null)
            {
                return;
            }
            var pad = Game.CurrentInputMode == InputMode.GamePad;
            shownForPad = pad;
            var card = shown.Card;
            // Built as JSON objects rather than an anonymous type: the guide's file reader already
            // proves these work in FiveM's client runtime, which refuses some of .NET.
            var message = new JObject
            {
                ["action"] = "vmenuGuide",
                ["visible"] = true,
                ["kind"] = shown.Mode == GuideMode.Welcome ? "welcome" : shown.Mode == GuideMode.Tour ? "tour" : "question",
                ["title"] = card.Title,
                ["body"] = new JArray(card.Body.ToArray()),
                ["tip"] = card.Tip,
                ["foot"] = card.Foot,
                ["number"] = shown.Number,
                ["count"] = shown.Count,
                ["last"] = shown.IsLast,
                ["pad"] = pad,
                ["highlight"] = new JArray(shownLabels.ToArray()),
            };
            SendNuiMessage(message.ToString(Formatting.None));
        }
        #endregion

        #region Input
        private static async Task InputTick()
        {
            try
            {
                InputFrame();
            }
            catch (Exception e)
            {
                // Never leave the menu without its buttons: stop the guide and hand them back now.
                Fail(e);
                ReleaseButtonsNow();
                inputSleeper.Sleep();
            }
            await Task.FromResult(0);
        }

        private static void InputFrame()
        {
            if (flow == null || (flow.Mode == GuideMode.Off && !hold.Holding))
            {
                inputSleeper.Sleep();
                return;
            }

            foreach (var control in GuideControls)
            {
                DisableControlAction(0, control, true);
            }
            hold.NoteKeyboard(UpdateOnscreenKeyboard() == 0, GetGameTimer());

            if (flow.Mode == GuideMode.Off)
            {
                // The guide has ended. Hold the menu's buttons until the key that ended it is let go:
                // MenuAPI acts on a release, so giving them back sooner would pick the selected row.
                MenuController.DisableMenuButtons = true;
                if (!hold.ReadyToRelease(GuideControls.Any(c => IsDisabledControlPressed(0, c)), GetGameTimer()))
                {
                    return;
                }
                MenuController.DisableMenuButtons = hold.ButtonsBefore;
                inputSleeper.Sleep();
                return;
            }

            if (!MenuController.IsAnyMenuOpen())
            {
                End(flow.MenuClosed());
                return;
            }

            // Kept on every frame: MenuAPI's on-screen keyboard check puts back what it found.
            MenuController.DisableMenuButtons = true;

            var pad = Game.CurrentInputMode == InputMode.GamePad;
            if (pad != shownForPad)
            {
                SendCard(); // the key caps follow keyboard or controller
            }

            // MenuAPI hides the menu (and the page hides the card) over a fade, a player switch or the
            // pause menu; keys pressed then don't move the guide on unseen.
            if (GetGameTimer() < acceptInputAt || !IsScreenFadedIn() || IsPauseMenuActive() || IsPlayerSwitchInProgress())
            {
                return;
            }

            var key = GuideInput.Route(
                esc: IsDisabledControlJustPressed(0, ControlPauseAlternate),
                cancel: IsDisabledControlJustPressed(0, ControlCancel),
                accept: IsDisabledControlJustPressed(0, ControlAccept),
                x: IsDisabledControlJustPressed(0, ControlX),
                left: IsDisabledControlJustPressed(0, ControlLeft),
                right: IsDisabledControlJustPressed(0, ControlRight),
                pad: pad);

            GuideView view;
            GuideEnd end;
            switch (key)
            {
                case GuideKey.Skip:
                    view = flow.Skip(out end);
                    break;
                case GuideKey.Back:
                    view = flow.Back(out end);
                    break;
                case GuideKey.Next:
                    view = flow.Next(out end);
                    break;
                default:
                    return;
            }
            var sound = key == GuideKey.Next ? "SELECT" : "BACK";

            PlaySoundFrontend(-1, sound, "HUD_FRONTEND_DEFAULT_SOUNDSET", false);
            if (view != null)
            {
                TryShow(view);
            }
            else
            {
                End(end);
            }
        }
        #endregion
    }
}
