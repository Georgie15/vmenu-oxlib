using System;
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
    /// The dressing room's guide (PSRP's psrp_dressing_room resource): a new player makes their first
    /// character with the Player Guide's card beside the menu. psrp_dressing_room starts it with the
    /// local event vMenu:DressingRoom:Start (where the player goes next, and "male" or "female" from
    /// the character they picked) once the welcome is closed. With a body it starts a new character
    /// of it and opens the character creator; without one it opens MP Ped Customization on Create
    /// Male / Female Character. Each step is ticked off as the player does it (DressingRoomFlow); the
    /// menu stays theirs. After Save Character one card with its own keys
    /// sends them on: Enter fires vMenu:DressingRoom:Leave, Backspace (or closing the menu) keeps the
    /// menu and fires vMenu:DressingRoom:Ended. Steps live in config/player-guide.json, "dressingRoom".
    ///
    /// Answers: vMenu:DressingRoom:Started, or vMenu:DressingRoom:Unavailable (no MP Ped
    /// Customization for this player, or no steps), so the resource can fall back to its own prompt.
    /// vMenu:DressingRoom:Stop ends it from outside. Nothing runs while it is off.
    /// </summary>
    public class DressingRoomGuide : BaseScript
    {
        private static readonly string[] Pages = new[] { "mpPed" }.Concat(GuideContent.CreatorPages).ToArray();
        private const string Hidden = "{\"action\":\"vmenuGuide\",\"visible\":false}";

        private static DressingRoomFlow flow;
        private static TickSleeper sleeper;
        private static readonly GuideButtonHold hold = new();
        private static bool buttonsAtStart;
        private static bool startPending;
        /// <summary>The body to start the character with (from the spawn selector), or null to let the player pick.</summary>
        private static bool? startMale;
        /// <summary>The new character's model is loading; the creator opens when it's done.</summary>
        private static bool openingCreator;
        private static bool dirty;
        private static string shown;
        private static bool? shownForPad;
        private static int lastStep = -1;
        private static int acceptInputAt;
        /// <summary>Set after an error: off for the rest of the session, and psrp_dressing_room is told.</summary>
        private static bool broken;

        /// <summary>From Start until the finish card is answered or it is stopped.</summary>
        internal static bool Active => flow != null && flow.Phase != DressingPhase.Off;

        public DressingRoomGuide()
        {
            sleeper = new TickSleeper(FrameTick, t => Tick += t, t => Tick -= t);
            Tick += FrameTick;
            EventHandlers.Add("vMenu:DressingRoom:Start", new Action<string, string>(Start));
            EventHandlers.Add("vMenu:DressingRoom:Stop", new Action(StopFromOutside));
        }

        #region Starting and stopping
        /// <summary>Why the guide can't run for this player, or null when it can. Printed to F8 when it says no.</summary>
        private static string Unavailable()
        {
            if (broken)
            {
                return "it stopped after an error earlier this session (see the line above)";
            }
            if (MainMenu.Menu == null)
            {
                return "the menu isn't set up yet (permissions not received, or vmenu_menu_staff_only)";
            }
            if (PlayerGuide.Content == null)
            {
                return $"{PlayerGuide.ContentFile} can't be used (see the [Player Guide] line)";
            }
            if (PlayerGuide.Content.DressingRoom == null)
            {
                return $"{PlayerGuide.ContentFile} has no \"dressingRoom\" section: upload release O's copy of that file";
            }
            if (PlayerGuide.MenuFor("mpPed") == null)
            {
                return "this player has no MP Ped Customization (permissions)";
            }
            return null;
        }

        private static void Start(string destination, string body)
        {
            try
            {
                var why = Unavailable();
                if (why != null)
                {
                    Debug.WriteLine($"[vMenu] [Dressing Room] not starting: {why}.");
                    TriggerEvent("vMenu:DressingRoom:Unavailable");
                    return;
                }
                flow ??= new DressingRoomFlow(PlayerGuide.Content.DressingRoom);
                if (!hold.Holding)
                {
                    buttonsAtStart = MenuController.DisableMenuButtons;
                }
                flow.Start(destination);
                startMale = DressingRoomFlow.BodyFrom(body);
                startPending = true;
                dirty = true;
                shown = null;
                lastStep = -1;
                sleeper.Wake();
                TriggerEvent("vMenu:DressingRoom:Started");
            }
            catch (Exception e)
            {
                Fail(e);
                TriggerEvent("vMenu:DressingRoom:Unavailable");
            }
        }

        /// <summary>psrp_dressing_room is sending the player off (or stopping): the card goes, and so does the menu.</summary>
        private static void StopFromOutside()
        {
            if (!Active)
            {
                return;
            }
            flow.Stop();
            Hide();
            MenuController.CloseAllMenus();
            sleeper.Wake(); // hands the buttons back if the finish card had them
        }

        /// <summary>MP Ped Customization saved a character (Save Character).</summary>
        internal static void CharacterSaved()
        {
            try
            {
                if (!Active || !flow.Saved())
                {
                    return;
                }
                // So the next join brings them back in it, as spawning it from Saved Characters would.
                MainMenu.MpPedCustomizationMenu?.RememberCurrentCharacter();
                // The finish card has keys of its own, so the menu's are off while it is up. The
                // setting from before the dressing room is the one to put back: an on-screen keyboard
                // (the save name) may have changed it for a moment.
                hold.Start(buttonsAtStart);
                MenuController.DisableMenuButtons = true;
                acceptInputAt = GetGameTimer() + PlayerGuide.InputDelayMs;
                dirty = true;
                sleeper.Wake();
                TriggerEvent("vMenu:DressingRoom:Saved");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Fail(Exception e)
        {
            broken = true;
            Debug.WriteLine($"[vMenu] [Dressing Room] stopped for this session after an error: {e}");
            flow?.Stop();
            try
            {
                Hide();
            }
            catch (Exception)
            {
                // The buttons matter more than the card.
            }
            TriggerEvent("vMenu:DressingRoom:Ended", "error");
            sleeper?.Wake();
        }

        private static void ReleaseButtonsNow()
        {
            if (hold.Holding)
            {
                MenuController.DisableMenuButtons = hold.ButtonsBefore;
                hold.Abandon();
            }
        }
        #endregion

        #region Every frame while it is on
        private static async Task FrameTick()
        {
            try
            {
                Frame();
            }
            catch (Exception e)
            {
                Fail(e);
                ReleaseButtonsNow();
                sleeper.Sleep();
            }
            await Task.FromResult(0);
        }

        private static void Frame()
        {
            if (flow == null || (!Active && !hold.Holding))
            {
                sleeper.Sleep();
                return;
            }

            if (hold.Holding)
            {
                foreach (var control in PlayerGuide.Controls)
                {
                    DisableControlAction(0, control, true);
                }
                hold.NoteKeyboard(UpdateOnscreenKeyboard() == 0, GetGameTimer());
            }

            if (!Active)
            {
                // Over: the menu gets its buttons back once the key that ended it is let go (MenuAPI
                // acts on a key's release, so sooner would also pick the selected row).
                MenuController.DisableMenuButtons = true;
                if (!hold.ReadyToRelease(PlayerGuide.Controls.Any(c => IsDisabledControlPressed(0, c)), GetGameTimer()))
                {
                    return;
                }
                MenuController.DisableMenuButtons = hold.ButtonsBefore;
                sleeper.Sleep();
                return;
            }

            if (startPending)
            {
                // The Player Guide ends on the frame after its menu closes; its card goes with it.
                if (PlayerGuide.Busy)
                {
                    if (MenuController.IsAnyMenuOpen())
                    {
                        MenuController.CloseAllMenus();
                    }
                    return;
                }
                if (!IsScreenFadedIn() || IsPlayerSwitchInProgress() || IsPauseMenuActive())
                {
                    return;
                }
                startPending = false;
                if (startMale.HasValue && DressingRoomFlow.StartsInCreator(flow.CurrentStep))
                {
                    // The spawn selector already knows the body: skip the pick and open the creator on it.
                    _ = OpenCreator(startMale.Value);
                }
                else
                {
                    OpenStepPage();
                }
                return;
            }

            if (openingCreator)
            {
                return;
            }

            var page = PageOf(GetOpenMenu());
            var before = flow.Page;
            if (flow.Update(page))
            {
                dirty = true;
                if (flow.Phase == DressingPhase.Finish && page == DressingRoomFlow.Closed)
                {
                    // Closing the menu on the finish card: they stay, and the store's own prompt takes over.
                    flow.Stay();
                    Hide();
                    TriggerEvent("vMenu:DressingRoom:Ended", "stay");
                    return;
                }
                if (flow.Phase == DressingPhase.Steps && before == DressingRoomFlow.Closed && !DressingRoomFlow.IsDressingPage(page))
                {
                    // Opened again (the menu key opens the main menu): straight back to the step.
                    OpenStepPage();
                    return;
                }
            }

            if (flow.Phase == DressingPhase.Steps && flow.Page == flow.CurrentStep.Card.Menu && flow.CurrentIndex != lastStep)
            {
                // A new step on the page it points at: the cursor moves to its row.
                lastStep = flow.CurrentIndex;
                var menu = PlayerGuide.MenuFor(flow.Page);
                if (menu != null)
                {
                    PlayerGuide.SelectRows(menu, flow.CurrentStep.Card.Highlight);
                }
                dirty = true;
            }

            var pad = Game.CurrentInputMode == InputMode.GamePad;
            if (pad != shownForPad)
            {
                dirty = true; // the key caps follow keyboard or controller
            }
            if (dirty)
            {
                dirty = false;
                Send(pad);
            }

            if (flow.Phase == DressingPhase.Finish)
            {
                FinishKeys(pad);
            }
        }

        private static void FinishKeys(bool pad)
        {
            // Kept on every frame: MenuAPI's on-screen keyboard check puts back what it found.
            MenuController.DisableMenuButtons = true;
            if (GetGameTimer() < acceptInputAt || !IsScreenFadedIn() || IsPauseMenuActive() || IsPlayerSwitchInProgress()
                || !MenuController.IsAnyMenuOpen())
            {
                return;
            }
            // No arrow keys here: one slip shouldn't send the player off.
            var key = GuideInput.Route(
                esc: IsDisabledControlJustPressed(0, PlayerGuide.ControlPauseAlternate),
                cancel: IsDisabledControlJustPressed(0, PlayerGuide.ControlCancel),
                accept: IsDisabledControlJustPressed(0, PlayerGuide.ControlAccept),
                x: IsDisabledControlJustPressed(0, PlayerGuide.ControlX),
                left: false,
                right: false,
                pad: pad);
            if (key == GuideKey.Next)
            {
                PlaySoundFrontend(-1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", false);
                flow.Leave();
                Hide();
                MenuController.CloseAllMenus();
                TriggerEvent("vMenu:DressingRoom:Leave");
            }
            else if (key == GuideKey.Back || key == GuideKey.Skip)
            {
                PlaySoundFrontend(-1, "BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET", false);
                flow.Stay();
                Hide();
                TriggerEvent("vMenu:DressingRoom:Ended", "stay");
            }
        }
        #endregion

        #region Pages and the card
        /// <summary>The dressing room's id for a menu: one of its pages, other, or closed.</summary>
        internal static string PageOf(Menu menu)
        {
            if (menu == null)
            {
                return DressingRoomFlow.Closed;
            }
            foreach (var id in Pages)
            {
                if (PlayerGuide.MenuFor(id) == menu)
                {
                    return id;
                }
            }
            return DressingRoomFlow.Elsewhere;
        }

        /// <summary>
        /// Does what Create Male / Female Character does, then opens the creator. Its step is ticked when
        /// the creator opens, as if the player had picked the row. If the model won't load, MP Ped
        /// Customization opens on the step instead.
        /// </summary>
        private static async Task OpenCreator(bool male)
        {
            openingCreator = true;
            try
            {
                var started = await MainMenu.MpPedCustomizationMenu.StartNewCharacter(male);
                if (!Active)
                {
                    return;
                }
                if (!started)
                {
                    OpenStepPage();
                    return;
                }
                var creator = PlayerGuide.MenuFor("mpCreator");
                MenuController.CloseAllMenus();
                creator.OpenMenu();
                dirty = true;
            }
            catch (Exception e)
            {
                Fail(e);
            }
            finally
            {
                openingCreator = false;
                sleeper?.Wake();
            }
        }

        private static void OpenStepPage()
        {
            var step = flow.CurrentStep;
            var menu = PlayerGuide.MenuFor(step.Card.Menu);
            if (menu == null)
            {
                return;
            }
            if (!menu.Visible)
            {
                MenuController.CloseAllMenus();
                menu.OpenMenu();
            }
            PlayerGuide.SelectRows(menu, step.Card.Highlight);
            lastStep = flow.CurrentIndex;
            dirty = true;
        }

        private static void Hide()
        {
            shown = Hidden;
            SendNuiMessage(Hidden);
        }

        private static void Send(bool pad)
        {
            shownForPad = pad;
            var view = flow.View();
            string message;
            if (view == null)
            {
                message = Hidden;
            }
            else
            {
                // Built as JSON objects, as PlayerGuide does: proven in FiveM's client runtime.
                message = new JObject
                {
                    ["action"] = "vmenuGuide",
                    ["visible"] = true,
                    ["kind"] = view.Phase == DressingPhase.Finish ? "dressingDone" : "dressing",
                    ["title"] = view.Title,
                    ["body"] = new JArray(view.Body.ToArray()),
                    ["tip"] = view.Tip,
                    ["foot"] = view.Foot,
                    ["number"] = view.Number,
                    ["count"] = view.Count,
                    ["pad"] = pad,
                    ["highlight"] = new JArray(view.Highlight.ToArray()),
                    ["checklist"] = new JArray(view.Checklist.Select(c => new JObject
                    {
                        ["title"] = c.Title,
                        ["done"] = c.Done,
                        ["current"] = c.Current,
                    }).ToArray()),
                    ["destination"] = flow.Destination,
                }.ToString(Formatting.None);
            }
            if (message == shown)
            {
                return;
            }
            shown = message;
            SendNuiMessage(message);
        }
        #endregion
    }
}
