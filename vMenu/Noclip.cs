using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CitizenFX.Core;

using MenuAPI;

using static CitizenFX.Core.Native.API;

using static vMenuClient.CommonFunctions;

namespace vMenuClient
{
    public class NoClip : BaseScript
    {
        private static bool NoclipActive { get; set; } = false;
        private static int MovingSpeed { get; set; } = 0;
        private static int Scale = -1;
        private static bool FollowCamMode { get; set; } = true;
        // What the help panel last showed; it is rebuilt only when this changes.
        private static int helpPanelState = -1;


        private readonly List<string> speeds = new()
        {
            "12.5%",
            "25%",
            "37.5%",
            "50%",
            "62.5%",
            "75%",
            "87.5%",
            "100%"
        };

        public NoClip()
        {
            Tick += NoClipHandler;
        }

        internal static void SetNoclipActive(bool active)
        {

            if (!CanDoInteraction("noclip"))
            {
                return;
            }

            NoclipActive = active;
            // Replicated statebag so server-side integrity monitoring (psrp_telemetry)
            // can suppress movement detections during a permitted noclip session.
            // The server still verifies the vMenu.NoClip ace, so a spoofed bag alone
            // grants nothing.
            Game.Player.State.Set("vmenu_noclip_active", NoclipActive, true);
            var actionData = new Dictionary<string, object>
            {
                ["enabled"] = NoclipActive
            };
            TriggerEvent("vMenu:Integrations:Action", "noclip", actionData);

            if (!active)
            {
                SetScaleformMovieAsNoLongerNeeded(ref Scale);

                Scale = -1;
            }
        }

        internal static bool IsNoclipActive()
        {
            return NoclipActive;
        }
        // How a key mapping's current key is shown in instructional buttons.
        private static string KeyMappingButton(string command) => $"~INPUT_{(uint)GetHashKey(command) | 0x80000000:X}~";

        private static void AddHelpButton(int slot, string button, string label)
        {
            BeginScaleformMovieMethod(Scale, "SET_DATA_SLOT");
            ScaleformMovieMethodAddParamInt(slot);
            PushScaleformMovieMethodParameterString(button);
            PushScaleformMovieMethodParameterString(label);
            EndScaleformMovieMethod();
        }

        private void BuildHelpPanel()
        {
            BeginScaleformMovieMethod(Scale, "CLEAR_ALL");
            EndScaleformMovieMethod();

            AddHelpButton(0, "~INPUT_SPRINT~", $"Change Speed ({speeds[MovingSpeed]})");
            AddHelpButton(1, "~INPUT_CURSOR_SCROLL_UP~", "Faster / Slower");
            AddHelpButton(2, "~INPUT_MOVE_LR~", FollowCamMode ? "Strafe Left/Right" : "Turn Left/Right");
            AddHelpButton(3, "~INPUT_MOVE_UD~", "Move");
            AddHelpButton(4, "~INPUT_MULTIPLAYER_INFO~", "Down");
            AddHelpButton(5, "~INPUT_COVER~", "Up");
            AddHelpButton(6, "~INPUT_VEH_HEADLIGHT~", "Cam Mode");
            AddHelpButton(7, KeyMappingButton($"vMenu:{MainMenu.GetKeyMappingId()}:NoClip"), "Toggle NoClip");

            BeginScaleformMovieMethod(Scale, "DRAW_INSTRUCTIONAL_BUTTONS");
            ScaleformMovieMethodAddParamInt(0);
            EndScaleformMovieMethod();
        }

        private async Task NoClipHandler()
        {
            if (NoclipActive)
            {
                Scale = RequestScaleformMovie("INSTRUCTIONAL_BUTTONS");
                while (!HasScaleformMovieLoaded(Scale))
                {
                    await Delay(0);
                }

                DrawScaleformMovieFullscreen(Scale, 255, 255, 255, 0, 0);
                helpPanelState = -1;
            }
            while (NoclipActive)
            {
                if (!IsHudHidden())
                {
                    var panelState = MovingSpeed * 2 + (FollowCamMode ? 1 : 0);
                    if (panelState != helpPanelState)
                    {
                        helpPanelState = panelState;
                        BuildHelpPanel();
                    }
                    DrawScaleformMovieFullscreen(Scale, 255, 255, 255, 255, 0);
                }

                var noclipEntity = Game.PlayerPed.IsInVehicle() ? Game.PlayerPed.CurrentVehicle.Handle : Game.PlayerPed.Handle;

                FreezeEntityPosition(noclipEntity, true);
                SetEntityInvincible(noclipEntity, true);

                Vector3 newPos;
                Game.DisableControlThisFrame(0, Control.MoveUpOnly);
                Game.DisableControlThisFrame(0, Control.MoveUp);
                Game.DisableControlThisFrame(0, Control.MoveUpDown);
                Game.DisableControlThisFrame(0, Control.MoveDown);
                Game.DisableControlThisFrame(0, Control.MoveDownOnly);
                Game.DisableControlThisFrame(0, Control.MoveLeft);
                Game.DisableControlThisFrame(0, Control.MoveLeftOnly);
                Game.DisableControlThisFrame(0, Control.MoveLeftRight);
                Game.DisableControlThisFrame(0, Control.MoveRight);
                Game.DisableControlThisFrame(0, Control.MoveRightOnly);
                Game.DisableControlThisFrame(0, Control.Cover);
                Game.DisableControlThisFrame(0, Control.MultiplayerInfo);
                Game.DisableControlThisFrame(0, Control.VehicleHeadlight);
                // The mouse wheel changes speed, not weapons.
                Game.DisableControlThisFrame(0, Control.WeaponWheelNext);
                Game.DisableControlThisFrame(0, Control.WeaponWheelPrev);
                Game.DisableControlThisFrame(0, Control.SelectNextWeapon);
                Game.DisableControlThisFrame(0, Control.SelectPrevWeapon);
                if (Game.PlayerPed.IsInVehicle())
                {
                    Game.DisableControlThisFrame(0, Control.VehicleRadioWheel);
                    Game.DisableControlThisFrame(0, Control.VehicleSelectNextWeapon);
                    Game.DisableControlThisFrame(0, Control.VehicleSelectPrevWeapon);
                }

                var xoff = 0.0f;
                var yoff = 0.0f;
                var zoff = 0.0f;

                if (Game.CurrentInputMode == InputMode.MouseAndKeyboard && UpdateOnscreenKeyboard() != 0 && !Game.IsPaused)
                {
                    if (Game.IsControlJustPressed(0, Control.Sprint))
                    {
                        MovingSpeed++;
                        if (MovingSpeed == speeds.Count)
                        {
                            MovingSpeed = 0;
                        }
                    }

                    if (!MenuController.IsAnyMenuOpen())
                    {
                        if (Game.IsDisabledControlJustPressed(0, Control.CursorScrollUp))
                        {
                            MovingSpeed = Math.Min(MovingSpeed + 1, speeds.Count - 1);
                        }
                        else if (Game.IsDisabledControlJustPressed(0, Control.CursorScrollDown))
                        {
                            MovingSpeed = Math.Max(MovingSpeed - 1, 0);
                        }
                    }

                    if (Game.IsDisabledControlPressed(0, Control.MoveUpOnly))
                    {
                        yoff = 0.5f;
                    }
                    if (Game.IsDisabledControlPressed(0, Control.MoveDownOnly))
                    {
                        yoff = -0.5f;
                    }
                    // Follow-cam: A/D strafe. Otherwise they turn.
                    if (Game.IsDisabledControlPressed(0, Control.MoveLeftOnly))
                    {
                        if (FollowCamMode)
                        {
                            xoff = -0.5f;
                        }
                        else
                        {
                            SetEntityHeading(Game.PlayerPed.Handle, GetEntityHeading(Game.PlayerPed.Handle) + 3f);
                        }
                    }
                    if (Game.IsDisabledControlPressed(0, Control.MoveRightOnly))
                    {
                        if (FollowCamMode)
                        {
                            xoff = 0.5f;
                        }
                        else
                        {
                            SetEntityHeading(Game.PlayerPed.Handle, GetEntityHeading(Game.PlayerPed.Handle) - 3f);
                        }
                    }
                    if (Game.IsDisabledControlPressed(0, Control.Cover))
                    {
                        zoff = 0.21f;
                    }
                    if (Game.IsDisabledControlPressed(0, Control.MultiplayerInfo))
                    {
                        zoff = -0.21f;
                    }
                    if (Game.IsDisabledControlJustPressed(0, Control.VehicleHeadlight))
                    {
                        FollowCamMode = !FollowCamMode;
                    }
                }
                float moveSpeed = MovingSpeed;
                if (MovingSpeed > speeds.Count / 2)
                {
                    moveSpeed *= 1.8f;
                }
                moveSpeed = moveSpeed / (1f / GetFrameTime()) * 60;
                newPos = GetOffsetFromEntityInWorldCoords(noclipEntity, xoff * (moveSpeed + 0.3f), yoff * (moveSpeed + 0.3f), zoff * (moveSpeed + 0.3f));

                var heading = GetEntityHeading(noclipEntity);
                SetEntityVelocity(noclipEntity, 0f, 0f, 0f);
                SetEntityRotation(noclipEntity, 0f, 0f, 0f, 0, false);
                SetEntityHeading(noclipEntity, FollowCamMode ? GetGameplayCamRelativeHeading() : heading);
                SetEntityCollision(noclipEntity, false, false);
                SetEntityCoordsNoOffset(noclipEntity, newPos.X, newPos.Y, newPos.Z, true, true, true);

                SetEntityVisible(noclipEntity, false, false);
                SetLocalPlayerVisibleLocally(true);
                SetEntityAlpha(noclipEntity, (int)(255 * 0.2), 0);

                SetEveryoneIgnorePlayer(Game.PlayerPed.Handle, true);
                SetPoliceIgnorePlayer(Game.PlayerPed.Handle, true);

                // After the next game tick, reset the entity properties.
                await Delay(0);
                FreezeEntityPosition(noclipEntity, false);
                SetEntityInvincible(noclipEntity, false);
                SetEntityCollision(noclipEntity, true, true);

                // If the player is not set as invisible by PlayerOptions or if the noclip entity is not the player ped, reset the visibility
                if (MainMenu.PlayerOptionsMenu == null || !MainMenu.PlayerOptionsMenu.PlayerInvisible || (MainMenu.PlayerOptionsMenu.PlayerInvisible && noclipEntity == Game.PlayerPed.Handle))
                {
                    SetEntityVisible(noclipEntity, true, false);
                    SetLocalPlayerVisibleLocally(true);
                }

                // Always reset the alpha.
                ResetEntityAlpha(noclipEntity);

                SetEveryoneIgnorePlayer(Game.PlayerPed.Handle, false);
                SetPoliceIgnorePlayer(Game.PlayerPed.Handle, false);
            }

            await Task.FromResult(0);
        }
    }
}
