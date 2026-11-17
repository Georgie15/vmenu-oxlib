using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;
using CitizenFX.Core.UI;

using MenuAPI;

using Newtonsoft.Json;

using vMenuClient.data;
using vMenuClient.menus;

using static CitizenFX.Core.Native.API;
using static CitizenFX.Core.UI.Screen;
using static vMenuClient.CommonFunctions;
using static vMenuClient.data.PedModels;
using static vMenuShared.ConfigManager;
using static vMenuShared.PermissionsManager;

namespace vMenuClient
{
    /// <summary>
    /// This class manages all things that need to be done every tick based on
    /// checkboxes/things changing in any of the (sub) menus.
    /// </summary>
    class FunctionsController : BaseScript
    {
        private int LastVehicle = 0;
        private int radioAppliedVehicle = 0;
        private bool SwitchedVehicle = false;
        private readonly List<int> deadPlayers = new();
        private float cameraRotationHeading = 0f;

        // show location variables
        private float safeZoneSizeX = (1 / GetSafeZoneSize() / 3.0f) - 0.358f;
        private string zoneDisplay = "";
        private string headingDisplay = "";

        private int voiceTimer = 0;
        private int voiceCycle = 1;
        private const float voiceIndicatorWidth = 0.02f;
        private const float voiceIndicatorHeight = 0.041f;
        private const float voiceIndicatorMutedWidth = voiceIndicatorWidth + 0.0021f;
        public const string clothingAnimationDecor = "clothing_animation_type";
        private bool clothingAnimationReverse = false;
        private float clothingOpacity = 1f;

        private const string snowball_anim_dict = "anim@mp_snowball";
        private const string snowball_anim_name = "pickup_snowball";
        private readonly uint snowball_hash = (uint)GetHashKey("weapon_snowball");
        private bool showSnowballInfo = false;

        private bool stopPropsLoop = false;
        private bool stopVehiclesLoop = false;
        private bool stopPedsLoop = false;
        private List<Prop> props = new();
        private List<Vehicle> vehicles = new();
        private List<Ped> peds = new();

        // Settings other resources also write. See OverrideLatch.
        private readonly OverrideLatch godModeLatch = new();
        private readonly OverrideLatch noRagdollLatch = new();
        private readonly OverrideLatch dragOutLatch = new();
        private readonly OverrideLatch shotInVehicleLatch = new();
        private readonly OverrideLatch knockOffBikeLatch = new();
        private readonly OverrideLatch rampGodLatch = new();
        private readonly OverrideLatch visualGodLatch = new();
        private readonly OverrideLatch engineGodLatch = new();
        private readonly OverrideLatch strongWheelsGodLatch = new();
        private readonly OverrideLatch invincibleGodLatch = new();
        private readonly OverrideLatch noHelmetLatch = new();
        private readonly OverrideLatch powerMultiplierLatch = new();
        private readonly OverrideLatch noSirenLatch = new();
        private readonly OverrideLatch planeTurbulenceLatch = new();
        private readonly OverrideLatch heliTurbulenceLatch = new();
        // Target is the weapon hash in hand, not an entity.
        private readonly OverrideLatch unlimitedAmmoLatch = new();
        // The boat vMenu anchored, so entering other boats never releases someone else's anchor.
        private int anchoredBoat = 0;
        private Menu[] vehicleOnlyMenus;

        public FunctionsController() { }

        // Idle rests. FiveM's C# runtime starts each tick handler again every frame once its last
        // run has finished, and that start costs more than most handlers' own checks. A handler
        // that has nothing it must do every frame now ends its run with one of these waits
        // instead, and checks again afterwards. Each handler stays per-frame while its feature is
        // on, and while any menu is open, so toggling an option takes effect on the next frame.
        // Anything a handler still does while resting is a check or a setting that persists, which
        // is now repeated 4 to 10 times a second instead of every frame.
        private const int IdleRestMs = 250;
        // Shorter, for what follows the player getting into a vehicle or dying.
        private const int StateRestMs = 100;

        private static bool AnyMenuOpen => MenuController.IsAnyMenuOpen();

        // A rest ends early when a menu opens (see IdleRest), so a resting loop is back to every
        // frame on the frame after the menu appears.
        private static Task Rest(int ms) => IdleRest.Until(Delay(ms));

        /// <summary>
        /// Applies a vMenu override through its latch: enforced on <paramref name="target"/> while
        /// <paramref name="enabled"/>, and the entity vMenu last changed is restored once when the
        /// option goes off or the target changes. Settings vMenu never changed are left alone.
        /// </summary>
        private static void ApplyOverride(OverrideLatch latch, bool enabled, int target, Action<int> apply, Action<int> restore)
        {
            var applyNow = latch.Update(enabled, target, out var previous);
            if (previous != 0 && DoesEntityExist(previous))
            {
                restore(previous);
            }
            if (applyNow)
            {
                apply(target);
            }
        }

        /// <summary>
        /// Setup the required tick functions
        /// </summary>
        [EventHandler("vMenu:SetupTickFunctions")]
        public void SetupTickFunctions()
        {
            // Always needed
            // The visor, snowball, point, minimap, drift and teleport-to-waypoint keys are FiveM key
            // bindings (see Key bindings below), so nothing polls them every frame.
            RegisterKeyBindings();
            Tick += HudOverlayTick;
            Tick += PlayerClothingAnimationsController;
            Tick += MiscRecordingKeybinds;
            Tick += MiscSettings;
            Tick += RadarSettings;
            Tick += GeneralTasks;

            // Configuration and permissions based
            if (IsAllowed(Permission.WOMenu) && GetSettingsBool(Setting.vmenu_enable_weather_sync))
            {
                Tick += WeatherOptions;
            }
            if (IsAllowed(Permission.TOMenu) && GetSettingsBool(Setting.vmenu_enable_time_sync))
            {
                Tick += TimeOptions;
            }

            // Configuration based
            if (!GetSettingsBool(Setting.vmenu_disable_spawning_as_default_character))
            {
                Tick += RestorePlayerAfterBeingDead;
            }
            if (!GetSettingsBool(Setting.vmenu_disable_entity_outlines_tool))
            {
                Tick += SlowMiscTick;
                Tick += ModelDrawDimensions;
            }

            // Permissions based
            if (IsAllowed(Permission.POMenu) || IsAllowed(Permission.VOMenu))
            {
                Tick += DoPlayerAndVehicleChecks;
            }
            if (IsAllowed(Permission.VOMenu))
            {
                Tick += VehicleOptions;
                vehicleHealthCheckEnabled = true;
                if (IsAllowed(Permission.VOFlashHighbeamsOnHonk))
                {
                    highbeamFlashCheckEnabled = true;
                }
            }
            if (IsAllowed(Permission.VCMenu))
            {
                Tick += VoiceChat;
            }
            if (IsAllowed(Permission.WPMenu))
            {
                Tick += WeaponOptions;
            }
            if (IsAllowed(Permission.MSDeathNotifs))
            {
                Tick += DeathNotifications;
            }
            if (IsAllowed(Permission.MSShowLocation))
            {
                Tick += UpdateLocation;
            }
            if (IsAllowed(Permission.PAMenu))
            {
                Tick += ManageCamera;
                movementLockCheckEnabled = true;
            }
            if (IsAllowed(Permission.MSPlayerBlips))
            {
                Tick += PlayerBlipsControl;
            }
            if (IsAllowed(Permission.MSOverheadNames))
            {
                Tick += PlayerOverheadNamesControl;
            }
            if (IsAllowed(Permission.POMenu))
            {
                Tick += PlayerOptions;
            }
            if (IsAllowed(Permission.WPSnowball))
            {
                Tick += SnowballPickupHelpMessageTask;
            }
            if (IsAllowed(Permission.PVMenu))
            {
                Tick += AutoPVBlipOnExitTick;
            }
            if (IsAllowed(Permission.PVLockDoors))
            {
                Tick += PersonalVehicleOptions;
                Tick += PersonalVehicleAutoLockTick;
            }
            if (GetSettingsBool(Setting.vmenu_enable_animals_spawn_menu))
            {
                animalCameraCheckEnabled = true;
            }
            if (vehicleHealthCheckEnabled || highbeamFlashCheckEnabled || movementLockCheckEnabled || animalCameraCheckEnabled)
            {
                Tick += PerFrameChecks;
            }
        }

        /// Task related
        #region General Tasks
        /// <summary>
        /// All general tasks that run every 1 game ticks (and are not (sub)menu specific).
        /// </summary>
        /// <returns></returns>
        private async Task GeneralTasks()
        {
            // Check if the player has switched to a new vehicle.
            // PlayerPedId() is one game call; Game.PlayerPed is two (PlayerId, then GetPlayerPed).
            var ped = PlayerPedId();
            var seated = IsPedInAnyVehicle(ped, false);
            if (seated) // added this for improved performance.
            {
                var tmpVehicle = GetVehicle();
                if (tmpVehicle != null && tmpVehicle.Exists() && tmpVehicle.Handle != LastVehicle)
                {
                    // Set the last vehicle to the new vehicle entity.
                    LastVehicle = tmpVehicle.Handle;
                    SwitchedVehicle = true;

                    if (IsAllowed(Permission.PVMenu) && MainMenu.PermissionsSetupComplete && MainMenu.PersonalVehicleMenu != null && Game.PlayerPed == tmpVehicle.Driver)
                    {
                        MainMenu.PersonalVehicleMenu.SetCurrentPersonalVehicle(tmpVehicle);
                    }
                }
            }
            // The player's radio choices (Vehicle Options), each time they become a vehicle's driver:
            // getting in, getting back in, or moving over from a passenger seat.
            var driving = seated ? GetVehiclePedIsIn(ped, false) : 0;
            if (driving != 0 && GetPedInVehicleSeat(driving, -1) == ped)
            {
                if (driving != radioAppliedVehicle)
                {
                    radioAppliedVehicle = driving;
                    VehicleRadio.ApplyTo(driving);
                }
            }
            else
            {
                radioAppliedVehicle = 0;
            }
            // Checked 10 times a second, seated or not (vMenu Enhanced polls the seat 4 times a
            // second). A new vehicle is seen within 100 ms of sitting in it, and VehicleOptions
            // applies its switch settings (power multiplier, no siren, turbulence, anchor) on its
            // next pass.
            await Rest(StateRestMs);
        }
        #endregion

        #region Player Options Tasks
        /// <summary>
        /// Run all tasks for the Player Options menu.
        /// </summary>
        /// <returns></returns>
        private async Task PlayerOptions()
        {
            // perms
            var godmodeAllowed = IsAllowed(Permission.POGod);
            var noRagdollAllowed = IsAllowed(Permission.PONoRagdoll);
            var playerPed = PlayerPedId();

            if (MainMenu.MpPedCustomizationMenu != null && MainMenu.MpPedCustomizationMenu.appearanceMenu != null && MainMenu.MpPedCustomizationMenu.faceShapeMenu != null && MainMenu.MpPedCustomizationMenu.createCharacterMenu != null && MainMenu.MpPedCustomizationMenu.inheritanceMenu != null && MainMenu.MpPedCustomizationMenu.propsMenu != null && MainMenu.MpPedCustomizationMenu.clothesMenu != null && MainMenu.MpPedCustomizationMenu.tattoosMenu != null)
            {
                // Manage Player God Mode
                static bool IsMpPedCreatorOpen()
                {
                    return
                        MainMenu.MpPedCustomizationMenu.appearanceMenu.Visible ||
                        MainMenu.MpPedCustomizationMenu.faceShapeMenu.Visible ||
                        MainMenu.MpPedCustomizationMenu.createCharacterMenu.Visible ||
                        MainMenu.MpPedCustomizationMenu.inheritanceMenu.Visible ||
                        MainMenu.MpPedCustomizationMenu.propsMenu.Visible ||
                        MainMenu.MpPedCustomizationMenu.clothesMenu.Visible ||
                        MainMenu.MpPedCustomizationMenu.tattoosMenu.Visible;
                }
                if (!IsMpPedCreatorOpen())
                {
                    ApplyOverride(godModeLatch, MainMenu.PlayerOptionsMenu.PlayerGodMode && godmodeAllowed, playerPed,
                        ped => SetEntityInvincible(ped, true), ped => SetEntityInvincible(ped, false));
                }
            }

            // Manage PlayerInvisible
            if (MainMenu.PlayerOptionsMenu.PlayerInvisible && IsAllowed(Permission.POInvisible) && GetSettingsBool(Setting.vmenu_handle_invisibility))
            {
                SetEntityVisible(playerPed, false, false);
            }

            // Manage Super jump.
            if (MainMenu.PlayerOptionsMenu.PlayerSuperJump && IsAllowed(Permission.POSuperjump))
            {
                SetSuperJumpThisFrame(Game.Player.Handle);
            }

            // Manage PlayerNoRagdoll
            ApplyOverride(noRagdollLatch, MainMenu.PlayerOptionsMenu.PlayerNoRagdoll && noRagdollAllowed, playerPed,
                ped => SetPedCanRagdoll(ped, false), ped => SetPedCanRagdoll(ped, true));


            // Manage never wanted.
            if (MainMenu.PlayerOptionsMenu.PlayerNeverWanted && GetPlayerWantedLevel(Game.Player.Handle) > 0 && IsAllowed(Permission.PONeverWanted))
            {
                ClearPlayerWantedLevel(Game.Player.Handle);
                if (GetMaxWantedLevel() > 0)
                {
                    SetMaxWantedLevel(0);
                }
            }

            if (DriveToWpTaskActive || DriveWanderTaskActive)
            {
                var activeOrg = Game.Player.State["activeOrg"];
                if (activeOrg is string dutyOrg && !string.IsNullOrWhiteSpace(dutyOrg))
                {
                    ClearPedTasks(Game.PlayerPed.Handle);
                    DriveToWpTaskActive = false;
                    DriveWanderTaskActive = false;
                    Notify.Error("Auto pilot cancelled because you went on duty.");
                }
            }

            if (DriveToWpTaskActive && !Game.IsWaypointActive)
            {
                ClearPedTasks(Game.PlayerPed.Handle);
                Notify.Custom("Destination reached, the car will now stop driving!");
                DriveToWpTaskActive = false;
            }

            // Every frame while an option here is enforced or watched every frame; otherwise the
            // god mode and no ragdoll latches only have something to restore, which can wait.
            var everyFrame = AnyMenuOpen
                || (MainMenu.PlayerOptionsMenu.PlayerGodMode && godmodeAllowed)
                || (MainMenu.PlayerOptionsMenu.PlayerNoRagdoll && noRagdollAllowed)
                || MainMenu.PlayerOptionsMenu.PlayerInvisible
                || MainMenu.PlayerOptionsMenu.PlayerSuperJump
                // Never Wanted defaults to on, also for players without the permission, who
                // can't see the option. Without it the check above does nothing.
                || (MainMenu.PlayerOptionsMenu.PlayerNeverWanted && IsAllowed(Permission.PONeverWanted))
                || DriveToWpTaskActive
                || DriveWanderTaskActive;
            if (!everyFrame)
            {
                await Rest(IdleRestMs);
            }
        }
        #endregion

        #region shared player options and vehicle options
        /// <summary>
        /// Slow tick that does some basic checks for shared vehicle/player options.
        /// </summary>
        /// <returns></returns>
        private async Task DoPlayerAndVehicleChecks()
        {
            var god = IsAllowed(Permission.POGod) && MainMenu.PlayerOptionsMenu != null && MainMenu.PlayerOptionsMenu.PlayerGodMode;
            await Delay(100);

            var vehGod = IsAllowed(Permission.VOGod) && MainMenu.VehicleOptionsMenu != null && MainMenu.VehicleOptionsMenu.VehicleGodMode;
            await Delay(100);

            var ignored = IsAllowed(Permission.POIgnored) && MainMenu.PlayerOptionsMenu != null && MainMenu.PlayerOptionsMenu.PlayerIsIgnored;
            await Delay(100);

            var stayInVeh = IsAllowed(Permission.POStayInVehicle) && MainMenu.PlayerOptionsMenu != null && MainMenu.PlayerOptionsMenu.PlayerStayInVehicle;
            await Delay(100);

            var bikeSeatbelt = IsAllowed(Permission.VOBikeSeatbelt) && MainMenu.VehicleOptionsMenu != null && MainMenu.VehicleOptionsMenu.VehicleBikeSeatbelt;
            await Delay(100);

            var noRagdoll = IsAllowed(Permission.PONoRagdoll) && MainMenu.PlayerOptionsMenu != null && MainMenu.PlayerOptionsMenu.PlayerNoRagdoll;
            await Delay(100);

            var cantBeKnockedOff = god || vehGod || bikeSeatbelt || noRagdoll;
            var cantBeDraggedOut = god || vehGod || ignored || stayInVeh;
            var cantBeShotInVehicle = god || vehGod;

            var playerPed = Game.PlayerPed.Handle;
            ApplyOverride(dragOutLatch, cantBeDraggedOut, playerPed,
                ped => new Ped(ped).CanBeDraggedOutOfVehicle = false, ped => new Ped(ped).CanBeDraggedOutOfVehicle = true);
            ApplyOverride(shotInVehicleLatch, cantBeShotInVehicle, playerPed,
                ped => new Ped(ped).CanBeShotInVehicle = false, ped => new Ped(ped).CanBeShotInVehicle = true);
            ApplyOverride(knockOffBikeLatch, cantBeKnockedOff, playerPed,
                ped => new Ped(ped).CanBeKnockedOffBike = false, ped => new Ped(ped).CanBeKnockedOffBike = true);
            await Delay(1000);
        }
        #endregion

        #region Vehicle Options Tasks
        /// <summary>
        /// Vehicle god mode settings. <paramref name="vehicle"/> is 0 while on foot, which keeps
        /// god mode on the last vehicle until the option is turned off or another vehicle is entered.
        /// </summary>
        private void UpdateVehicleGodOverrides(int vehicle)
        {
            var god = MainMenu.VehicleOptionsMenu.VehicleGodMode && IsAllowed(Permission.VOGod);

            ApplyOverride(rampGodLatch, MainMenu.VehicleOptionsMenu.VehicleGodRamp && god, vehicle,
                veh => SetRampVehicleReceivesRampDamage(veh, false), veh => SetRampVehicleReceivesRampDamage(veh, true));
            ApplyOverride(visualGodLatch, MainMenu.VehicleOptionsMenu.VehicleGodVisual && god, vehicle,
                veh => new Vehicle(veh).CanBeVisiblyDamaged = false, veh => new Vehicle(veh).CanBeVisiblyDamaged = true);
            ApplyOverride(engineGodLatch, MainMenu.VehicleOptionsMenu.VehicleGodEngine && god, vehicle,
                veh => new Vehicle(veh).CanEngineDegrade = false, veh => new Vehicle(veh).CanEngineDegrade = true);
            ApplyOverride(strongWheelsGodLatch, MainMenu.VehicleOptionsMenu.VehicleGodStrongWheels && god, vehicle,
                veh => SetVehicleGodStrongWheels(new Vehicle(veh), true), veh => SetVehicleGodStrongWheels(new Vehicle(veh), false));
            ApplyOverride(invincibleGodLatch, MainMenu.VehicleOptionsMenu.VehicleGodInvincible && god, vehicle,
                veh => SetVehicleGodInvincible(new Vehicle(veh), true), veh => SetVehicleGodInvincible(new Vehicle(veh), false));
        }

        private static void SetVehicleGodStrongWheels(Vehicle vehicle, bool strong)
        {
            vehicle.CanWheelsBreak = !strong;
            vehicle.IsAxlesStrong = strong;
        }

        private static void SetVehicleGodInvincible(Vehicle vehicle, bool invincible)
        {
            vehicle.IsBulletProof = invincible;
            vehicle.IsCollisionProof = invincible;
            vehicle.IsExplosionProof = invincible;
            vehicle.IsFireProof = invincible;
            vehicle.IsInvincible = invincible;
            vehicle.IsMeleeProof = invincible;

            foreach (var vehicleDoor in vehicle.Doors.GetAll())
            {
                vehicleDoor.CanBeBroken = !invincible;
            }
        }

        /// <summary>
        /// Manage all vehicle related tasks.
        /// </summary>
        /// <returns></returns>
        private async Task VehicleOptions()
        {
            // No helmet applies to the ped, so it is kept up to date on foot too.
            var noHelmet = MainMenu.VehicleOptionsMenu.VehicleNoBikeHelemet && IsAllowed(Permission.VONoHelmet);
            var playerPed = PlayerPedId();
            ApplyOverride(noHelmetLatch, noHelmet, playerPed,
                ped => new Ped(ped).CanWearHelmet = false, ped => new Ped(ped).CanWearHelmet = true);

            // When the player is in a valid vehicle:
            var inVehicle = IsPedInAnyVehicle(playerPed, true);
            if (inVehicle)
            {
                var veh = GetVehicle();
                if (veh != null && veh.Exists())
                {
                    // God mode
                    var god = MainMenu.VehicleOptionsMenu.VehicleGodMode && IsAllowed(Permission.VOGod);
                    var visualGod = MainMenu.VehicleOptionsMenu.VehicleGodVisual && god;
                    var engineGod = MainMenu.VehicleOptionsMenu.VehicleGodEngine && god;
                    var autoRepairGod = MainMenu.VehicleOptionsMenu.VehicleGodAutoRepair && god;

                    UpdateVehicleGodOverrides(veh.Handle);

                    if (visualGod && IsVehicleDamaged(veh.Handle))
                    {
                        RemoveDecalsFromVehicle(veh.Handle);
                    }

                    if (autoRepairGod && IsVehicleDamaged(veh.Handle) && GetEntitySpeed(veh.Handle) <= 8.9408f)
                    {
                        veh.Repair();
                    }

                    if (engineGod && veh.EngineHealth < 1000f)
                    {
                        veh.EngineHealth = 1000f;
                    }

                    // Freeze Vehicle Position (if enabled).
                    if (MainMenu.VehicleOptionsMenu.VehicleFrozen && IsAllowed(Permission.VOFreeze))
                    {
                        FreezeEntityPosition(veh.Handle, true);
                    }

                    if (MainMenu.VehicleOptionsMenu.VehicleNeverDirty && veh.DirtLevel > 0f && IsAllowed(Permission.VOKeepClean))
                    {
                        veh.Wash();
                    }

                    // If the torque multiplier is enabled and the player is allowed to use it.
                    if (MainMenu.VehicleOptionsMenu.VehicleTorqueMultiplier && IsAllowed(Permission.VOTorqueMultiplier))
                    {
                        // Set the torque multiplier to the selected value by the player.
                        // no need for an "else" to reset this value, because when it's not called every frame, nothing happens.
                        SetVehicleEngineTorqueMultiplier(veh.Handle, MainMenu.VehicleOptionsMenu.VehicleTorqueMultiplierAmount);
                    }
                    // If the player has switched to a new vehicle, and the vehicle engine power multiplier is turned on. Set the new value.
                    if (SwitchedVehicle)
                    {
                        // Only needs to be set once.
                        SwitchedVehicle = false;

                        // Get the license plate type index.

                        // Set the license plate index list item to the correct index.
                        if (IsAllowed(Permission.VOChangePlate) && MainMenu.VehicleOptionsMenu.GetMenu().GetMenuItems().Find(mi => mi is MenuListItem li && li.ListItems.Any(liText => liText == GetLabelText("CMOD_PLA_0"))) is MenuListItem listItem)
                        {
                            // Set the license plate style.
                            switch (veh.Mods.LicensePlateStyle)
                            {
                                case LicensePlateStyle.BlueOnWhite1:
                                    listItem.ListIndex = 0;
                                    break;
                                case LicensePlateStyle.YellowOnBlack:
                                    listItem.ListIndex = 1;
                                    break;
                                case LicensePlateStyle.YellowOnBlue:
                                    listItem.ListIndex = 2;
                                    break;
                                case LicensePlateStyle.BlueOnWhite2:
                                    listItem.ListIndex = 3;
                                    break;
                                case LicensePlateStyle.BlueOnWhite3:
                                    listItem.ListIndex = 4;
                                    break;
                                case LicensePlateStyle.NorthYankton:
                                    listItem.ListIndex = 5;
                                    break;
                                case LicensePlateStyle.ECola:
                                    listItem.ListIndex = 6;
                                    break;
                                case LicensePlateStyle.LasVenturas:
                                    listItem.ListIndex = 7;
                                    break;
                                case LicensePlateStyle.LibertyCity:
                                    listItem.ListIndex = 8;
                                    break;
                                case LicensePlateStyle.LSCarMeet:
                                    listItem.ListIndex = 9;
                                    break;
                                case LicensePlateStyle.LSPanic:
                                    listItem.ListIndex = 10;
                                    break;
                                case LicensePlateStyle.LSPounders:
                                    listItem.ListIndex = 11;
                                    break;
                                case LicensePlateStyle.Sprunk:
                                    listItem.ListIndex = 12;
                                    break;
                                default:
                                    break;
                            }
                        }

                        // Vehicle engine power multiplier. Set once the player switched vehicles.
                        // Only vehicles vMenu boosted are reset, so other resources' multipliers are kept.
                        ApplyOverride(powerMultiplierLatch, MainMenu.VehicleOptionsMenu.VehiclePowerMultiplier && IsAllowed(Permission.VOPowerMultiplier), veh.Handle,
                            v => SetVehicleEnginePowerMultiplier(v, MainMenu.VehicleOptionsMenu.VehiclePowerMultiplierAmount), v => SetVehicleEnginePowerMultiplier(v, 1f));

                        // disable this if els compatibility is turned on.
                        if (!GetSettingsBool(Setting.vmenu_use_els_compatibility_mode))
                        {
                            // No Siren Toggle
                            ApplyOverride(noSirenLatch, MainMenu.VehicleOptionsMenu.VehicleNoSiren && IsAllowed(Permission.VONoSiren), veh.Handle,
                                v => new Vehicle(v).IsSirenSilent = true, v => new Vehicle(v).IsSirenSilent = false);
                        }

                        // Set the plane / helicopter turbulence in case the vehicle was changed:
                        ApplyOverride(planeTurbulenceLatch, MainMenu.VehicleOptionsMenu.DisablePlaneTurbulence && IsAllowed(Permission.VODisableTurbulence), veh.Model.IsPlane ? veh.Handle : 0,
                            v => SetPlaneTurbulenceMultiplier(v, 0f), v => SetPlaneTurbulenceMultiplier(v, 1.0f));
                        ApplyOverride(heliTurbulenceLatch, MainMenu.VehicleOptionsMenu.DisableHelicopterTurbulence && IsAllowed(Permission.VODisableTurbulence), veh.Model.IsHelicopter ? veh.Handle : 0,
                            v => SetHeliTurbulenceScalar(v, 0f), v => SetHeliTurbulenceScalar(v, 1.0f));

                        if (veh.Model.IsBoat)
                        {
                            if (MainMenu.VehicleOptionsMenu.AnchorBoat && IsAllowed(Permission.VOAnchorBoat) && CanAnchorBoatHere(veh.Handle))
                            {
                                SetBoatAnchor(veh.Handle, true);
                                SetBoatFrozenWhenAnchored(veh.Handle, true);
                                SetForcedBoatLocationWhenAnchored(veh.Handle, true);
                                anchoredBoat = veh.Handle;
                            }
                            // Only release an anchor vMenu set.
                            else if (veh.Handle == anchoredBoat)
                            {
                                SetBoatAnchor(veh.Handle, false);
                                SetBoatFrozenWhenAnchored(veh.Handle, false);
                                SetForcedBoatLocationWhenAnchored(veh.Handle, false);
                                anchoredBoat = 0;
                            }
                        }
                    }

                    // If the player is still wearing a helmet, even if the option is set to: no helmet, then remove the helmet.
                    var ped = Game.PlayerPed;
                    if (ped.IsWearingHelmet && noHelmet)
                    {
                        ped.RemoveHelmet(true);
                    }

                    if (MainMenu.VehicleOptionsMenu.VehicleInfiniteFuel && DecorIsRegisteredAsType("_Fuel_Level", 1) && IsAllowed(Permission.VOInfiniteFuel))
                    {
                        var maxFuelLevel = GetVehicleHandlingFloat(veh.Handle, "CHandlingData", "fPetrolTankVolume");
                        var currentFuelLevel = GetVehicleFuelLevel(veh.Handle);
                        if (maxFuelLevel > 5f && currentFuelLevel < (maxFuelLevel * 0.95f))
                        {
                            try
                            {
                                DecorSetFloat(veh.Handle, "_Fuel_Level", maxFuelLevel);
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(@"[CRITICAL] A critical bug in one of your scripts was detected. vMenu is unable to set or register a decorator's value because another resource has already registered 1.5k or more decorators. vMenu will NOT work as long as this bug in your other scripts is unsolved. Please fix your other scripts. This is *NOT* caused by or fixable by vMenu!!!");
                                Debug.WriteLine($"Error Location: {e.StackTrace}\nError info: {e.Message}");
                                await Delay(1000);
                            }
                        }
                    }
                    await Delay(0);
                }
            }
            // When the player is not inside a vehicle:
            else
            {
                UpdateVehicleGodOverrides(0);

                // Make the vehicle visible again if vMenu hid it, otherwise you could not get back in.
                // Only looked up while vMenu has a vehicle hidden; no existing vehicle has handle 0.
                if (menus.VehicleOptions.HiddenVehicle != 0)
                {
                    var lastVehicle = GetVehicle(true);
                    if (lastVehicle != null && lastVehicle.Exists() && lastVehicle.Handle == menus.VehicleOptions.HiddenVehicle)
                    {
                        if (!lastVehicle.IsVisible)
                        {
                            lastVehicle.IsVisible = true;
                        }
                        menus.VehicleOptions.HiddenVehicle = 0;
                    }
                }

                vehicleOnlyMenus ??= new[]
                    {
                        MainMenu.VehicleOptionsMenu.DeleteConfirmMenu,
                        MainMenu.VehicleOptionsMenu.VehicleColorsMenu,
                        MainMenu.VehicleOptionsMenu.VehicleComponentsMenu,
                        MainMenu.VehicleOptionsMenu.VehicleDoorsMenu,
                        MainMenu.VehicleOptionsMenu.VehicleLiveriesMenu,
                        MainMenu.VehicleOptionsMenu.VehicleModMenu,
                        MainMenu.VehicleOptionsMenu.VehicleUnderglowMenu,
                        MainMenu.VehicleOptionsMenu.VehicleWindowsMenu,
                    };
                foreach (var m in vehicleOnlyMenus)
                {
                    if (m.Visible)
                    {
                        m.GoBack();
                        Notify.Error(CommonErrors.NoVehicle, placeholderValue: "to access this menu");
                    }
                }
            }

            // Every frame only while something above acts every frame: in a vehicle, an option that
            // must be reapplied each frame, or a vehicle switch still to handle. Otherwise everything
            // above only restores or checks, so it rests for 100 ms, in a vehicle too (vMenu Enhanced
            // polls vehicle state every 100-250 ms). Engine always on keeps its timing.
            var engineAlwaysOn = MainMenu.VehicleOptionsMenu.VehicleEngineAlwaysOn && IsAllowed(Permission.VOEngineAlwaysOn);
            if (AnyMenuOpen || engineAlwaysOn || (inVehicle && VehicleOptionsEveryFrame()))
            {
                await Delay(1);
            }
            else
            {
                await Rest(StateRestMs);
            }

            // Manage vehicle engine always on.
            if (MainMenu.VehicleOptionsMenu.VehicleEngineAlwaysOn && GetVehicle(true) != null && GetVehicle(true).Exists() && !Game.PlayerPed.IsInVehicle() && IsAllowed(Permission.VOEngineAlwaysOn))
            {
                await Delay(100);
                if (GetVehicle(true) != null)
                {
                    SetVehicleEngineOn(GetVehicle(true).Handle, true, true, true);
                }
            }
            await Task.FromResult(0);
        }

        /// <summary>
        /// Whether VehicleOptions must run every frame while you are in a vehicle: a vehicle switch
        /// is waiting to be handled, or an option is on that the game needs reapplied each frame
        /// (god mode, freeze, keep clean, torque, no helmet, infinite fuel). Each is checked with the
        /// same permission as where it is applied.
        /// </summary>
        private bool VehicleOptionsEveryFrame()
        {
            var options = MainMenu.VehicleOptionsMenu;
            return SwitchedVehicle
                || (options.VehicleGodMode && IsAllowed(Permission.VOGod))
                || (options.VehicleFrozen && IsAllowed(Permission.VOFreeze))
                || (options.VehicleNeverDirty && IsAllowed(Permission.VOKeepClean))
                || (options.VehicleTorqueMultiplier && IsAllowed(Permission.VOTorqueMultiplier))
                || (options.VehicleNoBikeHelemet && IsAllowed(Permission.VONoHelmet))
                || (options.VehicleInfiniteFuel && IsAllowed(Permission.VOInfiniteFuel));
        }

        /// <summary>
        /// Vehicle control options for flashing highbeams. Runs every frame, from PerFrameChecks.
        /// </summary>
        private void VehicleHighbeamFlash()
        {
            if (MainMenu.VehicleOptionsMenu.FlashHighbeamsOnHonk && IsPedInAnyVehicle(Game.PlayerPed.Handle, true))
            {
                var veh = GetVehicle();
                if (veh != null && veh.Exists() && !veh.IsDead)
                {
                    if (veh.Driver == Game.PlayerPed && veh.IsEngineRunning && !IsPauseMenuActive())
                    {
                        // turn on high beams when honking.
                        if (Game.IsControlPressed(0, Control.VehicleHorn))
                        {
                            veh.AreHighBeamsOn = true;
                        }
                        // turn high beams back off when just stopped honking.
                        if (Game.IsControlJustReleased(0, Control.VehicleHorn))
                        {
                            veh.AreHighBeamsOn = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Shows the vehicle health on screen. Runs every frame, from PerFrameChecks.
        /// </summary>
        private void VehicleShowHealthOnScreen()
        {
            if (MainMenu.VehicleOptionsMenu.VehicleShowHealth)
            {
                var veh = GetVehicle();
                if (veh != null && veh.Exists())
                {
                    static string GetHealthString(double health)
                    {
                        string color;
                        if (health <= 0)
                        {
                            color = "~r~";
                        }
                        else
                        {
                            color = (double)Math.Floor(Map(health, 0, 1000, 0, 4)) switch
                            {
                                0 => "~r~",
                                1 => "~o~",
                                2 => "~y~",
                                _ => "~g~",
                            };
                        }
                        return $"{color}{health}";
                    }
                    DrawTextOnScreen($"~n~Engine health: {GetHealthString(Math.Round(veh.EngineHealth, 2))}", 0.5f, 0.0f);
                    DrawTextOnScreen($"~n~~n~Body health: {GetHealthString(Math.Round(veh.BodyHealth, 2))}", 0.5f, 0.0f);
                    DrawTextOnScreen($"~n~~n~~n~Tank health: {GetHealthString(Math.Round(veh.PetrolTankHealth, 2))}", 0.5f, 0.0f);
                }
            }
        }
        #endregion

        #region Weather Options
        private async Task WeatherOptions()
        {
            await Delay(100);
            var weatherMenu = MainMenu.WeatherOptionsMenu.GetMenu();
            if (weatherMenu != null && weatherMenu.Visible)
            {
                if (IsAllowed(Permission.WODynamic))
                {
                    MainMenu.WeatherOptionsMenu.dynamicWeatherEnabled.Checked = EventManager.DynamicWeatherEnabled;
                }
                if (IsAllowed(Permission.WOBlackout))
                {
                    MainMenu.WeatherOptionsMenu.blackout.Checked = EventManager.IsBlackoutEnabled;
                }
                if (IsAllowed(Permission.WOSetWeather))
                {
                    MainMenu.WeatherOptionsMenu.snowEnabled.Checked = EventManager.IsSnowEnabled;
                    weatherMenu.GetMenuItems().ForEach(it =>
                    {
                        if (it.ItemData is string weatherType)
                        {
                            if (weatherType == EventManager.GetServerWeather)
                            {
                                it.RightIcon = MenuItem.Icon.TICK;
                            }
                            else
                            {
                                it.RightIcon = MenuItem.Icon.NONE;
                            }
                        }
                    });
                }
            }
        }
        #endregion

        #region Misc Settings Menu Tasks

        #region On-screen text (coordinates, location, time, speedometer)
        // vMenu Enhanced shows text like this on its web page, updated a few times a second, instead
        // of drawing it with game calls every frame. MenuAPI 4 already gives vMenu that page
        // (storage.html), so this loop sends what to show to it: at most 10 times a second and
        // only when something changed, and nothing at all while every one of these options is off.
        // Positions and sizes use the same screen fractions and text heights the game drawing used.
        private string lastHudMessage;
        private bool locationNear;
        private string locationMain = "";
        private string locationCross = "";

        private async Task HudOverlayTick()
        {
            var misc = MainMenu.MiscSettingsMenu;
            var coordsOn = misc != null && misc.ShowCoordinates && IsAllowed(Permission.MSShowCoordinates);
            var locationOn = misc != null && misc.ShowLocation && IsAllowed(Permission.MSShowLocation);
            var timeOn = misc != null && misc.DrawTimeOnScreen;
            var kmhOn = misc != null && misc.ShowSpeedoKmh;
            var mphOn = misc != null && misc.ShowSpeedoMph;

            if (!(coordsOn || locationOn || timeOn || kmhOn || mphOn))
            {
                if (lastHudMessage != null)
                {
                    SendNuiMessage("{\"action\":\"vmenuHud\",\"visible\":false}");
                    lastHudMessage = null;
                }
                // Checked 4 times a second, or every frame while a menu is open, so switching one
                // on shows it at once.
                if (!AnyMenuOpen)
                {
                    await Rest(IdleRestMs);
                }
                return;
            }

            var ped = PlayerPedId();
            // The game drawing was not shown over the pause menu or a fade either.
            var visible = !IsPauseMenuActive() && IsScreenFadedIn() && !IsPlayerSwitchInProgress() && !IsHudHidden();
            var safeZone = GetSafeZoneSize();
            // The game drawing was aligned to the top of the safe zone (SetScriptGfxAlign, 'T').
            var top = (1f - safeZone) / 2f;

            object coords = null;
            if (coordsOn)
            {
                var pos = GetEntityCoords(ped, true);
                coords = new
                {
                    x = Math.Round(pos.X, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    y = Math.Round(pos.Y, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    z = Math.Round(pos.Z, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    heading = Math.Round(GetEntityHeading(ped), 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    top,
                    h = GetTextScaleHeight(0.5f, 6),
                };
            }

            object location = null;
            if (locationOn)
            {
                location = new
                {
                    near = locationNear,
                    main = locationMain,
                    cross = locationCross,
                    zone = zoneDisplay,
                    heading = headingDisplay,
                    x = 0.234f + safeZoneSizeX,
                    headingX = 0.208f + safeZoneSizeX,
                    streetY = top + safeZone - GetTextScaleHeight(0.48f, 6) - GetTextScaleHeight(0.48f, 6),
                    zoneY = top + safeZone - GetTextScaleHeight(0.45f, 6) - GetTextScaleHeight(0.95f, 6),
                    headingY = top + safeZone - GetTextScaleHeight(1.2f, 6) - GetTextScaleHeight(0.4f, 6),
                    streetH = GetTextScaleHeight(0.48f, 6),
                    zoneH = GetTextScaleHeight(0.45f, 6),
                    headingH = GetTextScaleHeight(1.2f, 6),
                };
            }

            object time = null;
            if (timeOn)
            {
                var day = World.CurrentDayTime;
                time = new
                {
                    text = $"{day.Hours:00}:{day.Minutes:00}",
                    x = 0.208f + safeZoneSizeX,
                    y = top + safeZone - GetTextScaleHeight(0.4f, 1),
                    h = GetTextScaleHeight(0.4f, 6),
                };
            }

            object speed = null;
            var vehicle = GetVehiclePedIsIn(ped, false);
            if ((kmhOn || mphOn) && vehicle != 0)
            {
                var metres = GetEntitySpeed(vehicle);
                speed = new
                {
                    kmh = kmhOn ? (int)Math.Round(metres * 3.6f) : (int?)null,
                    mph = mphOn ? (int)Math.Round(metres * 2.23694f) : (int?)null,
                    h = GetTextScaleHeight(0.7f, 4),
                };
            }

            var message = JsonConvert.SerializeObject(new { action = "vmenuHud", visible, coords, location, time, speed });
            if (message != lastHudMessage)
            {
                SendNuiMessage(message);
                lastHudMessage = message;
            }
            await Delay(100);
        }
        #endregion

        #region Update Location for location display
        /// <summary>
        /// Updates the location for location display.
        /// </summary>
        /// <returns></returns>
        private async Task UpdateLocation()
        {
            if (MainMenu.MiscSettingsMenu.ShowLocation)
            {
                // Get the current location.
                var currentPos = GetEntityCoords(Game.PlayerPed.Handle, true);
                var heading = Game.PlayerPed.Heading;
                zoneDisplay = World.GetZoneLocalizedName(currentPos);

                // Get the nearest vehicle node.
                var nodePos = Vector3.Zero;
                GetNthClosestVehicleNode(currentPos.X, currentPos.Y, currentPos.Z, 0, ref nodePos, 0, 0, 0);

                // Get the safezone size for x and y to be able to move with the minimap.
                safeZoneSizeX = (1 / GetSafeZoneSize() / 3.0f) - 0.358f;

                // Get the cross road.
                uint mainSt = 0, crossSt = 0;
                GetStreetNameAtCoord(currentPos.X, currentPos.Y, currentPos.Z, ref mainSt, ref crossSt);
                var mainName = GetStreetNameFromHashKey(mainSt);
                var crossName = GetStreetNameFromHashKey(crossSt);

                // The street, "Near" when far from a road, and the crossing, for the web page.
                locationNear = currentPos.DistanceToSquared(nodePos) > 1400f;
                locationMain = mainName;
                locationCross = crossSt != 0 ? crossName : "";

                if (heading is > 320 or < 45) // North
                {
                    headingDisplay = "N";
                }
                else if (heading is >= 45 and <= 135) // West
                {
                    headingDisplay = "W";
                }
                else if (heading is > 135 and < 225) // South
                {
                    headingDisplay = "S";
                }
                else // East
                {
                    headingDisplay = "E";
                }

                await Delay(200);
            }
            else
            {
                await Delay(1000);
            }
        }
        #endregion

        #region Main misc settings function
        int radarSwitchTimer = 0;
        // Whether vMenu opened the expanded radar; one another script opened is left alone.
        bool vMenuOpenedBigmap = false;
        /// <summary>
        /// Run all tasks that need to be handeled for the Misc Settings Menu.
        /// </summary>
        /// <returns></returns>
        // Pause menu > settings > display > expanded radar, read by RadarSettings.
        private bool expandedRadarProfile;

        /// <summary>
        /// The radar settings that persist once set: the pause menu's "show radar" preference and
        /// its expanded radar option. MiscSettings applied the first and read the second every
        /// frame; 4 times a second is enough for a setting changed in the pause menu.
        /// </summary>
        private async Task RadarSettings()
        {
            if (MainMenu.MiscSettingsMenu != null)
            {
                // Show radar (or hide it if the user disabled it in pausemenu > settings > display > show radar.
                // Hide radar is enforced every frame by MiscSettings instead.
                if (!MainMenu.MiscSettingsMenu.HideRadar && !IsRadarHidden()) // this should allow other resources to still disable it
                {
                    DisplayRadar(IsRadarPreferenceSwitchedOn());
                }
                expandedRadarProfile = GetProfileSetting(221) == 1; // 221 = settings > display > expanded radar
            }
            // Every frame while a menu is open, as before, so unticking Hide Radar or leaving the
            // character creator follows the preference on the next frame.
            if (!AnyMenuOpen)
            {
                await Rest(IdleRestMs);
            }
        }

        private async Task MiscSettings()
        {
            // Coordinates, location, time and speed are on the web page now (HudOverlayTick). With both
            // speeds shown, MPH sits where the game's street name is, so that is hidden every frame, as before.
            var bothSpeeds = MainMenu.MiscSettingsMenu.ShowSpeedoKmh && MainMenu.MiscSettingsMenu.ShowSpeedoMph;
            if (bothSpeeds && IsPedInAnyVehicle(PlayerPedId(), false))
            {
                HideHudComponentThisFrame((int)HudComponent.StreetName);
            }

            #region Misc Settings
            // Hide radar. Showing it again follows the pause menu preference, in RadarSettings.
            if (MainMenu.MiscSettingsMenu.HideRadar)
            {
                DisplayRadar(false);
            }
            #endregion

            #region camera angle locking
            if (MainMenu.MiscSettingsMenu.LockCameraY)
            {
                SetGameplayCamRelativePitch(0f, 0f);
            }
            if (MainMenu.MiscSettingsMenu.LockCameraX)
            {
                if (Game.IsControlPressed(0, Control.LookLeftOnly))
                {
                    cameraRotationHeading++;
                }
                else if (Game.IsControlPressed(0, Control.LookRightOnly))
                {
                    cameraRotationHeading--;
                }
                SetGameplayCamRelativeHeading(cameraRotationHeading);
            }
            #endregion

            // The teleport-to-waypoint, drift and point keys are key bindings now (Key bindings).

            if (expandedRadarProfile) // 221 = settings > display > expanded radar, read by RadarSettings
            {
                SetBigmapActive(true, false);
            }
            else
            {
                // The minimap key opens it (Key bindings); vMenu closes what it opened after 8 s.
                if (vMenuOpenedBigmap && (!IsBigmapActive() || GetGameTimer() - radarSwitchTimer > 8000))
                {
                    SetBigmapActive(false, false);
                    vMenuOpenedBigmap = false;
                }
            }

            // Each option above draws or enforces a setting every frame while it is on. With all of
            // them off nothing here does anything, so it rests. The keys are key bindings now and
            // need no polling.
            var misc = MainMenu.MiscSettingsMenu;
            var everyFrame = AnyMenuOpen
                || misc.HideRadar || misc.LockCameraX || misc.LockCameraY
                || expandedRadarProfile || vMenuOpenedBigmap || bothSpeeds;
            if (!everyFrame)
            {
                await Rest(IdleRestMs);
            }
        }

        #endregion

        #region Misc settings recording keybinds
        /// <summary>
        /// Function that manages the recording keybinds.
        /// </summary>
        /// <returns></returns>
        private async Task MiscRecordingKeybinds()
        {
            if (!MainMenu.MiscSettingsMenu.KbRecordKeys)
            {
                // The recording keys are off: nothing to read. Checked again 4 times a second, or
                // next frame while a menu is open, so switching them on works at once.
                if (!AnyMenuOpen)
                {
                    await Rest(IdleRestMs);
                }
                return;
            }

            // Nothing below acts unless one of these keys is used this frame (the record key, the
            // discard key while recording, or holding the controller's recording button), so they
            // are read first. The pause, fade, menu and input mode checks only run when one is.
            if (!Game.IsControlJustReleased(0, 0 == Control.ReplayStartStopRecording ? Control.SaveReplayClip : Control.ReplayStartStopRecording)
                && !Game.IsControlJustPressed(0, Control.ReplayClipDelete)
                && !Game.IsControlPressed(0, Control.MultiplayerInfo))
            {
                return;
            }

            if (MainMenu.MiscSettingsMenu.KbRecordKeys)
            {
                if (!IsPauseMenuActive() && IsScreenFadedIn() && !IsPlayerSwitchInProgress() && !MenuController.IsAnyMenuOpen())
                {
                    if (Game.CurrentInputMode == InputMode.MouseAndKeyboard)
                    {
                        var recordKey = 0 == Control.ReplayStartStopRecording ? Control.SaveReplayClip : Control.ReplayStartStopRecording;
                        if (!IsRecording())
                        {
                            if (Game.IsControlJustReleased(0, recordKey))
                            {
                                StartRecording(1);
                                if (recordKey == Control.ReplayStartStopRecording)
                                {
                                    HelpMessage.Custom("Press ~INPUT_REPLAY_START_STOP_RECORDING~ to save the recording, press ~INPUT_REPLAY_CLIP_DELETE~ to discard the recording.");
                                }
                                else
                                {
                                    HelpMessage.Custom("Press ~INPUT_SAVE_REPLAY_CLIP~ to save the recording, press ~INPUT_REPLAY_CLIP_DELETE~ to discard the recording.");
                                }

                            }
                        }
                        else
                        {
                            if (Game.IsControlJustReleased(0, recordKey))
                            {
                                StopRecording();
                            }
                            if (Game.IsControlJustPressed(0, Control.ReplayClipDelete)) // delete key on keyboard
                            {
                                StopRecordingAndDiscardClip();
                            }
                        }
                    }
                    else
                    {
                        if (Game.IsControlPressed(0, Control.MultiplayerInfo))
                        {
                            var timer = GetGameTimer();
                            var longEnough = false;
                            var notifOne = -1;
                            var notifTwo = -1;
                            while (Game.IsControlPressed(0, Control.MultiplayerInfo))
                            {
                                if (GetGameTimer() - timer > 400 && !longEnough)
                                {
                                    longEnough = true;

                                    if (IsRecording())
                                    {
                                        SetNotificationTextEntry("STRING");
                                        notifOne = DrawNotificationWithButton(1, "~INPUT_REPLAY_START_STOP_RECORDING~", "Stop recording and save clip.");
                                        SetNotificationTextEntry("STRING");
                                        notifTwo = DrawNotificationWithButton(1, "~INPUT_SAVE_REPLAY_CLIP~", "Stop recording and delete clip.");
                                    }
                                    else
                                    {
                                        SetNotificationTextEntry("STRING");
                                        notifOne = DrawNotificationWithButton(1, "~INPUT_REPLAY_START_STOP_RECORDING~", "Start recording.");
                                    }
                                }

                                if (longEnough)
                                {
                                    Game.DisableControlThisFrame(0, Control.VehicleCinCam);

                                    if (IsRecording())
                                    {
                                        if (Game.IsControlJustReleased(0, Control.SaveReplayClip))
                                        {
                                            StopRecordingAndDiscardClip();
                                            break;
                                        }
                                        if (Game.IsControlJustReleased(0, Control.ReplayStartStopRecording))
                                        {
                                            StopRecording();
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        if (Game.IsControlJustReleased(0, Control.ReplayStartStopRecording))
                                        {
                                            StartRecording(1);
                                            HelpMessage.Custom("Hold down ~INPUT_MULTIPLAYER_INFO~ and press ~INPUT_REPLAY_START_STOP_RECORDING~ to save the recording, press ~INPUT_SAVE_REPLAY_CLIP~ to discard the recording.");
                                            break;
                                        }
                                    }
                                }
                                await Delay(0);
                            }

                            if (notifOne != -1)
                            {
                                RemoveNotification(notifOne);
                            }
                            if (notifTwo != -1)
                            {
                                RemoveNotification(notifTwo);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Join / Quit notifications (via events)
        /// <summary>
        /// Runs join/quit notification checks.
        /// </summary>
        /// <returns></returns>
        [EventHandler("vMenu:PlayerJoinQuit")]
        internal void OnJoinQuitNotification(string playerName, string dropReason)
        {
            if (MainMenu.PermissionsSetupComplete && MainMenu.MiscSettingsMenu != null)
            {
                // Join/Quit notifications
                if (MainMenu.MiscSettingsMenu.JoinQuitNotifications && IsAllowed(Permission.MSJoinQuitNotifs))
                {
                    if (dropReason == null)
                    {
                        Notify.Custom($"~g~{GetSafePlayerName(playerName)}~s~ joined the server.");
                    }
                    else
                    {
                        Notify.Custom($"~r~{GetSafePlayerName(playerName)}~s~ left the server. ~c~({GetSafePlayerName(dropReason)})");
                    }
                }
            }
        }
        #endregion

        #region Death Notifications
        /// <summary>
        /// Runs death notification checks.
        /// </summary>
        /// <returns></returns>
        private async Task DeathNotifications()
        {
            // Death notifications
            if (MainMenu.MiscSettingsMenu.DeathNotifications)
            {
                var pl = Players;
                var tmpiterator = 0;
                foreach (var p in pl)
                {
                    tmpiterator++;
                    if (p.IsDead)
                    {
                        if (deadPlayers.Contains(p.Handle)) { continue; }
                        var killer = p.Character.GetKiller();
                        if (killer != null)
                        {
                            if (killer.Handle != p.Character.Handle)
                            {
                                if (killer.Exists())
                                {
                                    if (killer.Model.IsPed)
                                    {
                                        var found = false;
                                        foreach (var playerKiller in pl)
                                        {
                                            if (playerKiller.Character.Handle == killer.Handle)
                                            {
                                                Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~has been murdered by ~y~{GetSafePlayerName(playerKiller.Name)}~s~.");
                                                found = true;
                                                break;
                                            }
                                        }
                                        if (!found)
                                        {
                                            Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~has been murdered.");
                                        }
                                    }
                                    else if (killer.Model.IsVehicle)
                                    {
                                        var found = false;
                                        foreach (var playerKiller in pl)
                                        {
                                            if (playerKiller.Character.IsInVehicle())
                                            {
                                                if (playerKiller.Character.CurrentVehicle.Handle == killer.Handle)
                                                {
                                                    Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~has been murdered by ~y~{GetSafePlayerName(playerKiller.Name)}~s~.");
                                                    found = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (!found)
                                        {
                                            Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~has been murdered.");
                                        }
                                    }
                                    else
                                    {
                                        Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~has been murdered.");
                                    }
                                }
                                else
                                {
                                    Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~has been murdered.");
                                }
                            }
                            else
                            {
                                Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~committed suicide.");
                            }
                        }
                        else
                        {
                            Notify.Custom($"~o~{GetSafePlayerName(p.Name)} ~s~died.");
                        }
                        deadPlayers.Add(p.Handle);
                    }
                    else
                    {
                        if (deadPlayers.Contains(p.Handle))
                        {
                            deadPlayers.Remove(p.Handle);
                        }
                    }
                }
            }
            // Players stay dead for seconds, so 4 checks a second is enough. This ran every frame.
            await Delay(250);
        }
        #endregion
        #endregion

        #region Voice Chat Tasks
        /// <summary>
        /// Run all voice chat options tasks
        /// </summary>
        /// <returns></returns>
        private async Task VoiceChat()
        {
            if (MainMenu.VoiceChatSettingsMenu.EnableVoicechat && IsAllowed(Permission.VCEnable))
            {
                NetworkSetVoiceActive(true);
                NetworkSetTalkerProximity(MainMenu.VoiceChatSettingsMenu.currentProximity);
                var channel = MainMenu.VoiceChatSettingsMenu.channels.IndexOf(MainMenu.VoiceChatSettingsMenu.currentChannel);
                if (channel < 1)
                {
                    NetworkClearVoiceChannel();
                }
                else
                {
                    NetworkSetVoiceChannel(channel);
                }
                if (MainMenu.VoiceChatSettingsMenu.ShowCurrentSpeaker && IsAllowed(Permission.VCShowSpeaker))
                {
                    var pl = Players;
                    var i = 1;
                    var currentlyTalking = false;
                    foreach (var p in pl)
                    {
                        if (NetworkIsPlayerTalking(p.Handle))
                        {
                            if (!currentlyTalking)
                            {
                                DrawTextOnScreen("~s~Currently Talking", 0.5f, 0.00f, 0.5f, Alignment.Center, 6);
                                currentlyTalking = true;
                            }
                            DrawTextOnScreen($"~b~{p.Name}", 0.5f, 0.00f + (i * 0.03f), 0.5f, Alignment.Center, 6);
                            i++;
                        }
                    }
                }
                if (MainMenu.VoiceChatSettingsMenu.ShowVoiceStatus)
                {

                    if (GetGameTimer() - voiceTimer > 150)
                    {
                        voiceTimer = GetGameTimer();
                        voiceCycle++;
                        if (voiceCycle > 3)
                        {
                            voiceCycle = 1;
                        }
                    }
                    if (!HasStreamedTextureDictLoaded("mpleaderboard"))
                    {
                        RequestStreamedTextureDict("mpleaderboard", false);
                        while (!HasStreamedTextureDictLoaded("mpleaderboard"))
                        {
                            await Delay(0);
                        }
                    }
                    if (NetworkIsPlayerTalking(Game.Player.Handle))
                    {
                        DrawSprite("mpleaderboard", $"leaderboard_audio_{voiceCycle}", 0.008f, 0.985f, voiceIndicatorWidth, voiceIndicatorHeight, 0f, 255, 55, 0, 255);
                    }
                    else
                    {
                        DrawSprite("mpleaderboard", "leaderboard_audio_mute", 0.008f, 0.985f, voiceIndicatorMutedWidth, voiceIndicatorHeight, 0f, 255, 55, 0, 255);
                    }
                }
                else
                {
                    if (HasStreamedTextureDictLoaded("mpleaderboard"))
                    {
                        SetStreamedTextureDictAsNoLongerNeeded("mpleaderboard");
                    }
                }
            }
            else
            {
                NetworkSetVoiceActive(false);
                NetworkClearVoiceChannel();
                // Voice chat is off: these two settings persist, so 4 times a second is enough.
                if (!AnyMenuOpen)
                {
                    await Rest(IdleRestMs);
                }
            }
        }
        #endregion

        #region Update Time Options Menu (current time display)
        /// <summary>
        /// Update the current time display in the time options menu.
        /// </summary>
        /// <returns></returns>
        private async Task TimeOptions()
        {
            if (MainMenu.TimeOptionsMenu.freezeTimeToggle != null && MainMenu.TimeOptionsMenu.GetMenu().Visible && IsAllowed(Permission.TOFreezeTime))
            {
                // Update the current time displayed in the Time Options menu (only when the menu is actually visible).
                var hours = GetClockHours();
                var minutes = GetClockMinutes();
                var hoursString = hours < 10 ? "0" + hours.ToString() : hours.ToString();
                var minutesString = minutes < 10 ? "0" + minutes.ToString() : minutes.ToString();
                MainMenu.TimeOptionsMenu.freezeTimeToggle.Label = $"(Current Time {hoursString}:{minutesString})";
            }
            // This only needs to be updated once every 2 seconds so we can delay it.
            await Delay(2000);
        }
        #endregion

        #region Weapon Options Tasks
        /// <summary>
        /// Manage all weapon options that need to be handeled every tick.
        /// </summary>
        /// <returns></returns>
        private async Task WeaponOptions()
        {
            // If no reload is enabled.
            if (MainMenu.WeaponOptionsMenu.NoReload && Game.PlayerPed.Weapons.Current.Hash != WeaponHash.Minigun && IsAllowed(Permission.WPNoReload))
            {
                // Disable reloading.
                SetAmmoInClip(Game.PlayerPed.Handle, (uint)Game.PlayerPed.Weapons.Current.Hash, 5);
            }

            // Unlimited ammo: kept on the weapon in hand while the option is on, and switched off once
            // for the last weapon when the option goes off or you change weapon. It used to write
            // "off" every frame, undoing any other script's infinite ammo.
            var ped = PlayerPedId();
            var weapon = GetSelectedPedWeapon(ped);
            var unlimitedAmmo = MainMenu.WeaponOptionsMenu.UnlimitedAmmo && IsAllowed(Permission.WPUnlimitedAmmo);
            var applyAmmo = unlimitedAmmoLatch.Update(unlimitedAmmo, weapon == unchecked((int)WeaponHash.Unarmed) ? 0 : weapon, out var previousWeapon);
            if (previousWeapon != 0)
            {
                SetPedInfiniteAmmo(ped, false, unchecked((uint)previousWeapon));
            }
            if (applyAmmo)
            {
                SetPedInfiniteAmmo(ped, true, unchecked((uint)weapon));
            }

            if (MainMenu.WeaponOptionsMenu.AutoEquipChute)
            {
                if ((IsPedInAnyHeli(ped) || IsPedInAnyPlane(ped)) && !HasPedGotWeapon(ped, (uint)WeaponHash.Parachute, false))
                {
                    GiveWeaponToPed(ped, (uint)WeaponHash.Parachute, 1, false, true);
                    SetPlayerHasReserveParachute(PlayerId());
                    SetPlayerCanLeaveParachuteSmokeTrail(ped, true);
                }
            }

            if (MainMenu.WeaponOptionsMenu.UnlimitedParachutes)
            {
                // WeaponHash.Parachute is GetHashKey("gadget_parachute") (0xFBAB5776), which this hashed every frame.
                if (!HasPedGotWeapon(ped, (uint)WeaponHash.Parachute, false))
                {
                    GiveWeaponToPed(ped, (uint)WeaponHash.Parachute, 0, false, false);
                }

                if (!GetPlayerHasReserveParachute(PlayerId()))
                {
                    SetPlayerHasReserveParachute(PlayerId());
                }
            }

            // With every weapon option off there is nothing to apply; the unlimited ammo latch only
            // has a weapon to switch back, which happened above.
            var everyFrame = AnyMenuOpen
                || MainMenu.WeaponOptionsMenu.NoReload
                || MainMenu.WeaponOptionsMenu.UnlimitedAmmo
                || MainMenu.WeaponOptionsMenu.AutoEquipChute
                || MainMenu.WeaponOptionsMenu.UnlimitedParachutes;
            if (!everyFrame)
            {
                await Rest(IdleRestMs);
            }
        }
        #endregion

        #region Player Appearance

        internal static bool reverseCamera = false;
        private static Camera camera;
        internal static float CameraFov { get; set; } = 45;
        internal static int CurrentCam { get; set; }
        internal static List<KeyValuePair<Vector3, Vector3>> CameraOffsets { get; } = new List<KeyValuePair<Vector3, Vector3>>()
        {
            // Full body
            new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 2.8f, 0.3f), new Vector3(0f, 0f, 0f)),

            // Head level
            new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 0.9f, 0.65f), new Vector3(0f, 0f, 0.6f)),

            // Upper Body
            new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 1.4f, 0.5f), new Vector3(0f, 0f, 0.3f)),

            // Lower Body
            new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 1.6f, -0.3f), new Vector3(0f, 0f, -0.45f)),

            // Shoes
            new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 0.98f, -0.7f), new Vector3(0f, 0f, -0.90f)),

            // Lower Arms
            new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 0.98f, 0.1f), new Vector3(0f, 0f, 0f)),

            // Full arms
            new KeyValuePair<Vector3, Vector3>(new Vector3(0f, 1.3f, 0.35f), new Vector3(0f, 0f, 0.15f)),
        };

        private async Task UpdateCamera(Camera oldCamera, Vector3 pos, Vector3 pointAt)
        {
            var newCam = CreateCam("DEFAULT_SCRIPTED_CAMERA", true);
            var newCamera = new Camera(newCam)
            {
                Position = pos,
                FieldOfView = CameraFov
            };
            newCamera.PointAt(pointAt);
            oldCamera.InterpTo(newCamera, 1000, true, true);
            while (oldCamera.IsInterpolating || !newCamera.IsActive)
            {
                SetEntityCollision(Game.PlayerPed.Handle, false, false);
                //Game.PlayerPed.IsInvincible = true;
                Game.PlayerPed.IsPositionFrozen = true;
                await Delay(0);
            }
            await Delay(50);
            oldCamera.Delete();
            CurrentCam = newCam;
            camera = newCamera;
        }

        private bool IsMpCharEditorOpen()
        {
            if (MainMenu.MpPedCustomizationMenu != null)
            {
                return
                    MainMenu.MpPedCustomizationMenu.appearanceMenu.Visible ||
                    MainMenu.MpPedCustomizationMenu.faceShapeMenu.Visible ||
                    MainMenu.MpPedCustomizationMenu.createCharacterMenu.Visible ||
                    MainMenu.MpPedCustomizationMenu.inheritanceMenu.Visible ||
                    MainMenu.MpPedCustomizationMenu.propsMenu.Visible ||
                    MainMenu.MpPedCustomizationMenu.clothesMenu.Visible ||
                    MainMenu.MpPedCustomizationMenu.tattoosMenu.Visible;
            }
            return false;
        }

        /// <summary>
        /// Manages the camera for the mp character customization menu
        /// </summary>
        /// <returns></returns>
        private async Task ManageCamera()
        {
            if (IsPedInAnyVehicle(PlayerPedId(), false))
            {
                if (MainMenu.MpPedCustomizationMenu.editPedBtn != null && MainMenu.MpPedCustomizationMenu.editPedBtn.Enabled)
                {
                    MainMenu.MpPedCustomizationMenu.editPedBtn.Enabled = false;
                    MainMenu.MpPedCustomizationMenu.editPedBtn.LeftIcon = MenuItem.Icon.LOCK;
                    MainMenu.MpPedCustomizationMenu.editPedBtn.Description += " ~r~You need to get out of your vehicle before you can use this.";
                }
                if (MainMenu.MpPedCustomizationMenu.createMaleBtn != null && MainMenu.MpPedCustomizationMenu.createMaleBtn.Enabled)
                {
                    MainMenu.MpPedCustomizationMenu.createMaleBtn.Enabled = false;
                    MainMenu.MpPedCustomizationMenu.createMaleBtn.LeftIcon = MenuItem.Icon.LOCK;
                    MainMenu.MpPedCustomizationMenu.createMaleBtn.Description += " ~r~You need to get out of your vehicle before you can use this.";
                }
                if (MainMenu.MpPedCustomizationMenu.createFemaleBtn != null && MainMenu.MpPedCustomizationMenu.createFemaleBtn.Enabled)
                {
                    MainMenu.MpPedCustomizationMenu.createFemaleBtn.Enabled = false;
                    MainMenu.MpPedCustomizationMenu.createFemaleBtn.LeftIcon = MenuItem.Icon.LOCK;
                    MainMenu.MpPedCustomizationMenu.createFemaleBtn.Description += " ~r~You need to get out of your vehicle before you can use this.";
                }
            }
            else
            {
                if (MainMenu.MpPedCustomizationMenu.editPedBtn != null && !MainMenu.MpPedCustomizationMenu.editPedBtn.Enabled)
                {
                    MainMenu.MpPedCustomizationMenu.editPedBtn.Enabled = true;
                    MainMenu.MpPedCustomizationMenu.editPedBtn.LeftIcon = MenuItem.Icon.NONE;
                    MainMenu.MpPedCustomizationMenu.editPedBtn.Description = MainMenu.MpPedCustomizationMenu.editPedBtn.Description.Replace(" ~r~You need to get out of your vehicle before you can use this.", "");
                }
                if (MainMenu.MpPedCustomizationMenu.createMaleBtn != null && !MainMenu.MpPedCustomizationMenu.createMaleBtn.Enabled)
                {
                    MainMenu.MpPedCustomizationMenu.createMaleBtn.Enabled = true;
                    MainMenu.MpPedCustomizationMenu.createMaleBtn.LeftIcon = MenuItem.Icon.NONE;
                    MainMenu.MpPedCustomizationMenu.createMaleBtn.Description = MainMenu.MpPedCustomizationMenu.createMaleBtn.Description.Replace(" ~r~You need to get out of your vehicle before you can use this.", "");
                }
                if (MainMenu.MpPedCustomizationMenu.createFemaleBtn != null && !MainMenu.MpPedCustomizationMenu.createFemaleBtn.Enabled)
                {
                    MainMenu.MpPedCustomizationMenu.createFemaleBtn.Enabled = true;
                    MainMenu.MpPedCustomizationMenu.createFemaleBtn.LeftIcon = MenuItem.Icon.NONE;
                    MainMenu.MpPedCustomizationMenu.createFemaleBtn.Description = MainMenu.MpPedCustomizationMenu.createFemaleBtn.Description.Replace(" ~r~You need to get out of your vehicle before you can use this.", "");
                }
            }

            if (IsMpCharEditorOpen())
            {
                // Only used for the watch pose; the editor still works if it fails to load.
                await LoadAnimDictAsync("anim@random@shop_clothes@watches");

                while (IsMpCharEditorOpen())
                {
                    await Delay(0);

                    var index = GetCameraIndex(MenuController.GetCurrentMenu());
                    if (MenuController.GetCurrentMenu() == MainMenu.MpPedCustomizationMenu.propsMenu && MenuController.GetCurrentMenu().CurrentIndex == 3 && !reverseCamera)
                    {
                        TaskPlayAnim(Game.PlayerPed.Handle, "anim@random@shop_clothes@watches", "BASE", 8f, -8f, -1, 1, 0, false, false, false);
                    }
                    else
                    {
                        Game.PlayerPed.Task.ClearAll();
                    }

                    var xOffset = 0f;
                    var yOffset = 0f;

                    if ((Game.IsControlPressed(0, Control.ParachuteBrakeLeft) || Game.IsControlPressed(0, Control.ParachuteBrakeRight)) && !(Game.IsControlPressed(0, Control.ParachuteBrakeLeft) && Game.IsControlPressed(0, Control.ParachuteBrakeRight)))
                    {
                        switch (index)
                        {
                            case 0:
                                xOffset = 2.2f;
                                yOffset = -1f;
                                break;
                            case 1:
                                xOffset = 0.7f;
                                yOffset = -0.45f;
                                break;
                            case 2:
                                xOffset = 1.35f;
                                yOffset = -0.4f;
                                break;
                            case 3:
                                xOffset = 1.0f;
                                yOffset = -0.4f;
                                break;
                            case 4:
                                xOffset = 0.9f;
                                yOffset = -0.4f;
                                break;
                            case 5:
                                xOffset = 0.8f;
                                yOffset = -0.7f;
                                break;
                            case 6:
                                xOffset = 1.5f;
                                yOffset = -1.0f;
                                break;
                            default:
                                xOffset = 0f;
                                yOffset = 0.2f;
                                break;
                        }
                        if (Game.IsControlPressed(0, Control.ParachuteBrakeRight))
                        {
                            xOffset *= -1f;
                        }

                    }

                    Vector3 pos;
                    if (reverseCamera)
                    {
                        pos = GetOffsetFromEntityInWorldCoords(Game.PlayerPed.Handle, (CameraOffsets[index].Key.X + xOffset) * -1f, (CameraOffsets[index].Key.Y + yOffset) * -1f, CameraOffsets[index].Key.Z);
                    }
                    else
                    {
                        pos = GetOffsetFromEntityInWorldCoords(Game.PlayerPed.Handle, CameraOffsets[index].Key.X + xOffset, CameraOffsets[index].Key.Y + yOffset, CameraOffsets[index].Key.Z);
                    }

                    var pointAt = GetOffsetFromEntityInWorldCoords(Game.PlayerPed.Handle, CameraOffsets[index].Value.X, CameraOffsets[index].Value.Y, CameraOffsets[index].Value.Z);

                    if (Game.IsControlPressed(0, Control.MoveLeftOnly))
                    {
                        Game.PlayerPed.Task.LookAt(GetOffsetFromEntityInWorldCoords(Game.PlayerPed.Handle, 1.2f, .5f, .7f), 1100);
                    }
                    else if (Game.IsControlPressed(0, Control.MoveRightOnly))
                    {
                        Game.PlayerPed.Task.LookAt(GetOffsetFromEntityInWorldCoords(Game.PlayerPed.Handle, -1.2f, .5f, .7f), 1100);
                    }
                    else
                    {
                        Game.PlayerPed.Task.LookAt(GetOffsetFromEntityInWorldCoords(Game.PlayerPed.Handle, 0f, .5f, .7f), 1100);
                    }

                    if (Game.IsControlJustReleased(0, Control.Jump))
                    {
                        var Pos = Game.PlayerPed.Position;
                        SetEntityCollision(Game.PlayerPed.Handle, true, true);
                        FreezeEntityPosition(Game.PlayerPed.Handle, false);
                        TaskGoStraightToCoord(Game.PlayerPed.Handle, Pos.X, Pos.Y, Pos.Z, 8f, 1600, Game.PlayerPed.Heading + 180f, 0.1f);
                        var timer = GetGameTimer();
                        while (true)
                        {
                            await Delay(0);
                            //DisplayRadar(false);
                            Game.DisableAllControlsThisFrame(0);
                            if (GetGameTimer() - timer > 1600)
                            {
                                break;
                            }
                        }
                        ClearPedTasks(Game.PlayerPed.Handle);
                        Game.PlayerPed.PositionNoOffset = Pos;
                        FreezeEntityPosition(Game.PlayerPed.Handle, true);
                        SetEntityCollision(Game.PlayerPed.Handle, false, false);
                        reverseCamera = !reverseCamera;
                    }

                    SetEntityCollision(Game.PlayerPed.Handle, false, false);
                    //Game.PlayerPed.IsInvincible = true;
                    Game.PlayerPed.IsPositionFrozen = true;

                    if (!DoesCamExist(CurrentCam))
                    {
                        CurrentCam = CreateCam("DEFAULT_SCRIPTED_CAMERA", true);
                        camera = new Camera(CurrentCam)
                        {
                            Position = pos,
                            FieldOfView = CameraFov
                        };
                        camera.PointAt(pointAt);
                        RenderScriptCams(true, false, 0, false, false);
                        camera.IsActive = true;
                    }
                    else
                    {
                        if (camera.Position != pos)
                        {
                            await UpdateCamera(camera, pos, pointAt);
                        }
                    }
                }

                SetEntityCollision(Game.PlayerPed.Handle, true, true);

                Game.PlayerPed.IsPositionFrozen = false;

                DisplayHud(true);
                DisplayRadar(true);

                if (HasAnimDictLoaded("anim@random@shop_clothes@watches"))
                {
                    RemoveAnimDict("anim@random@shop_clothes@watches");
                }

                reverseCamera = false;
            }
            else
            {
                if (camera != null)
                {
                    ClearCamera();
                    camera = null;
                }
            }

            // The character editor only opens from the menu, and the buttons above are only seen
            // in it, so with every menu closed this rests.
            if (!AnyMenuOpen && camera == null)
            {
                await Rest(IdleRestMs);
            }
        }

        private int GetCameraIndex(Menu menu)
        {
            if (menu != null)
            {
                if (menu == MainMenu.MpPedCustomizationMenu.inheritanceMenu)
                {
                    return 1;
                }
                else if (menu == MainMenu.MpPedCustomizationMenu.clothesMenu)
                {
                    return menu.CurrentIndex switch
                    {
                        // masks
                        0 => 1,
                        // upper body
                        1 => 2,
                        // lower body
                        2 => 3,
                        // bags & parachutes
                        3 => 2,
                        // shoes
                        4 => 4,
                        // scarfs & chains
                        5 => 2,
                        // shirt & accessory
                        6 => 2,
                        // body armor & accessory
                        7 => 2,
                        // badges & logos
                        8 => 0,
                        // shirt overlay & jackets
                        9 => 2,
                        _ => 0,
                    };
                }
                else if (menu == MainMenu.MpPedCustomizationMenu.propsMenu)
                {
                    return menu.CurrentIndex switch
                    {
                        // 0 = hats & helmets
                        // 1 = glasses
                        // 2 = misc props
                        0 or 1 or 2 => 1,

                        // 3 = watches
                        3 => reverseCamera ? 5 : 6,

                        // 4 = bracelets
                        4 => 5,

                        _ => 0,
                    };
                }
                else if (menu == MainMenu.MpPedCustomizationMenu.appearanceMenu)
                {
                    return menu.CurrentIndex switch
                    {
                        // 0 = hair style
                        // 1 = hair color
                        // 2 = hair highlight color
                        // 3 = blemishes
                        // 4 = blemishes opacity
                        // 5 = beard style
                        // 6 = beard opacity
                        // 7 = beard color
                        // 8 = eyebrows style
                        // 9 = eyebrows opacity
                        // 10 = eyebrows color
                        // 11 = ageing style
                        // 12 = ageing opacity
                        // 13 = makeup style
                        // 14 = makeup opacity
                        // 15 = makeup color
                        // 16 = blush style
                        // 17 = blush opacity
                        // 18 = blush color
                        // 19 = complexion style
                        // 20 = complexion opacity
                        // 21 = sun damage style
                        // 22 = sun damage opacity
                        // 23 = lipstick style
                        // 24 = lipstick opacity
                        // 25 = lipstick color
                        // 26 = moles and freckles style
                        // 27 = moles and freckles opacity
                        0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 26 or 27 => 1,

                        // 28 = chest hair style
                        // 29 = chest hair opacity
                        // 30 = chest hair color
                        // 31 = body blemishes style
                        // 32 = body blemishes opacity
                        28 or 29 or 30 or 31 or 32 => 2,

                        // 33 = eye colors
                        33 => 1,

                        _ => 0,
                    };
                }
                else if (menu == MainMenu.MpPedCustomizationMenu.tattoosMenu)
                {
                    return menu.CurrentIndex switch
                    {
                        // 0 = head
                        0 => 1,

                        // 1 = torso
                        1 => 2,

                        // 2 = left arm
                        // 3 = right arm
                        2 or 3 => 6,

                        // 4 = left leg 
                        // 5 = right leg
                        4 or 5 => 3,

                        // 6 = badges
                        6 => 2,

                        _ => 0,
                    };
                }
                else if (menu == MainMenu.MpPedCustomizationMenu.faceShapeMenu)
                {
                    var item = menu.GetCurrentMenuItem();
                    if (item != null)
                    {
                        if (item.GetType() == typeof(MenuSliderItem))
                        {
                            return 1;
                        }
                    }
                    return 0;
                }
            }
            return 0;
        }

        internal static void ClearCamera()
        {
            camera.IsActive = false;
            RenderScriptCams(false, false, 0, false, false);
            DestroyCam(CurrentCam, false);
            CurrentCam = -1;
            camera.Delete();
        }

        /// <summary>
        /// Disables movement while the mp character creator is open. Runs every frame, from PerFrameChecks.
        /// </summary>
        private void DisableMovement()
        {
            if (IsMpCharEditorOpen())
            {
                Game.DisableControlThisFrame(0, Control.MoveDown);
                Game.DisableControlThisFrame(0, Control.MoveDownOnly);
                Game.DisableControlThisFrame(0, Control.MoveLeft);
                Game.DisableControlThisFrame(0, Control.MoveLeftOnly);
                Game.DisableControlThisFrame(0, Control.MoveLeftRight);
                Game.DisableControlThisFrame(0, Control.MoveRight);
                Game.DisableControlThisFrame(0, Control.MoveRightOnly);
                Game.DisableControlThisFrame(0, Control.MoveUp);
                Game.DisableControlThisFrame(0, Control.MoveUpDown);
                Game.DisableControlThisFrame(0, Control.MoveUpOnly);
                Game.DisableControlThisFrame(0, Control.NextCamera);
                Game.DisableControlThisFrame(0, Control.LookBehind);
                Game.DisableControlThisFrame(0, Control.LookDown);
                Game.DisableControlThisFrame(0, Control.LookDownOnly);
                Game.DisableControlThisFrame(0, Control.LookLeft);
                Game.DisableControlThisFrame(0, Control.LookLeftOnly);
                Game.DisableControlThisFrame(0, Control.LookLeftRight);
                Game.DisableControlThisFrame(0, Control.LookRight);
                Game.DisableControlThisFrame(0, Control.LookRightOnly);
                Game.DisableControlThisFrame(0, Control.LookUp);
                Game.DisableControlThisFrame(0, Control.LookUpDown);
                Game.DisableControlThisFrame(0, Control.LookUpOnly);
                Game.DisableControlThisFrame(0, Control.Aim);
                Game.DisableControlThisFrame(0, Control.AccurateAim);
                Game.DisableControlThisFrame(0, Control.Cover);
                Game.DisableControlThisFrame(0, Control.Duck);
                Game.DisableControlThisFrame(0, Control.Jump);
                Game.DisableControlThisFrame(0, Control.SelectNextWeapon);
                Game.DisableControlThisFrame(0, Control.PrevWeapon);
                Game.DisableControlThisFrame(0, Control.WeaponSpecial);
                Game.DisableControlThisFrame(0, Control.WeaponSpecial2);
                Game.DisableControlThisFrame(0, Control.WeaponWheelLeftRight);
                Game.DisableControlThisFrame(0, Control.WeaponWheelNext);
                Game.DisableControlThisFrame(0, Control.WeaponWheelPrev);
                Game.DisableControlThisFrame(0, Control.WeaponWheelUpDown);
                Game.DisableControlThisFrame(0, Control.VehicleExit);
                Game.DisableControlThisFrame(0, Control.Enter);
            }
        }
        #endregion

        #region Restore player skin & weapons after respawning.
        /// <summary>
        /// PSRP_DeathScript's `dead` state bag (true or 1) is set until a revive or respawn.
        /// </summary>
        private static bool IsLogicallyDead()
        {
            object dead = Game.Player.State["dead"];
            if (dead is bool flag)
            {
                return flag;
            }
            return (dead is int || dead is long || dead is double) && Convert.ToDouble(dead) == 1;
        }

        /// <summary>
        /// Restores player appearance after dying.
        /// </summary>
        /// <returns></returns>
        private async Task RestorePlayerAfterBeingDead()
        {
            // Still checked every frame: the appearance and weapons are saved on the first frame
            // of death, before the game can drop the weapon in hand.
            if (MainMenu.MiscSettingsMenu != null && IsEntityDead(PlayerPedId()))
            {
                var restoreDefault = MainMenu.MiscSettingsMenu.MiscRespawnDefaultCharacter
                    && !string.IsNullOrEmpty(GetResourceKvpString("vmenu_default_character"));

                if (!restoreDefault)
                {
                    if (IsAllowed(Permission.MSRestoreAppearance))
                    {
                        var savedMpSnapshot = MainMenu.MpPedCustomizationMenu != null
                            && MainMenu.MpPedCustomizationMenu.CaptureCurrentMpAppearanceSnapshot();
                        if (!savedMpSnapshot)
                        {
                            await SavePed("vMenu_tmp_saved_ped");
                        }
                    }
                }

                if ((MainMenu.MiscSettingsMenu.RestorePlayerWeapons && IsAllowed(Permission.MSRestoreWeapons)) || (MainMenu.WeaponLoadoutsMenu != null && MainMenu.WeaponLoadoutsMenu.WeaponLoadoutsSetLoadoutOnRespawn && IsAllowed(Permission.WLEquipOnRespawn)))
                {
                    //await SaveWeaponLoadout();
                    if (SaveWeaponLoadout("vmenu_temp_weapons_loadout_before_respawn"))
                    {
                        Log($"weapons saved {GetResourceKvpString("vmenu_temp_weapons_loadout_before_respawn")}");
                    }
                    else
                    {
                        Log("save failed from restore weapons after death");
                    }
                }

                // PSRP_DeathScript's fake death brings the dead ped back to life about
                // 1.5 s after the fall and holds it in a lying clip, so IsDead alone would
                // swap the model under the body long before a revive or respawn.
                while (Game.PlayerPed.IsDead || IsLogicallyDead() || IsScreenFadedOut() || IsScreenFadingOut())
                {
                    await Delay(0);
                }

                var restoredMpAppearance = MainMenu.MpPedCustomizationMenu != null
                    && await MainMenu.MpPedCustomizationMenu.RestorePreferredMpAppearance(true);
                if (restoredMpAppearance)
                {
                    var deathAppearancePatchJson = await WaitForDpClothingDeathAppearancePatchJson();
                    if (HasDpClothingDeathAppearancePatch(deathAppearancePatchJson))
                    {
                        ApplyDpClothingDeathAppearancePatchToLivePed(deathAppearancePatchJson);
                        TriggerServerEvent("dpclothing:server:consumeDeathAppearancePatch");
                    }
                }
                else
                {
                    if (IsTempPedSaved() && IsAllowed(Permission.MSRestoreAppearance))
                    {
                        LoadSavedPed("vMenu_tmp_saved_ped", false, true);
                    }
                }

                if ((MainMenu.MiscSettingsMenu != null && MainMenu.MiscSettingsMenu.RestorePlayerWeapons && IsAllowed(Permission.MSRestoreWeapons)) || (MainMenu.WeaponLoadoutsMenu != null && MainMenu.WeaponLoadoutsMenu.WeaponLoadoutsSetLoadoutOnRespawn && IsAllowed(Permission.WLEquipOnRespawn)))
                {
                    await SpawnWeaponLoadoutAsync("vmenu_temp_weapons_loadout_before_respawn", true, false, false);
                    Log("weapons restored, deleting kvp");
                    DeleteResourceKvp("vmenu_temp_weapons_loadout_before_respawn");
                }
            }
        }
        #endregion

        #region Player clothing animations controller.
        // Static glow (0 = on, 1 = off) last applied per ped. This loop scans every player once a
        // second (see ScanClothingGlow); static modes used to be re-applied each time, and our own
        // decorator written twice.
        private readonly Dictionary<int, int> appliedStaticGlow = new();
        private readonly HashSet<int> glowPedsSeen = new();
        private int ownGlowDecorPed;
        private int ownGlowDecorValue = int.MinValue;

        // Peds whose glow animates (decorator 2 = fade, 3 = flash), found by the last scan.
        private readonly List<KeyValuePair<int, int>> animatedGlowPeds = new();
        private const int ClothingGlowScanMs = 1000;
        private bool clothingGlowScanned;
        private int clothingGlowScannedAt;

        /// <summary>
        /// Reads every player's glow decorator: applies static glows (0 = on, 1 = off) when they
        /// change, and lists the animated ones for <see cref="AnimateClothingGlow"/>.
        /// </summary>
        private void ScanClothingGlow()
        {
            glowPedsSeen.Clear();
            animatedGlowPeds.Clear();
            foreach (var player in Players)
            {
                var p = player.Character;
                if (p != null && p.Exists() && !p.IsDead)
                {
                    if (DecorExistOn(p.Handle, clothingAnimationDecor))
                    {
                        var decorVal = DecorGetInt(p.Handle, clothingAnimationDecor);
                        glowPedsSeen.Add(p.Handle);
                        if (decorVal is 0 or 1) // 0 = on solid/no animation, 1 = off.
                        {
                            if (!appliedStaticGlow.TryGetValue(p.Handle, out var applied) || applied != decorVal)
                            {
                                SetPedIlluminatedClothingGlowIntensity(p.Handle, decorVal == 0 ? 1f : 0f);
                                appliedStaticGlow[p.Handle] = decorVal;
                            }
                            continue;
                        }
                        appliedStaticGlow.Remove(p.Handle);
                        if (decorVal is 2 or 3)
                        {
                            animatedGlowPeds.Add(new KeyValuePair<int, int>(p.Handle, decorVal));
                        }
                    }
                }
            }
            // Forget peds that left, died or respawned.
            if (appliedStaticGlow.Count > glowPedsSeen.Count)
            {
                foreach (var ped in appliedStaticGlow.Keys.Where(ped => !glowPedsSeen.Contains(ped)).ToList())
                {
                    appliedStaticGlow.Remove(ped);
                }
            }
        }

        /// <summary>
        /// Sets this step's intensity on every animated glow the last scan found, skipping peds
        /// that have since gone or died (the scan used to skip those every pass).
        /// </summary>
        private void AnimateClothingGlow()
        {
            foreach (var glow in animatedGlowPeds)
            {
                var handle = glow.Key;
                if (!DoesEntityExist(handle) || IsEntityDead(handle))
                {
                    continue;
                }
                if (glow.Value == 2) // fade.
                {
                    SetPedIlluminatedClothingGlowIntensity(handle, clothingOpacity);
                }
                else // 3, flash.
                {
                    var result = 0f;
                    if (clothingAnimationReverse)
                    {
                        if (clothingOpacity is >= 0f and <= 0.5f) // || (clothingOpacity >= 0.5f && clothingOpacity <= 0.75f))
                        {
                            result = 1f;
                        }
                    }
                    else
                    {
                        if (clothingOpacity is >= 0.5f and <= 1.0f) //|| (clothingOpacity >= 0.75f && clothingOpacity <= 1.0f))
                        {
                            result = 1f;
                        }
                    }
                    SetPedIlluminatedClothingGlowIntensity(handle, result);
                }
            }
        }

        /// <summary>
        /// Writes our clothing animation decorator only when our ped or the chosen mode changed.
        /// </summary>
        private void SetOwnClothingAnimationDecor()
        {
            var ped = Game.PlayerPed.Handle;
            var value = PlayerAppearance.ClothingAnimationType;
            if (ped == ownGlowDecorPed && value == ownGlowDecorValue)
            {
                return;
            }
            DecorSetInt(ped, clothingAnimationDecor, value);
            ownGlowDecorPed = ped;
            ownGlowDecorValue = value;
            // Your own style or ped changed: scan on this pass, so your glow follows at once
            // instead of at the next once-a-second scan.
            clothingGlowScanned = false;
        }

        private async Task PlayerClothingAnimationsController()
        {
            if (!DecorIsRegisteredAsType(clothingAnimationDecor, 3))
            {
                try
                {
                    DecorRegister(clothingAnimationDecor, 3);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(@"[CRITICAL] A critical bug in one of your scripts was detected. vMenu is unable to set or register a decorator's value because another resource has already registered 1.5k or more decorators. vMenu will NOT work as long as this bug in your other scripts is unsolved. Please fix your other scripts. This is *NOT* caused by or fixable by vMenu!!!");
                    Debug.WriteLine($"Error Location: {e.StackTrace}\nError info: {e.Message}");
                    await Delay(1000);
                }
                while (!DecorIsRegisteredAsType(clothingAnimationDecor, 3))
                {
                    await Delay(0);
                }
            }
            else
            {
                try
                {
                    SetOwnClothingAnimationDecor();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(@"[CRITICAL] A critical bug in one of your scripts was detected. vMenu is unable to set or register a decorator's value because another resource has already registered 1.5k or more decorators. vMenu will NOT work as long as this bug in your other scripts is unsolved. Please fix your other scripts. This is *NOT* caused by or fixable by vMenu!!!");
                    Debug.WriteLine($"Error Location: {e.StackTrace}\nError info: {e.Message}");
                    await Delay(1000);
                }
                // The pass over every player (about five game calls each) runs once a second
                // instead of every 25 ms. It applies static glows when they change and lists the
                // animated ones; in between, only those are updated, every 25 ms as before.
                var now = GetGameTimer();
                if (!clothingGlowScanned || now - clothingGlowScannedAt >= ClothingGlowScanMs)
                {
                    clothingGlowScanned = true;
                    clothingGlowScannedAt = now;
                    ScanClothingGlow();
                }
                AnimateClothingGlow();
                if (clothingAnimationReverse)
                {
                    clothingOpacity -= 0.05f;
                    if (clothingOpacity < 0f)
                    {
                        clothingOpacity = 0f;
                        clothingAnimationReverse = false;
                    }
                }
                else
                {
                    clothingOpacity += 0.05f;
                    if (clothingOpacity > 1f)
                    {
                        clothingOpacity = 1f;
                        clothingAnimationReverse = true;
                    }
                }
                // One wake-up after 25 ms. This used to wait by waking every frame (Delay(0) until 25 ms
                // had passed), and each wake-up costs the C# scheduler more than the check itself.
                await Delay(25);
            }
            try
            {
                SetOwnClothingAnimationDecor();
            }
            catch (Exception e)
            {
                Debug.WriteLine(@"[CRITICAL] A critical bug in one of your scripts was detected. vMenu is unable to set or register a decorator's value because another resource has already registered 1.5k or more decorators. vMenu will NOT work as long as this bug in your other scripts is unsolved. Please fix your other scripts. This is *NOT* caused by or fixable by vMenu!!!");
                Debug.WriteLine($"Error Location: {e.StackTrace}\nError info: {e.Message}");
                await Delay(1000);
            }
        }
        #endregion

        #region player blips tasks
        // Blips this script created for other players (server id -> blip handle). GetBlipFromEntity only
        // returns one blip per ped, so it can't tell ours from another resource's (like the duty blip).
        private readonly Dictionary<int, int> ownedPlayerBlips = new();

        // Players whose blip another resource is already drawing. vMenu leaves them alone.
        // client/dutyBlips.lua pushes this list to us with a plain string event. It used to be
        // pulled from here through a Lua export every 100 ms: a constant stream of C# -> Lua calls,
        // each creating and releasing function references, which coincided with vMenu's key
        // handlers failing ("No such reference for 1": F1 and noclip stop responding until a game
        // restart). A string event carries no function references.
        private HashSet<int> externalBlipPlayers = new();
        private int externalBlipPlayersUpdatedAt;
        // dutyBlips.lua resends at least every 5 s. If it goes quiet (stopped, erroring), drop the
        // list so vMenu draws its own blips again rather than hiding them indefinitely.
        private const int ExternalBlipPlayersStaleMs = 15000;
        private int ownedPlayerBlipsPrunedAt;
        private const int OwnedPlayerBlipsPruneMs = 100;

        // What we last applied to each player's blip (server id -> style).
        private sealed class BlipStyle
        {
            public int Blip;
            public int Sprite = -1;
            public bool? Heading;
            public int Display = -1;
        }
        private readonly Dictionary<int, BlipStyle> blipStyles = new();

        // With blips on, the whole refresh below ran every frame: about 20 game calls per player in
        // scope and two per player elsewhere on the server. It now runs 10 times a second, which is
        // as fast as a blip's sprite, colour or display could usefully change. Rotation follows a
        // turning player, so it is still set every frame, for the blips the last refresh rotated.
        private const int PlayerBlipsRefreshMs = 100;
        private int playerBlipsRefreshedAt;
        private readonly List<KeyValuePair<int, int>> rotatingPlayerBlips = new(); // blip, ped

        private void RotatePlayerBlips()
        {
            foreach (var entry in rotatingPlayerBlips)
            {
                if (DoesBlipExist(entry.Key))
                {
                    SetBlipRotation(entry.Key, (int)GetEntityHeading(entry.Value));
                }
            }
        }

        /// <summary>
        /// Removes the blip vMenu created for this player, if any. Blips from other resources are never touched.
        /// </summary>
        private void RemoveOwnedPlayerBlip(int serverId)
        {
            blipStyles.Remove(serverId);
            if (ownedPlayerBlips.TryGetValue(serverId, out var blip))
            {
                ownedPlayerBlips.Remove(serverId);
                if (DoesBlipExist(blip))
                {
                    RemoveBlip(ref blip);
                }
            }
        }

        /// <summary>
        /// Server ids whose blip another resource is drawing, as a comma-separated list ("" for none),
        /// pushed by client/dutyBlips.lua.
        /// </summary>
        [EventHandler("vMenu:SetExternalBlipPlayers")]
        public void SetExternalBlipPlayers(string serverIds)
        {
            var players = new HashSet<int>();
            if (!string.IsNullOrEmpty(serverIds))
            {
                foreach (var part in serverIds.Split(','))
                {
                    if (int.TryParse(part, out var serverId) && serverId > 0)
                    {
                        players.Add(serverId);
                    }
                }
            }
            externalBlipPlayers = players;
            externalBlipPlayersUpdatedAt = GetGameTimer();
        }

        private void PruneOwnedPlayerBlips()
        {
            ownedPlayerBlipsPrunedAt = GetGameTimer();

            // forget blips that no longer exist (player left, streamed out or was respawned).
            foreach (var serverId in ownedPlayerBlips.Where(entry => !DoesBlipExist(entry.Value)).Select(entry => entry.Key).ToList())
            {
                ownedPlayerBlips.Remove(serverId);
                blipStyles.Remove(serverId);
            }

            // stop trusting a list the duty script has stopped refreshing.
            if (externalBlipPlayers.Count > 0 && GetGameTimer() - externalBlipPlayersUpdatedAt > ExternalBlipPlayersStaleMs)
            {
                externalBlipPlayers = new HashSet<int>();
            }
        }

        private async Task PlayerBlipsControl()
        {
            // Idle gate: with the blips toggle off this tick still looped every
            // player in scope at frame rate. The body must keep running while
            // off — it deletes leftover blips and keeps our sprite decorator
            // fresh for other players who have blips enabled — but 2Hz is
            // plenty for that. Toggled on, it refreshes 10 times a second and
            // rotates the blips in between (see PlayerBlipsRefreshMs).
            if (MainMenu.MiscSettingsMenu == null || !MainMenu.MiscSettingsMenu.ShowPlayerBlips)
            {
                await Delay(500);
            }
            else if (GetGameTimer() - playerBlipsRefreshedAt < PlayerBlipsRefreshMs)
            {
                RotatePlayerBlips();
                return;
            }
            playerBlipsRefreshedAt = GetGameTimer();
            rotatingPlayerBlips.Clear();

            if (DecorIsRegisteredAsType("vmenu_player_blip_sprite_id", 3))
            {
                var sprite = 1;
                if (IsPedInAnyVehicle(Game.PlayerPed.Handle, false))
                {
                    var veh = GetVehicle();
                    if (veh != null && veh.Exists())
                    {
                        sprite = BlipInfo.GetBlipSpriteForVehicle(veh.Handle);
                    }
                }
                try
                {
                    DecorSetInt(Game.PlayerPed.Handle, "vmenu_player_blip_sprite_id", sprite);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(@"[CRITICAL] A critical bug in one of your scripts was detected. vMenu is unable to set or register a decorator's value because another resource has already registered 1.5k or more decorators. vMenu will NOT work as long as this bug in your other scripts is unsolved. Please fix your other scripts. This is *NOT* caused by or fixable by vMenu!!!");
                    Debug.WriteLine($"Error Location: {e.StackTrace}\nError info: {e.Message}");
                    await Delay(1000);
                }

                if (MainMenu.MiscSettingsMenu != null)
                {
                    var enabled = MainMenu.MiscSettingsMenu.ShowPlayerBlips;

                    if (enabled && GetGameTimer() - ownedPlayerBlipsPrunedAt >= OwnedPlayerBlipsPruneMs)
                    {
                        PruneOwnedPlayerBlips();
                    }

                    foreach (var p in MainMenu.PlayersList)
                    {
                        // continue only if this player is valid.
                        if (p != null && NetworkIsPlayerActive(p.Handle) && p.Character != null && p.Character.Exists())
                        {
                            // if blips are enabled and the player has permisisons to use them.
                            if (enabled)
                            {
                                if (!p.IsLocal)
                                {
                                    var ped = p.Character.Handle;

                                    // the duty blip already covers this player, so drop our own blip.
                                    if (externalBlipPlayers.Contains(p.ServerId))
                                    {
                                        RemoveOwnedPlayerBlip(p.ServerId);
                                        continue;
                                    }

                                    var attachedBlip = GetBlipFromEntity(ped);
                                    int blip;

                                    // keep using our blip for as long as it is still attached to this player.
                                    if (!(ownedPlayerBlips.TryGetValue(p.ServerId, out blip) && attachedBlip > 0 && DoesBlipExist(blip)))
                                    {
                                        RemoveOwnedPlayerBlip(p.ServerId);
                                        blip = attachedBlip;

                                        // if blip id is invalid.
                                        if (blip < 1)
                                        {
                                            blip = AddBlipForEntity(ped);
                                            ownedPlayerBlips[p.ServerId] = blip;
                                        }
                                        // a blip from another resource is on this player; don't restyle it.
                                        else
                                        {
                                            continue;
                                        }
                                    }

                                    // Colour, name and category used to be set on every blip every frame. They are
                                    // now set when the blip (or its sprite) is new; sprite, heading indicator and
                                    // display only when they change. Rotation still follows the player every frame.
                                    if (!blipStyles.TryGetValue(p.ServerId, out var style) || style.Blip != blip)
                                    {
                                        style = new BlipStyle { Blip = blip };
                                        blipStyles[p.ServerId] = style;
                                    }

                                    // only manage the blip for this player if the player is nearby
                                    int blipDisplay;
                                    if (p.Character.Position.DistanceToSquared2D(Game.PlayerPed.Position) < 500000 || Game.IsPaused)
                                    {
                                        int blipSprite;
                                        bool showHeading;
                                        bool rotateBlip;
                                        // if the decorator exists on this player, use the decorator value to determine what the blip sprite should be.
                                        if (DecorExistOn(p.Character.Handle, "vmenu_player_blip_sprite_id"))
                                        {
                                            blipSprite = DecorGetInt(p.Character.Handle, "vmenu_player_blip_sprite_id");
                                            // show heading on blip only if the player is on foot (blip sprite 1)
                                            showHeading = blipSprite == 1;
                                            // set the blip rotation if the player is not in a helicopter (sprite 422).
                                            rotateBlip = blipSprite != 422;
                                        }
                                        else // backup method for when the decorator value is not found.
                                        {
                                            var inVehicle = IsPedInAnyVehicle(ped, false);
                                            blipSprite = inVehicle ? BlipInfo.GetBlipSpriteForVehicle(GetVehiclePedIsIn(ped, false)) : 1;
                                            // only show the heading indicator if the player is NOT in a vehicle.
                                            showHeading = !inVehicle;
                                            // If the player is in a vehicle that is not a helicopter, set the blip rotation.
                                            rotateBlip = inVehicle && !p.Character.IsInHeli;
                                        }

                                        if (style.Sprite != blipSprite)
                                        {
                                            SetBlipSprite(blip, blipSprite);
                                            style.Sprite = blipSprite;

                                            // (re)set the colour and name in case the sprite change reset them.
                                            SetBlipColour(blip, 0);
                                            SetBlipNameToPlayerName(blip, p.Handle);

                                            // thanks lambda menu for hiding this great feature in their source code!
                                            // sets the blip category to 7, which makes the blips group under "Other Players:"
                                            SetBlipCategory(blip, 7);
                                        }
                                        if (style.Heading != showHeading)
                                        {
                                            ShowHeadingIndicatorOnBlip(blip, showHeading);
                                            style.Heading = showHeading;
                                        }
                                        if (rotateBlip)
                                        {
                                            SetBlipRotation(blip, (int)GetEntityHeading(ped));
                                            rotatingPlayerBlips.Add(new KeyValuePair<int, int>(blip, ped));
                                        }

                                        // display on minimap and main map.
                                        blipDisplay = 6;
                                    }
                                    else
                                    {
                                        // hide it from the minimap.
                                        blipDisplay = 3;
                                    }
                                    if (style.Display != blipDisplay)
                                    {
                                        SetBlipDisplay(blip, blipDisplay);
                                        style.Display = blipDisplay;
                                    }
                                }
                            }
                            else // blips are not enabled.
                            {
                                // remove the player blip we created. Blips from other resources (like the duty blip) are left alone.
                                RemoveOwnedPlayerBlip(p.ServerId);
                            }
                        }
                    }
                }
                else // misc settings is null
                {
                    await Delay(1000);
                }
            }
            else // decorator does not exist.
            {
                try
                {
                    DecorRegister("vmenu_player_blip_sprite_id", 3);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(@"[CRITICAL] A critical bug in one of your scripts was detected. vMenu is unable to set or register a decorator's value because another resource has already registered 1.5k or more decorators. vMenu will NOT work as long as this bug in your other scripts is unsolved. Please fix your other scripts. This is *NOT* caused by or fixable by vMenu!!!");
                    Debug.WriteLine($"Error Location: {e.StackTrace}\nError info: {e.Message}");
                    await Delay(1000);
                }
                while (!DecorIsRegisteredAsType("vmenu_player_blip_sprite_id", 3))
                {
                    await Delay(0);
                }
            }
        }

        #endregion

        #region player overhead names
        private readonly OverheadNameTags gamerTags = new();

        // Compared with the squared distance, so 500 means about 22 m.
        private readonly float playerNamesDistance = GetSettingsFloat(Setting.vmenu_player_names_distance) > 10f ? GetSettingsFloat(Setting.vmenu_player_names_distance) : 500f;


        /// <summary>
        /// Manages overhead player names.
        /// </summary>
        /// <returns></returns>
        private async Task PlayerOverheadNamesControl()
        {
            await Delay(500);

            if (MainMenu.MiscSettingsMenu != null)
            {
                var enabled = MainMenu.MiscSettingsMenu.MiscShowOverheadNames;
                if (!enabled)
                {
                    gamerTags.Clear(tag => RemoveMpGamerTag(tag));
                }
                else
                {
                    gamerTags.BeginUpdate();
                    foreach (var p in Players)
                    {
                        if (p != Game.Player && p.Character != null && p.Character.Exists())
                        {
                            var dist = p.Character.Position.DistanceToSquared(Game.PlayerPed.Position);
                            var closeEnough = dist < playerNamesDistance;
                            var tag = gamerTags.Visit(p.ServerId, closeEnough, IsMpGamerTagActive,
                                () => CreateMpGamerTag(p.Character.Handle, p.Name + $" [{p.ServerId}]", false, false, "", 0),
                                RemoveMpGamerTag);
                            if (tag != -1)
                            {
                                SetMpGamerTagVisibility(tag, 2, true); // healthArmor
                                if (p.WantedLevel > 0)
                                {
                                    SetMpGamerTagVisibility(tag, 7, true); // wantedStars
                                    SetMpGamerTagWantedLevel(tag, GetPlayerWantedLevel(p.Handle));
                                }
                                else
                                {
                                    SetMpGamerTagVisibility(tag, 7, false); // wantedStars
                                }
                            }
                        }
                    }
                    gamerTags.EndUpdate(RemoveMpGamerTag);
                }
            }
        }
        #endregion

        #region Key bindings
        // vMenu Enhanced reads keys like these as FiveM key bindings instead of reading game controls
        // every frame. Players can change them in Settings > Key Bindings > FiveM. Each handler keeps
        // the conditions the old every-frame check had. The command names must never change: FiveM
        // saves each player's chosen key under them.
        private static bool keyBindingsRegistered;
        private bool visorHeld;
        private int lastPadPointPress;
        private int driftVehicle;
        private TickSleeper pointingSleeper;
        // The pointing task can take a few frames to start after the key; the steering loop waits.
        private int pointingWokenAt;

        // Keyboard key names for the controls vmenu_teleport_to_wp_keybind_key may hold.
        private static readonly Dictionary<int, string> TeleportKeyNames = new Dictionary<int, string>
        {
            [288] = "F1", [289] = "F2", [170] = "F3", [166] = "F5", [167] = "F6", [168] = "F7",
            [169] = "F8", [56] = "F9", [57] = "F10", [344] = "F11",
        };

        private void RegisterKeyBindings()
        {
            if (keyBindingsRegistered)
            {
                return;
            }
            keyBindingsRegistered = true;
            var id = MainMenu.GetKeyMappingId();

            void Command(string name, Action action)
            {
                RegisterCommand(name, new Action<int, List<object>, string>((source, args, raw) => action()), false);
            }

            // Helmet visor: hold for 400 ms. (Was the game's visor control, read every frame.)
            Command($"+vMenu:{id}:Visor", OnVisorPressed);
            Command($"-vMenu:{id}:Visor", () => visorHeld = false);
            RegisterKeyMapping($"+vMenu:{id}:Visor", "vMenu Helmet Visor (hold)", "keyboard", "F11");
            RegisterKeyMapping($"+vMenu:{id}:Visor", "vMenu Helmet Visor (hold, controller)", "PAD_ANALOGBUTTON", "LRIGHT_INDEX");

            // Snowball pickup, on release, as before. (Was the game's detonate control.)
            Command($"+vMenu:{id}:Snowball", () => { });
            Command($"-vMenu:{id}:Snowball", OnSnowballReleased);
            RegisterKeyMapping($"+vMenu:{id}:Snowball", "vMenu Pick Up Snowball", "keyboard", "G");
            RegisterKeyMapping($"+vMenu:{id}:Snowball", "vMenu Pick Up Snowball (controller)", "PAD_ANALOGBUTTON", "LLEFT_INDEX");

            // Finger point toggle: B, or a double tap of the right stick. Misc Settings > Keybind
            // Settings > Finger Point Controls must be on.
            Command($"vMenu:{id}:Point", () => OnPointKey(false));
            Command($"vMenu:{id}:PointPad", () => OnPointKey(true));
            RegisterKeyMapping($"vMenu:{id}:Point", "vMenu Finger Point Toggle", "keyboard", "B");
            RegisterKeyMapping($"vMenu:{id}:PointPad", "vMenu Finger Point Toggle (controller, double tap)", "PAD_ANALOGBUTTON", "R3_INDEX");
            pointingSleeper = new TickSleeper(PointingTick, t => Tick += t, t => Tick -= t);
            Tick += PointingTick;

            // Expanded minimap toggle, on release, as before. Minimap Controls must be on.
            Command($"+vMenu:{id}:Minimap", () => { });
            Command($"-vMenu:{id}:Minimap", OnMinimapReleased);
            RegisterKeyMapping($"+vMenu:{id}:Minimap", "vMenu Expanded Minimap Toggle", "keyboard", "Z");
            RegisterKeyMapping($"+vMenu:{id}:Minimap", "vMenu Expanded Minimap Toggle (controller)", "PAD_ANALOGBUTTON", "LDOWN_INDEX");

            // Drift mode: less grip while held. Drift Mode must be on.
            Command($"+vMenu:{id}:Drift", () => SetDriftGrip(true));
            Command($"-vMenu:{id}:Drift", () => SetDriftGrip(false));
            RegisterKeyMapping($"+vMenu:{id}:Drift", "vMenu Drift Mode (hold)", "keyboard", "LSHIFT");
            RegisterKeyMapping($"+vMenu:{id}:Drift", "vMenu Drift Mode (hold, controller)", "PAD_ANALOGBUTTON", "RLEFT_INDEX");

            // Teleport to waypoint, keyboard only as before. Its default key still comes from
            // vmenu_teleport_to_wp_keybind_key (a control number, 168 = F7).
            Command($"vMenu:{id}:TpToWaypoint", OnTeleportToWaypointKey);
            var tpControl = MainMenu.MiscSettingsMenu != null ? MainMenu.MiscSettingsMenu.KbTpToWaypointKey : 168;
            var tpKey = TeleportKeyNames.TryGetValue(tpControl, out var keyName) ? keyName : "F7";
            RegisterKeyMapping($"vMenu:{id}:TpToWaypoint", "vMenu Teleport To Waypoint", "keyboard", tpKey);
        }

        // The state the old every-frame checks required before acting on a key.
        private static bool WorldKeysAllowed()
        {
            return !(MenuController.IsAnyMenuOpen() || MainMenu.DontOpenMenus || !Fading.IsFadedIn || Game.IsPaused || IsPlayerSwitchInProgress() || Game.PlayerPed.IsDead);
        }

        private async void OnVisorPressed()
        {
            if (visorHeld)
            {
                return;
            }
            visorHeld = true;
            try
            {
                var timer = GetGameTimer();
                while (visorHeld && WorldKeysAllowed())
                {
                    await Delay(0);
                    var veh = GetVehicle();
                    var inVeh = veh != null && (veh.Model.IsBike || veh.Model.IsBicycle || veh.Model.IsQuadbike);
                    // On a bike the controller key is also the headlight key.
                    if (GetGameTimer() - timer > 380 && inVeh)
                    {
                        Game.DisableControlThisFrame(2, Control.VehicleHeadlight);
                    }
                    if (GetGameTimer() - timer > 400)
                    {
                        var t = SwitchHelmetOnce();
                        while (!t.IsCompleted && !t.IsCanceled && !t.IsFaulted)
                        {
                            if (inVeh)
                            {
                                Game.DisableControlThisFrame(2, Control.VehicleHeadlight);
                            }
                            await Delay(0);
                        }
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[vMenu] visor key: {e.Message}");
            }
        }

        private async void OnSnowballReleased()
        {
            try
            {
                if (!WorldKeysAllowed() || !EventManager.IsSnowEnabled || !IsAllowed(Permission.WPSnowball))
                {
                    return;
                }
                if (!(Game.PlayerPed.IsInVehicle() || Game.PlayerPed.IsDead || !Fading.IsFadedIn || IsPlayerSwitchInProgress() || Game.IsPaused
                    || GetInteriorFromEntity(Game.PlayerPed.Handle) != 0 || !Game.PlayerPed.IsOnFoot || Game.PlayerPed.IsInParachuteFreeFall ||
                    Game.PlayerPed.IsFalling || Game.PlayerPed.IsBeingStunned || Game.PlayerPed.IsWalking || Game.PlayerPed.IsRunning ||
                    Game.PlayerPed.IsSprinting || Game.PlayerPed.IsSwimming || Game.PlayerPed.IsSwimmingUnderWater || (Game.PlayerPed.IsDiving && GetSelectedPedWeapon(Game.PlayerPed.Handle) == snowball_hash) || GetSelectedPedWeapon(Game.PlayerPed.Handle) == GetHashKey("unarmed")))
                {
                    await PickupSnowballOnce();
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[vMenu] snowball key: {e.Message}");
            }
        }

        private async void OnPointKey(bool controller)
        {
            try
            {
                var misc = MainMenu.MiscSettingsMenu;
                if (misc == null || !misc.KbPointKeys || IsPauseMenuActive() || Game.PlayerPed.IsInVehicle())
                {
                    return;
                }
                // Best effort: a script that disables the point control (for example while cuffed)
                // still blocks it, as when vMenu read the control itself.
                if (!Game.IsControlEnabled(0, Control.SpecialAbilitySecondary))
                {
                    return;
                }
                if (controller)
                {
                    // Double tap within 300 ms.
                    var now = GetGameTimer();
                    var doubleTap = now - lastPadPointPress < 300;
                    lastPadPointPress = now;
                    if (!doubleTap)
                    {
                        return;
                    }
                }
                else if (UpdateOnscreenKeyboard() == 0)
                {
                    // Typing in the game's onscreen keyboard.
                    return;
                }

                if (IsPedPointing(Game.PlayerPed.Handle))
                {
                    ClearPedSecondaryTask(Game.PlayerPed.Handle);
                    return;
                }
                if (!await LoadAnimDictAsync("anim@mp_point"))
                {
                    return;
                }
                TaskMoveNetwork(Game.PlayerPed.Handle, "task_mp_pointing", 0.5f, false, "anim@mp_point", 24);
                RemoveAnimDict("anim@mp_point");
                pointingWokenAt = GetGameTimer();
                pointingSleeper?.Wake();
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[vMenu] point key: {e.Message}");
            }
        }

        /// <summary>
        /// Steers the pointing animation every frame while pointing started by the point key, and
        /// stops pointing in a vehicle. Off the tick list the rest of the time.
        /// </summary>
        private async Task PointingTick()
        {
            var ped = PlayerPedId();
            if (!IsPedPointing(ped))
            {
                // Not pointing yet right after the key: give the task half a second to start.
                if (GetGameTimer() - pointingWokenAt >= 500)
                {
                    pointingSleeper?.Sleep();
                }
                return;
            }
            if (IsPedInAnyVehicle(ped, false))
            {
                ClearPedSecondaryTask(ped);
                return;
            }
            SetTaskMoveNetworkSignalFloat(ped, "Pitch", GetPointingPitch());
            SetTaskMoveNetworkSignalFloat(ped, "Heading", GetPointingHeading());
            SetTaskMoveNetworkSignalBool(ped, "isBlocked", GetPointingIsBlocked());
            SetTaskMoveNetworkSignalBool(ped, "isFirstPerson", GetFollowPedCamViewMode() == 4);
            SetTaskMoveNetworkSignalFloat(ped, "Speed", 0.25f);
            await Task.FromResult(0);
        }

        private void OnMinimapReleased()
        {
            var misc = MainMenu.MiscSettingsMenu;
            // Noclip uses Z to go down, and disables the game control vMenu used to read.
            if (misc == null || !misc.KbRadarKeys || expandedRadarProfile || MenuController.IsAnyMenuOpen() || IsPauseMenuActive()
                || NoClip.IsNoclipActive() || !Game.IsControlEnabled(0, Control.MultiplayerInfo))
            {
                return;
            }
            if (IsBigmapActive())
            {
                SetBigmapActive(false, false);
                vMenuOpenedBigmap = false;
            }
            else
            {
                SetBigmapActive(true, false);
                radarSwitchTimer = GetGameTimer();
                vMenuOpenedBigmap = true;
            }
        }

        private void SetDriftGrip(bool held)
        {
            if (!held)
            {
                if (driftVehicle != 0 && DoesEntityExist(driftVehicle))
                {
                    SetVehicleReduceGrip(driftVehicle, false);
                }
                driftVehicle = 0;
                return;
            }
            var misc = MainMenu.MiscSettingsMenu;
            if (misc == null || !misc.KbDriftMode || !IsAllowed(Permission.MSDriftMode) || IsPauseMenuActive() || NoClip.IsNoclipActive()
                || !Game.PlayerPed.IsInVehicle())
            {
                return;
            }
            var veh = GetVehicle();
            if (veh != null && veh.Exists() && !veh.IsDead)
            {
                SetVehicleReduceGrip(veh.Handle, true);
                driftVehicle = veh.Handle;
            }
        }

        private void OnTeleportToWaypointKey()
        {
            var misc = MainMenu.MiscSettingsMenu;
            if (misc == null || !misc.KbTpToWaypoint || !IsAllowed(Permission.MSTeleportToWp) || !Fading.IsFadedIn || IsPlayerSwitchInProgress()
                || IsPauseMenuActive() || MenuController.IsAnyMenuOpen())
            {
                return;
            }
            if (Game.IsWaypointActive)
            {
                TeleportToWp();
                Notify.Success("Teleported to waypoint.");
            }
            else
            {
                Notify.Error("You need to set a waypoint first.");
            }
        }
        #endregion

        #region help message controller
        /// <summary>
        /// Help message timer and stuff.
        /// </summary>
        /// <returns></returns>
        private async Task SnowballPickupHelpMessageTask()
        {
            if (EventManager.IsSnowEnabled)
            {
                void ShowSnowballInfoMessage()
                {
                    var maxAmmo = 10;
                    GetMaxAmmo(Game.PlayerPed.Handle, snowball_hash, ref maxAmmo);
                    if (maxAmmo > GetAmmoInPedWeapon(Game.PlayerPed.Handle, snowball_hash))
                    {
                        BeginTextCommandDisplayHelp("HELP_SNOWP");
                        AddTextComponentInteger(2);
                        AddTextComponentInteger(maxAmmo);
                        EndTextCommandDisplayHelp(0, false, true, 6000);
                    }
                }

                // help text control for snowballs
                if (!IsPedInAnyVehicle(Game.PlayerPed.Handle, true))
                {
                    if (showSnowballInfo)
                    {
                        BeginTextCommandIsThisHelpMessageBeingDisplayed("HELP_SNOWP");
                        if (EndTextCommandIsThisHelpMessageBeingDisplayed(0))
                        {
                            showSnowballInfo = false;
                            return;
                        }
                        else if (IsHelpMessageBeingDisplayed())
                        {
                            ClearAllHelpMessages();
                        }
                        ShowSnowballInfoMessage();
                    }
                    showSnowballInfo = false;
                }
                else
                {
                    showSnowballInfo = true;
                }
            }
            await Delay(100);
        }
        #endregion

        #region draw model dimensions
        /// <summary>
        /// Draws entity outlines if enabled (per entity type).
        /// </summary>
        /// <returns></returns>
        private async Task ModelDrawDimensions()
        {
            // Everything below draws inside one of the three outline options. While a menu is open
            // this checks every frame, so switching one on shows at once.
            if (!OutlinesWanted())
            {
                if (!AnyMenuOpen)
                {
                    await Rest(IdleRestMs);
                }
                return;
            }

            if (MainMenu.PermissionsSetupComplete && MainMenu.MiscSettingsMenu != null)
            {
                // Vehicles
                if (MainMenu.MiscSettingsMenu.ShowVehicleModelDimensions)
                {
                    foreach (var v in vehicles)
                    {
                        if (stopVehiclesLoop)
                        {
                            break;
                        }

                        DrawEntityBoundingBox(v, 250, 150, 0, 100);

                        if (MainMenu.MiscSettingsMenu.ShowEntityHandles && v.IsOnScreen)
                        {
                            SetDrawOrigin(v.Position.X, v.Position.Y, v.Position.Z, 0);
                            DrawTextOnScreen($"Veh {v.Handle}", 0f, 0f, 0.3f, Alignment.Center, 0);
                            ClearDrawOrigin();
                        }
                        if (MainMenu.MiscSettingsMenu.ShowEntityModels && v.IsOnScreen)
                        {
                            SetDrawOrigin(v.Position.X, v.Position.Y, v.Position.Z - 0.3f, 0);
                            var model = GetEntityModel(v.Handle);

                            var hashes = $"{model} / {(uint)model} / 0x{model:X8}";

                            DrawTextOnScreen($"Hash {hashes}", 0f, 0f, 0.3f, Alignment.Center, 0);
                            ClearDrawOrigin();
                        }
                        if (MainMenu.MiscSettingsMenu.ShowEntityNetOwners && v.IsOnScreen)
                        {
                            var netOwnerLocalId = NetworkGetEntityOwner(v.Handle);

                            if (netOwnerLocalId != 0)
                            {
                                var playerServerId = GetPlayerServerId(netOwnerLocalId);
                                var playerName = GetPlayerName(netOwnerLocalId);
                                SetDrawOrigin(v.Position.X, v.Position.Y, v.Position.Z + 0.3f, 0);
                                DrawTextOnScreen($"Owner ID {playerServerId} ({playerName})", 0f, 0f, 0.3f, Alignment.Center, 0);
                                ClearDrawOrigin();
                            }
                        }
                    }
                }

                // Props
                if (MainMenu.MiscSettingsMenu.ShowPropModelDimensions)
                {
                    foreach (var p in props)
                    {
                        if (stopPropsLoop)
                        {
                            break;
                        }

                        DrawEntityBoundingBox(p, 255, 0, 0, 100);

                        if (MainMenu.MiscSettingsMenu.ShowEntityHandles && p.IsOnScreen)
                        {
                            SetDrawOrigin(p.Position.X, p.Position.Y, p.Position.Z, 0);
                            DrawTextOnScreen($"Prop {p.Handle}", 0f, 0f, 0.3f, Alignment.Center, 0);
                            ClearDrawOrigin();
                        }

                        if (MainMenu.MiscSettingsMenu.ShowEntityModels && p.IsOnScreen)
                        {
                            SetDrawOrigin(p.Position.X, p.Position.Y, p.Position.Z - 0.3f, 0);
                            var model = GetEntityModel(p.Handle);

                            var hashes = $"{model} / {(uint)model} / 0x{model:X8}";

                            DrawTextOnScreen($"Hash {hashes}", 0f, 0f, 0.3f, Alignment.Center, 0);
                            ClearDrawOrigin();
                        }

                        if (MainMenu.MiscSettingsMenu.ShowEntityNetOwners && p.IsOnScreen)
                        {
                            var netOwnerLocalId = NetworkGetEntityOwner(p.Handle);

                            if (netOwnerLocalId != 0)
                            {
                                var playerServerId = GetPlayerServerId(netOwnerLocalId);
                                var playerName = GetPlayerName(netOwnerLocalId);
                                SetDrawOrigin(p.Position.X, p.Position.Y, p.Position.Z + 0.3f, 0);
                                DrawTextOnScreen($"Owner ID {playerServerId} ({playerName})", 0f, 0f, 0.3f, Alignment.Center, 0);
                                ClearDrawOrigin();
                            }
                        }
                    }
                }

                // Peds
                if (MainMenu.MiscSettingsMenu.ShowPedModelDimensions)
                {
                    foreach (var p in peds)
                    {
                        if (stopPedsLoop)
                        {
                            break;
                        }

                        DrawEntityBoundingBox(p, 50, 255, 50, 100);

                        if (MainMenu.MiscSettingsMenu.ShowEntityHandles && p.IsOnScreen)
                        {
                            SetDrawOrigin(p.Position.X, p.Position.Y, p.Position.Z, 0);
                            DrawTextOnScreen($"Ped {p.Handle}", 0f, 0f, 0.3f, Alignment.Center, 0);
                            ClearDrawOrigin();
                        }

                        if (MainMenu.MiscSettingsMenu.ShowEntityModels && p.IsOnScreen)
                        {
                            SetDrawOrigin(p.Position.X, p.Position.Y, p.Position.Z - 0.3f, 0);
                            var model = GetEntityModel(p.Handle);

                            var hashes = $"{model} / {(uint)model} / 0x{model:X8}";

                            DrawTextOnScreen($"Hash {hashes}", 0f, 0f, 0.3f, Alignment.Center, 0);
                            ClearDrawOrigin();
                        }

                        if (MainMenu.MiscSettingsMenu.ShowEntityNetOwners && p.IsOnScreen)
                        {
                            var netOwnerLocalId = NetworkGetEntityOwner(p.Handle);

                            if (netOwnerLocalId != 0)
                            {
                                var playerServerId = GetPlayerServerId(netOwnerLocalId);
                                var playerName = GetPlayerName(netOwnerLocalId);
                                SetDrawOrigin(p.Position.X, p.Position.Y, p.Position.Z + 0.3f, 0);
                                DrawTextOnScreen($"Owner ID {playerServerId} ({playerName})", 0f, 0f, 0.3f, Alignment.Center, 0);
                                ClearDrawOrigin();
                            }
                        }
                    }
                }
            }
            else
            {
                await Task.FromResult(0);
            }
        }
        #endregion

        #region animal ped camera change blocker
        /// <summary>
        /// Prevents players from going into first person when they're currently using an animal as their player ped.
        /// This is to prevent them crashing their game or falling out of the sky as ~~birds~~ bricks.
        /// Runs every frame, from PerFrameChecks: first person stays blocked for every frame the ped is an animal.
        /// </summary>
        // Whether the last check found the player using an animal ped.
        private bool playerIsAnimal;

        private void AnimalPedCameraChangeBlocker()
        {
            playerIsAnimal = AnimalHashes.Contains((uint)GetEntityModel(PlayerPedId()));
            if (playerIsAnimal)
            {
                DisableFirstPersonCamThisFrame();
            }
        }
        #endregion

        #region per-frame checks
        // Small checks that must run every frame. As separate ticks, each cost the C# scheduler a task
        // start every frame (and DisableMovement and the animal blocker a Delay(0) resume as well), which
        // was more than the checks themselves. They now share this one tick. Each is caught on its own,
        // so one failing doesn't stop the others, as when they were separate ticks.
        private bool vehicleHealthCheckEnabled;
        private bool highbeamFlashCheckEnabled;
        private bool movementLockCheckEnabled;
        private bool animalCameraCheckEnabled;

        private async Task PerFrameChecks()
        {
            if (vehicleHealthCheckEnabled)
            {
                try { VehicleShowHealthOnScreen(); } catch (Exception e) { LogPerFrameCheckError(nameof(VehicleShowHealthOnScreen), e); }
            }
            if (highbeamFlashCheckEnabled)
            {
                try { VehicleHighbeamFlash(); } catch (Exception e) { LogPerFrameCheckError(nameof(VehicleHighbeamFlash), e); }
            }
            if (movementLockCheckEnabled)
            {
                try { DisableMovement(); } catch (Exception e) { LogPerFrameCheckError(nameof(DisableMovement), e); }
            }
            if (animalCameraCheckEnabled)
            {
                try { AnimalPedCameraChangeBlocker(); } catch (Exception e) { LogPerFrameCheckError(nameof(AnimalPedCameraChangeBlocker), e); }
            }

            // These checks only act every frame in a vehicle (health text, high beams), in the
            // character editor (which is a menu) or as an animal. Otherwise this rests; getting in
            // counts as in a vehicle, so the text and high beams are on by the time you are seated.
            var vehicleChecks = MainMenu.VehicleOptionsMenu != null
                && ((vehicleHealthCheckEnabled && MainMenu.VehicleOptionsMenu.VehicleShowHealth)
                    || (highbeamFlashCheckEnabled && MainMenu.VehicleOptionsMenu.FlashHighbeamsOnHonk));
            var everyFrame = AnyMenuOpen
                || (animalCameraCheckEnabled && playerIsAnimal)
                || (vehicleChecks && IsPedInAnyVehicle(PlayerPedId(), true));
            if (!everyFrame)
            {
                await Rest(StateRestMs);
            }
        }

        private static void LogPerFrameCheckError(string check, Exception e)
        {
            Debug.WriteLine($"[vMenu] {check} failed: {e}");
        }
        #endregion

        #region Slow misc tick
        internal static float entityRange = 2000f;
        /// <summary>
        /// Slow functions for the model dimensions outline entities lists.
        /// </summary>
        /// <returns></returns>
        private static bool OutlinesWanted() =>
            MainMenu.PermissionsSetupComplete && MainMenu.MiscSettingsMenu != null
            && (MainMenu.MiscSettingsMenu.ShowPropModelDimensions
                || MainMenu.MiscSettingsMenu.ShowPedModelDimensions
                || MainMenu.MiscSettingsMenu.ShowVehicleModelDimensions);

        private async Task SlowMiscTick()
        {
            const int delay = 50;
            // With every outline option off this returned at once, so it ran every frame. It still
            // does while a menu is open, so switching one on is picked up at once.
            if (!OutlinesWanted())
            {
                if (!AnyMenuOpen)
                {
                    await Rest(IdleRestMs);
                }
                return;
            }

            if (MainMenu.PermissionsSetupComplete && MainMenu.MiscSettingsMenu != null)
            {
                var pp = Game.PlayerPed.Position;
                if (MainMenu.MiscSettingsMenu.ShowPropModelDimensions)
                {
                    stopPropsLoop = true;
                    props = World.GetAllProps().Where(e => e.IsOnScreen && e.Position.DistanceToSquared(pp) < entityRange).ToList();
                    stopPropsLoop = false;

                    await Delay(delay);
                }

                if (MainMenu.MiscSettingsMenu.ShowPedModelDimensions)
                {
                    stopPedsLoop = true;
                    peds = World.GetAllPeds().Where(e => e.IsOnScreen && e.Position.DistanceToSquared(pp) < entityRange).ToList();
                    stopPedsLoop = false;

                    await Delay(delay);
                }

                if (MainMenu.MiscSettingsMenu.ShowVehicleModelDimensions)
                {
                    stopVehiclesLoop = true;
                    vehicles = World.GetAllVehicles().Where(e => e.IsOnScreen && e.Position.DistanceToSquared(pp) < entityRange).ToList();
                    stopVehiclesLoop = false;

                    await Delay(delay);
                }

            }
        }
        #endregion

        #region Personal Vehicle options
        private readonly PersonalVehicleAutoLock pvAutoLock = new();

        /// <summary>
        /// Personal Vehicle > Auto-Lock When I Walk Away, checked twice a second.
        /// </summary>
        private async Task PersonalVehicleAutoLockTick()
        {
            await Delay(500);
            var personalVehicleMenu = MainMenu.PersonalVehicleMenu;
            var veh = personalVehicleMenu?.CurrentPersonalVehicle;
            if (!MainMenu.PermissionsSetupComplete || veh == null || !veh.Exists() || veh.IsDead)
            {
                return;
            }
            var distance = Game.PlayerPed.Position.DistanceToSquared(veh.Position);
            var occupied = GetVehicleNumberOfPassengers(veh.Handle) > 0 || !IsVehicleSeatFree(veh.Handle, -1);
            if (pvAutoLock.Update(personalVehicleMenu.AutoLock, (float)Math.Sqrt(distance), occupied, IsVehicleLocked(veh)))
            {
                await LockOrUnlockDoors(veh, true);
            }
        }

        private bool didShowPvHelpMessage = false;
        private int? pvHornPressFrame;

        private async Task AutoPVBlipOnExitTick()
        {
            if (!MainMenu.PermissionsSetupComplete || MainMenu.PersonalVehicleMenu == null)
            {
                await Delay(200);
                return;
            }

            var personalVehicleMenu = MainMenu.PersonalVehicleMenu;
            var personalVehicle = personalVehicleMenu.CurrentPersonalVehicle;

            if (personalVehicle == null)
            {
                await Delay(200);
                return;
            }

            if (!personalVehicle.Exists())
            {
                // The vehicle streamed out (e.g. the player walked deep into an
                // interior); the entity handle is dead but the vehicle may still
                // exist on the server. Keep the selection and re-resolve it from
                // its network id once it streams back in.
                personalVehicleMenu.TryReacquireCurrentPersonalVehicle();
                await Delay(200);
                return;
            }

            if (personalVehicle.IsDead)
            {
                personalVehicleMenu.ClearCurrentPersonalVehicle();
                await Delay(200);
                return;
            }

            personalVehicleMenu.LastKnownPosition = personalVehicle.Position;

            var driver = personalVehicle.Driver;
            var hasDriver = driver != null && driver.Exists();
            if (hasDriver)
            {
                // Blips should only exist while there is no driver in the vehicle.
                personalVehicleMenu.RemoveCurrentPersonalVehicleBlip();

                if (driver != Game.PlayerPed && driver.IsPlayer)
                {
                    personalVehicleMenu.ClearCurrentPersonalVehicle();
                    await Delay(200);
                    return;
                }
            }
            else if (personalVehicleMenu.BlipOnExit)
            {
                personalVehicleMenu.EnsureCurrentPersonalVehicleBlip();
            }
            else
            {
                personalVehicleMenu.RemoveCurrentPersonalVehicleBlip();
            }

            await Delay(100);
        }

        /// <summary>
        /// Manages personal vehicle options like locking doors while close.
        /// </summary>
        /// <returns></returns>
        private async Task PersonalVehicleOptions()
        {
            var personalVehicleMenu = MainMenu.PersonalVehicleMenu;
            var veh = personalVehicleMenu?.CurrentPersonalVehicle;
            var ped = Game.PlayerPed;
            if (!MainMenu.PermissionsSetupComplete || veh == null || !veh.Exists()
                || ped.IsInVehicle(veh) || ped.IsGettingIntoAVehicle
                || ped.Position.DistanceToSquared(veh.Position) >= 650.0f)
            {
                pvHornPressFrame = null;
                personalVehicleMenu?.ResetHornLockTap();
                await Delay(100);
                return;
            }

            if (Game.IsControlJustPressed(0, Control.VehicleHorn))
            {
                pvHornPressFrame = GetFrameCount();
            }
            if (Game.IsControlJustReleased(0, Control.VehicleHorn) && pvHornPressFrame is { } inputFrame)
            {
                pvHornPressFrame = null;
                await personalVehicleMenu.HandleLockKeyTap(inputFrame, fromHorn: true);
            }
            if (!didShowPvHelpMessage)
            {
                didShowPvHelpMessage = true;
                HelpMessage.Custom("When you are close to your personal vehicle, you can double tap ~INPUT_VEH_HORN~ to lock or unlock it.", 10000, true);
            }
        }
        #endregion

        #region animation functions
        /// <summary>
        /// This triggers a helmet visor/goggles toggle if available.
        /// THIS IS NOT A TICK FUNCTION
        /// </summary>
        /// <returns></returns>
        private async Task SwitchHelmetOnce()
        {
            if (MainMenu.PermissionsSetupComplete)
            {
                var component = GetPedPropIndex(Game.PlayerPed.Handle, 0);      // helmet index
                var texture = GetPedPropTextureIndex(Game.PlayerPed.Handle, 0); // texture
                var compHash = GetHashNameForProp(Game.PlayerPed.Handle, 0, component, texture); // prop combination hash
                if (GetShopPedApparelVariantPropCount((uint)compHash) > 0) // helmet has visor.
                {
                    var newHelmet = component;
                    var newHelmetTexture = texture;

                    var newHelmetData = Game.GetAltPropVariationData(Game.PlayerPed.Handle, 0);

                    Log(JsonConvert.SerializeObject(newHelmetData, Formatting.Indented));

                    if (newHelmetData != null && newHelmetData.Length > 0)
                    {
                        newHelmet = newHelmetData[0].altPropVariationIndex;
                        newHelmetTexture = newHelmetData[0].altPropVariationTexture;
                    }

                    var animName = component < newHelmet ? "visor_up" : "visor_down";
                    if (Game.PlayerPed.Model == PedHash.FreemodeFemale01)
                    {
                        if (component is 66 or 81)
                        {
                            animName = component > newHelmet ? "visor_up" : "visor_down";
                        }
                        if (component is >= 115 and <= 118)
                        {
                            animName = component < newHelmet ? "goggles_up" : "goggles_down";
                        }
                    }
                    else
                    {
                        if (component is 67 or 82)
                        {
                            animName = component > newHelmet ? "visor_up" : "visor_down";
                        }
                        if (component is >= 116 and <= 119)
                        {
                            animName = component < newHelmet ? "goggles_up" : "goggles_down";
                        }
                    }

                    var animDict = "anim@mp_helmets@on_foot";

                    if (GetFollowPedCamViewMode() == 4)
                    {
                        if (animName.Contains("goggles"))
                        {
                            animName = animName.Replace("goggles", "visor");
                        }
                        animName = "pov_" + animName;
                    }
                    if (Game.PlayerPed.IsInVehicle())
                    {
                        if (animName.Contains("goggles"))
                        {
                            ClearAllHelpMessages();
                            BeginTextCommandDisplayHelp("string");
                            AddTextComponentSubstringPlayerName("You can not toggle your goggles while in a vehicle.");
                            EndTextCommandDisplayHelp(0, false, true, 6000);
                            return;
                        }
                        var veh = GetVehicle();
                        if (veh != null && veh.Exists() && !veh.IsDead && (veh.Model.IsBicycle || veh.Model.IsBike || veh.Model.IsQuadbike))
                        {
                            if (veh.Model.IsQuadbike)
                            {
                                animDict = "anim@mp_helmets@on_bike@quad";
                            }
                            else if (veh.Model.IsBike)
                            {
                                var sportBikes = new List<uint>()
                                {
                                    (uint)GetHashKey("AKUMA"),
                                    (uint)GetHashKey("BATI"),
                                    (uint)GetHashKey("BATI2"),
                                    (uint)GetHashKey("CARBONRS"),
                                    (uint)GetHashKey("DEFILER"),
                                    (uint)GetHashKey("DIABLOUS2"),
                                    (uint)GetHashKey("DOUBLE"),
                                    (uint)GetHashKey("FCR"),
                                    (uint)GetHashKey("FCR2"),
                                    (uint)GetHashKey("HAKUCHOU"),
                                    (uint)GetHashKey("HAKUCHOU2"),
                                    (uint)GetHashKey("LECTRO"),
                                    (uint)GetHashKey("NEMESIS"),
                                    (uint)GetHashKey("OPPRESSOR"),
                                    (uint)GetHashKey("OPPRESSOR2"),
                                    (uint)GetHashKey("PCJ"),
                                    (uint)GetHashKey("RUFFIAN"),
                                    (uint)GetHashKey("SHOTARO"),
                                    (uint)GetHashKey("VADER"),
                                    (uint)GetHashKey("VORTEX"),
                                };
                                var chopperBikes = new List<uint>()
                                {
                                    (uint)GetHashKey("SANCTUS"),
                                    (uint)GetHashKey("ZOMBIEA"),
                                    (uint)GetHashKey("ZOMBIEB"),
                                };
                                var dirtBikes = new List<uint>()
                                {
                                    (uint)GetHashKey("BF400"),
                                    (uint)GetHashKey("ENDURO"),
                                    (uint)GetHashKey("MANCHEZ"),
                                    (uint)GetHashKey("SANCHEZ"),
                                    (uint)GetHashKey("SANCHEZ2"),
                                    (uint)GetHashKey("ESSKEY"),
                                };
                                var scooters = new List<uint>()
                                {
                                    (uint)GetHashKey("FAGGIO"),
                                    (uint)GetHashKey("FAGGIO2"),
                                    (uint)GetHashKey("FAGGIO3"),
                                    (uint)GetHashKey("CLIFFHANGER"),
                                    (uint)GetHashKey("BAGGER"),
                                };
                                var policeb = new List<uint>()
                                {
                                    (uint)GetHashKey("AVARUS"),
                                    (uint)GetHashKey("CHIMERA"),
                                    (uint)GetHashKey("POLICEB"),
                                    (uint)GetHashKey("SOVEREIGN"),
                                    (uint)GetHashKey("HEXER"),
                                    (uint)GetHashKey("INNOVATION"),
                                    (uint)GetHashKey("NIGHTBLADE"),
                                    (uint)GetHashKey("RATBIKE"),
                                    (uint)GetHashKey("DAEMON"),
                                    (uint)GetHashKey("DAEMON2"),
                                    (uint)GetHashKey("DIABLOUS"),
                                    (uint)GetHashKey("GARGOYLE"),
                                    (uint)GetHashKey("THRUST"),
                                    (uint)GetHashKey("VINDICATOR"),
                                    (uint)GetHashKey("WOLFSBANE"),
                                };

                                if (policeb.Contains((uint)veh.Model.Hash))
                                {
                                    animDict = "anim@mp_helmets@on_bike@policeb";
                                }
                                else if (sportBikes.Contains((uint)veh.Model.Hash))
                                {
                                    animDict = "anim@mp_helmets@on_bike@sports";
                                }
                                else if (chopperBikes.Contains((uint)veh.Model.Hash))
                                {
                                    animDict = "anim@mp_helmets@on_bike@chopper";
                                }
                                else if (dirtBikes.Contains((uint)veh.Model.Hash))
                                {
                                    animDict = "anim@mp_helmets@on_bike@dirt";
                                }
                                else if (scooters.Contains((uint)veh.Model.Hash))
                                {
                                    animDict = "anim@mp_helmets@on_bike@scooter";
                                }
                                else
                                {
                                    animDict = "anim@mp_helmets@on_bike@sports";
                                }
                            }
                            else if (veh.Model.IsBicycle)
                            {
                                animDict = "anim@mp_helmets@on_bike@scooter";
                            }
                        }
                    }
                    if (!await LoadAnimDictAsync(animDict))
                    {
                        return;
                    }
                    if (animName.StartsWith("pov_") && animDict != "anim@mp_helmets@on_foot")
                    {
                        animName = animName.Substring(4);
                    }
                    ClearPedTasks(Game.PlayerPed.Handle);
                    TaskPlayAnim(Game.PlayerPed.Handle, animDict, animName, 8.0f, 1.0f, -1, 48, 0.0f, false, false, false);
                    var timeoutTimer = GetGameTimer();
                    while (GetEntityAnimCurrentTime(Game.PlayerPed.Handle, animDict, animName) <= 0.0f)
                    {
                        if (GetGameTimer() - timeoutTimer > 1000)
                        {
                            ClearPedTasks(Game.PlayerPed.Handle);
                            Debug.WriteLine("[vMenu] [WARNING] Waiting for animation to start took too long. Preventing hanging of function. Dbg: fault in location 1.");
                            return;
                        }
                        await Delay(0);
                    }
                    timeoutTimer = GetGameTimer();
                    while (GetEntityAnimCurrentTime(Game.PlayerPed.Handle, animDict, animName) > 0.0f)
                    {
                        await Delay(0);

                        if (GetGameTimer() - timeoutTimer > 3000)
                        {
                            ClearPedTasks(Game.PlayerPed.Handle);
                            Debug.WriteLine("[vMenu] [WARNING] Waiting for animation duration took too long. Preventing hanging of function. Dbg: fault in location 2.");
                            return;
                        }
                        if (GetEntityAnimCurrentTime(Game.PlayerPed.Handle, animDict, animName) > 0.39f)
                        {
                            SetPedPropIndex(Game.PlayerPed.Handle, 0, newHelmet, newHelmetTexture, true);
                        }
                    }
                    ClearPedTasks(Game.PlayerPed.Handle);
                    RemoveAnimDict(animDict);
                }
            }
        }

        /// <summary>
        /// Pickup a snowball.
        /// THIS IS NOT A TICK FUNCTION
        /// </summary>
        /// <returns></returns>
        private async Task PickupSnowballOnce()
        {
            if (MainMenu.PermissionsSetupComplete)
            {
                ClearPedTasks(Game.PlayerPed.Handle);
                var maxAmmo = 10;
                GetMaxAmmo(Game.PlayerPed.Handle, snowball_hash, ref maxAmmo);
                if (GetAmmoInPedWeapon(Game.PlayerPed.Handle, snowball_hash) < maxAmmo)
                {
                    SetPedCurrentWeaponVisible(Game.PlayerPed.Handle, false, true, false, false);
                    if (!await LoadAnimDictAsync(snowball_anim_dict))
                    {
                        return;
                    }
                    TaskPlayAnim(Game.PlayerPed.Handle, snowball_anim_dict, snowball_anim_name, 8f, 1f, -1, 0, 0f, false, false, false);
                    var fired = false;

                    var dur = GetAnimDuration(snowball_anim_dict, snowball_anim_name);
                    var timer = GetGameTimer();
                    while (GetEntityAnimCurrentTime(Game.PlayerPed.Handle, snowball_anim_dict, snowball_anim_name) < 0.97f)
                    {
                        await Delay(0);
                        if (!fired)
                        {
                            if (HasAnimEventFired(Game.PlayerPed.Handle, (uint)GetHashKey("CreateObject")))
                            {
                                AddAmmoToPed(Game.PlayerPed.Handle, snowball_hash, 2);
                                GiveWeaponToPed(Game.PlayerPed.Handle, snowball_hash, 0, true, true);
                                if (GetAmmoInPedWeapon(Game.PlayerPed.Handle, snowball_hash) > maxAmmo)
                                {
                                    SetPedAmmo(Game.PlayerPed.Handle, snowball_hash, maxAmmo);
                                }
                                fired = true;
                            }
                            else if (HasAnimEventFired(Game.PlayerPed.Handle, (uint)GetHashKey("Interrupt")))
                            {
                                break;
                            }
                        }
                        else if (HasAnimEventFired(Game.PlayerPed.Handle, (uint)GetHashKey("Interrupt")))
                        {
                            break;
                        }
                        // fail safe just in case
                        if (GetGameTimer() - timer > (dur * 1000f))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    ClearAllHelpMessages();
                    BeginTextCommandDisplayHelp("string");
                    AddTextComponentSubstringPlayerName($"You can not carry more than {maxAmmo} snowballs!");
                    EndTextCommandDisplayHelp(0, false, true, 6000);
                }
            }
        }
        #endregion
    }
}
