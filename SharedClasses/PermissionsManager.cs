using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;

namespace vMenuShared
{
    public static class PermissionsManager
    {
        public enum Permission
        {
            // Global
            #region global
            Everything,
            DontKickMe,
            DontBanMe,
            NoClip,
            Staff,
            #endregion

            // Online Players
            #region online players
            OPMenu,
            OPAll,
            OPTeleport,
            OPWaypoint,
            OPSpectate,
            OPSendMessage,
            OPIdentifiers,
            OPSummon,
            OPKill,
            OPKick,
            OPPermBan,
            OPTempBan,
            OPUnban,
            OPViewBannedPlayers,
            OPSeePrivateMessages,
            #endregion

            // Player Options
            #region player options
            POMenu,
            POAll,
            POGod,
            POInvisible,
            POFastRun,
            POFastSwim,
            POSuperjump,
            PONoRagdoll,
            PONeverWanted,
            POSetWanted,
            POClearBlood,
            POSetBlood,
            POIgnored,
            POStayInVehicle,
            POMaxHealth,
            POMaxArmor,
            POCleanPlayer,
            PODryPlayer,
            POWetPlayer,
            POVehicleAutoPilotMenu,
            POFreeze,
            POScenarios,
            POUnlimitedStamina,
            #endregion

            // Vehicle Options
            #region vehicle options
            VOMenu,
            VOAll,
            VOGod,
            VOKeepClean,
            VORepair,
            VOWash,
            VOEngine,
            VODestroyEngine,
            VOBikeSeatbelt,
            VOSpeedLimiter,
            VOChangePlate,
            VOMod,
            VOColors,
            VOLiveries,
            VOComponents,
            VODoors,
            VOWindows,
            VOFreeze,
            VOInvisible,
            VOTorqueMultiplier,
            VOPowerMultiplier,
            VOFlip,
            VOAlarm,
            VOCycleSeats,
            VOEngineAlwaysOn,
            VONoSiren,
            VONoHelmet,
            VOLights,
            VOFixOrDestroyTires,
            VODelete,
            VOUnderglow,
            VOFlashHighbeamsOnHonk,
            VODisableTurbulence,
            VOAnchorBoat,
            VOInfiniteFuel,
            VOFlares,
            VOPlaneBombs,
            VOBypassExtraDamage,
            VOBulletproofTires,
            #endregion

            // Vehicle Spawner
            #region vehicle spawner
            VSMenu,
            VSAll,
            VSDisableReplacePrevious,
            VSSpawnByName,
            VSAddon,
            VSCompacts,
            VSSedans,
            VSSUVs,
            VSCoupes,
            VSMuscle,
            VSSportsClassic,
            VSSports,
            VSSuper,
            VSMotorcycles,
            VSOffRoad,
            VSIndustrial,
            VSUtility,
            VSVans,
            VSCycles,
            VSBoats,
            VSHelicopters,
            VSPlanes,
            VSService,
            VSEmergency,
            VSMilitary,
            VSCommercial,
            VSTrains,
            VSOpenWheel,
            #endregion

            // Saved Vehicles
            #region saved vehicles
            SVMenu,
            SVAll,
            SVSpawn,
            #endregion

            // Personal Vehicle
            #region personal vehicle
            PVMenu,
            PVAll,
            PVToggleEngine,
            PVToggleLights,
            PVToggleStance,
            PVKickPassengers,
            PVLockDoors,
            PVDoors,
            PVSoundHorn,
            PVToggleAlarm,
            PVAddBlip,
            PVExclusiveDriver,
            #endregion

            // Player Appearance
            #region player appearance
            PAMenu,
            PAAll,
            PACustomize,
            PASpawnSaved,
            PASpawnNew,
            PASpawnAnimals,
            PAAddonPeds,
            PAPedPack,
            #endregion

            // Time Options
            #region time options
            TOMenu,
            TOAll,
            TOFreezeTime,
            TOSetTime,
            #endregion

            // Weather Options
            #region weather options
            WOMenu,
            WOAll,
            WODynamic,
            WOBlackout,
            WOVehBlackout,
            WOSetWeather,
            WORemoveClouds,
            WORandomizeClouds,
            #endregion

            // Weapon Options
            #region weapon options
            WPMenu,
            WPAll,
            WPGetAll,
            WPRemoveAll,
            WPUnlimitedAmmo,
            WPNoReload,
            WPSpawn,
            WPSpawnByName,
            WPSetAllAmmo,
            #endregion

            //Weapons Permissions
            #region weapon specific permissions
            WPAPPistol,
            WPAdvancedRifle,
            WPAssaultRifle,
            WPAssaultRifleMk2,
            WPAssaultSMG,
            WPAssaultShotgun,
            WPBZGas,
            WPBall,
            WPBat,
            WPBattleAxe,
            WPBottle,
            WPBullpupRifle,
            WPBullpupRifleMk2,
            WPBullpupShotgun,
            WPCarbineRifle,
            WPCarbineRifleMk2,
            WPCombatMG,
            WPCombatMGMk2,
            WPCombatPDW,
            WPCombatPistol,
            WPCompactGrenadeLauncher,
            WPCompactRifle,
            WPCrowbar,
            WPDagger,
            WPDoubleAction,
            WPDoubleBarrelShotgun,
            WPFireExtinguisher,
            WPFirework,
            WPFlare,
            WPFlareGun,
            WPFlashlight,
            WPGolfClub,
            WPGrenade,
            WPGrenadeLauncher,
            WPGrenadeLauncherSmoke,
            WPGusenberg,
            WPHammer,
            WPHatchet,
            WPHeavyPistol,
            WPHeavyShotgun,
            WPHeavySniper,
            WPHeavySniperMk2,
            WPHomingLauncher,
            WPKnife,
            WPKnuckleDuster,
            WPMG,
            WPMachete,
            WPMachinePistol,
            WPMarksmanPistol,
            WPMarksmanRifle,
            WPMarksmanRifleMk2,
            WPMicroSMG,
            WPMiniSMG,
            WPMinigun,
            WPMolotov,
            WPMusket,
            WPNightVision,
            WPNightstick,
            WPParachute,
            WPPetrolCan,
            WPPipeBomb,
            WPPistol,
            WPPistol50,
            WPPistolMk2,
            WPPoolCue,
            WPProximityMine,
            WPPumpShotgun,
            WPPumpShotgunMk2,
            WPRPG,
            WPRailgun,
            WPRevolver,
            WPRevolverMk2,
            WPSMG,
            WPSMGMk2,
            WPSNSPistol,
            WPSNSPistolMk2,
            WPSawnOffShotgun,
            WPSmokeGrenade,
            WPSniperRifle,
            WPSnowball,
            WPSpecialCarbine,
            WPSpecialCarbineMk2,
            WPStickyBomb,
            WPStunGun,
            WPSweeperShotgun,
            WPSwitchBlade,
            WPUnarmed,
            WPVintagePistol,
            WPWrench,
            WPPlasmaPistol,
            WPPlasmaCarbine,
            WPPlasmaMinigun,
            WPStoneHatchet,
            // MPHEIST3 DLC (v 1868)
            WPCeramicPistol,
            WPNavyRevolver,
            WPHazardCan,
            // MPHEIST4 DLC (v 2189)
            WPPericoPistol,
            WPMilitaryRifle,
            WPCombatShotgun,
            // MPSECURITY DLC (v 2545)
            WPEMPLauncher,
            WPHeavyRifle,
            WPFertilizerCan,
            WPStunGunMP,
            // MPSUM2 DLC (v 2699)
            WPPrecisionRifle,
            WPTacticalRifle,
            // MPCHRISTMAS3 DLC (V 2802)
            WPPistolXM3,
            WPCandyCane,
            WPRailgunXM3,
            WPAcidPackage,
            // MP2023_01 DLC (V 2944)
            WPTecPistol,
            // MP2023_02 DLC (V 3095)
            WPBattleRifle,
            WPSnowLauncher,
            WPHackingDevice,
            // MP2024_01 DLC (V 3258)
            WPStunRod,
            #endregion

            // Weapon Loadouts Menu
            #region weapon loadouts
            WLMenu,
            WLAll,
            WLEquip,
            WLEquipOnRespawn,
            #endregion

            // Misc Settings
            #region misc settings
            MSAll,
            MSClearArea,
            MSTeleportToWp,
            MSTeleportToCoord,
            MSShowCoordinates,
            MSShowLocation,
            MSJoinQuitNotifs,
            MSDeathNotifs,
            MSNightVision,
            MSThermalVision,
            MSLocationBlips,
            MSPlayerBlips,
            MSOverheadNames,
            MSTeleportLocations,
            MSTeleportSaveLocation,
            MSConnectionMenu,
            MSRestoreAppearance,
            MSRestoreWeapons,
            MSDriftMode,
            MSEntitySpawner,
            MSDevTools,
            #endregion

            // Voice Chat
            #region voice chat
            VCMenu,
            VCAll,
            VCEnable,
            VCShowSpeaker,
            VCStaffChannel,
            #endregion
        };

        public static Dictionary<Permission, bool> Permissions { get; private set; } = new Dictionary<Permission, bool>();
        public static bool ArePermissionsSetup { get; set; } = false;


#if SERVER
        /// <summary>
        /// Public function to check if a permission is allowed.
        /// </summary>
        /// <param name="permission"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        public static bool IsAllowed(Permission permission, Player source) => IsAllowedServer(permission, source);

        /// <summary>
        /// Public function to check if a permission is allowed.
        /// </summary>
        /// <param name="permission"></param>
        /// <param name="playerHandle"></param>
        /// <returns></returns>
        public static bool IsAllowed(Permission permission, string playerHandle) => IsAllowedServer(permission, playerHandle);
#endif

#if CLIENT
        /// <summary>
        /// Public function to check if a permission is allowed.
        /// </summary>
        /// <param name="permission"></param>
        /// <param name="checkAnyway">if true, then the permissions will be checked even if they aren't setup yet.</param>
        /// <returns></returns>
        public static bool IsAllowed(Permission permission, bool checkAnyway = false) => IsAllowedClient(permission, checkAnyway);

        // The answer for every permission, built once in SetPermissions. IsAllowed runs dozens of
        // times per frame; it used to read the staff-only convar and re-check the parent
        // permissions on every call (denied answers were never cached).
        private static bool[] effectivePermissions = new bool[0];

        /// <summary>
        /// Private function that handles client side permission requests.
        /// </summary>
        /// <param name="permission"></param>
        /// <returns></returns>
        private static bool IsAllowedClient(Permission permission, bool checkAnyway)
        {
            if (ArePermissionsSetup || checkAnyway)
            {
                var index = (int)permission;
                return index >= 0 && index < effectivePermissions.Length && effectivePermissions[index];
            }
            return false;
        }

        /// <summary>
        /// A permission is allowed if it, its category's "All" permission or Everything is granted.
        /// With the staff only convar set, nothing is allowed unless Staff or Everything is granted.
        /// </summary>
        internal static bool[] BuildEffectivePermissions(Dictionary<Permission, bool> permissions, bool staffOnly)
        {
            bool Granted(Permission p) => permissions != null && permissions.TryGetValue(p, out var allowed) && allowed;

            var values = (Permission[])Enum.GetValues(typeof(Permission));
            var table = new bool[values.Max(p => (int)p) + 1];

            // Return all false if the staff only convar is set and the user is not a staff member.
            if (staffOnly && !Granted(Permission.Staff) && !Granted(Permission.Everything))
            {
                return table;
            }

            foreach (var permission in values)
            {
                // The permission itself or any of its parents.
                table[(int)permission] = GetPermissionAndParentPermissions(permission).Any(Granted);
            }
            return table;
        }
#endif
#if SERVER
        /// <summary>
        /// Checks if the player is allowed that specific permission.
        /// </summary>
        /// <param name="permission"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        private static bool IsAllowedServer(Permission permission, Player source)
        {
            if (source == null)
            {
                return false;
            }

            return IsAllowedServer(permission, source.Handle);
        }

        /// <summary>
        /// Checks if the player is allowed that specific permission.
        /// </summary>
        /// <param name="permission"></param>
        /// <param name="playerHandle"></param>
        /// <returns></returns>
        private static bool IsAllowedServer(Permission permission, string playerHandle)
        {
            if (!DoesPlayerExist(playerHandle))
            {
                return false;
            }

            return IsPlayerAceAllowed(playerHandle, GetAceName(permission));
        }
#endif

        private static readonly Dictionary<Permission, List<Permission>> parentPermissions = new();

        /// <summary>
        /// Gets the current permission and all parent permissions.
        /// </summary>
        /// <param name="permission"></param>
        /// <returns></returns>
        public static List<Permission> GetPermissionAndParentPermissions(Permission permission)
        {
            if (parentPermissions.ContainsKey(permission))
            {
                return parentPermissions[permission];
            }
            else
            {
                var list = new List<Permission>() { Permission.Everything, permission };
                var permStr = permission.ToString();

                // if the first 2 characters are both uppercase
                if (permStr.Substring(0, 2).ToUpper() == permStr.Substring(0, 2))
                {
                    if (permStr.Substring(2) is not ("All" or "Menu"))
                    {
                        list.AddRange(Enum.GetValues(typeof(Permission)).Cast<Permission>().Where(a => a.ToString() == permStr.Substring(0, 2) + "All"));
                    }
                }
                //else // it's one of the .Everything, .DontKickMe, DontBanMe, NoClip, Staff, etc perms that are not menu specific.
                //{
                //    // do nothing
                //}
                parentPermissions[permission] = list;
                return list;
            }
        }

#if SERVER
        /// <summary>
        /// Join/quit-notification eligibility per player handle, filled in during the
        /// permission bootstrap below and consumed by MainServer.GetJoinQuitNotifPlayers.
        /// Without this, every join/quit re-ran two ace checks per online player, which
        /// on a large ace/principal graph cost 50ms+ per disconnect.
        /// </summary>
        public static readonly Dictionary<string, bool> JoinQuitNotifEligibility = new();

        // Permissions are built for one player at a time. This is the bootstrap in the live server
        // build (packaged vMenuServer.net.dll e17419b8...). A per-handle queue that merged
        // duplicate requests was built in September and pulled again; see current-build.md.
        private static bool permissionsBootstrapBusy = false;

        // How many ace checks run per server tick during a bootstrap. The Permission
        // enum has ~300 entries and one IsPlayerAceAllowed call costs 0.3-0.5ms on a
        // server with a big principal graph — unchunked, that was a 90-145ms freeze
        // in a single tick on every player join.
        private const int AceChecksPerTick = 25;

        /// <summary>
        /// Sets the permissions for a specific player (checks server side, sends event to client side).
        /// Fire-and-forget wrapper kept for existing call sites.
        /// </summary>
        /// <param name="player"></param>
        public static void SetPermissionsForPlayer([FromSource] Player player)
        {
            _ = SetPermissionsForPlayerAsync(player);
        }

        /// <summary>
        /// Sets the permissions for a specific player (checks server side, sends event to client side).
        /// The ace checks are spread across ticks; the client receives the exact same events
        /// as before, just a few frames later.
        /// </summary>
        /// <param name="player"></param>
        public static async Task SetPermissionsForPlayerAsync(Player player)
        {
            if (player == null)
            {
                return;
            }

            // Wait until the previous player's bootstrap has finished.
            while (permissionsBootstrapBusy)
            {
                await BaseScript.Delay(0);
            }
            permissionsBootstrapBusy = true;

            try
            {
                // The player can leave (and the handle be reused) while the checks yield between
                // chunks; stop instead of sending one player's permissions to another.
                var originalLicense = player.Identifiers["license"];
                var perms = new Dictionary<Permission, bool>();

                if (!ConfigManager.GetSettingsBool(ConfigManager.Setting.vmenu_use_permissions))
                {
                    foreach (var p in Enum.GetValues(typeof(Permission)))
                    {
                        var permission = (Permission)p;
                        switch (permission)
                        {
                            // don't allow any of the following permissions if perms are ignored.
                            case Permission.Everything:
                            case Permission.OPAll:
                            case Permission.OPKick:
                            case Permission.OPKill:
                            case Permission.OPPermBan:
                            case Permission.OPTempBan:
                            case Permission.OPUnban:
                            case Permission.OPIdentifiers:
                            case Permission.OPViewBannedPlayers:
                                break;
                            // do allow the rest
                            default:
                                perms.Add(permission, true);
                                break;
                        }
                    }

                    // GetJoinQuitNotifPlayers always used real ace checks even in this
                    // mode, so mirror that exactly rather than reading the all-true dict.
                    JoinQuitNotifEligibility[player.Handle] =
                        IsAllowed(Permission.MSJoinQuitNotifs, player.Handle)
                        || IsAllowed(Permission.MSAll, player.Handle);
                }
                else
                {
                    // Loop through all permissions and check if they're allowed.
                    var checksThisChunk = 0;
                    foreach (var p in Enum.GetValues(typeof(Permission)))
                    {
                        var permission = (Permission)p;
                        if (!perms.ContainsKey(permission))
                        {
                            perms.Add(permission, IsAllowed(permission, player)); // triggers IsAllowedServer

                            checksThisChunk++;
                            if (checksThisChunk >= AceChecksPerTick)
                            {
                                checksThisChunk = 0;
                                await BaseScript.Delay(0);
                                if (!DoesPlayerExist(player.Handle) || player.Identifiers["license"] != originalLicense)
                                {
                                    return;
                                }
                            }
                        }
                    }

                    JoinQuitNotifEligibility[player.Handle] =
                        (perms.TryGetValue(Permission.MSJoinQuitNotifs, out var notifs) && notifs)
                        || (perms.TryGetValue(Permission.MSAll, out var msAll) && msAll);
                }

                // Send the permissions to the client.
                player.TriggerEvent("vMenu:SetPermissions", Newtonsoft.Json.JsonConvert.SerializeObject(perms));

                // Also tell the client to do the addons setup.
                player.TriggerEvent("vMenu:SetAddons");
                player.TriggerEvent("vMenu:UpdateTeleportLocations", ConfigManager.GetTeleportLocationsJson());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[vMenu] Failed to send permissions to player {player.Handle}: {ex.Message}");
            }
            finally
            {
                permissionsBootstrapBusy = false;
            }
        }
#endif
#if CLIENT
        /// <summary>
        /// Sets the permission (client side event handler).
        /// </summary>
        /// <param name="permissions"></param>
        public static void SetPermissions(string permissions)
        {
            Permissions = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<Permission, bool>>(permissions);
            effectivePermissions = BuildEffectivePermissions(Permissions, ConfigManager.GetSettingsBool(ConfigManager.Setting.vmenu_menu_staff_only));
            // if debug logging.
            if (GetResourceMetadata(GetCurrentResourceName(), "client_debug_mode", 0) == "true")
            {
                Debug.WriteLine("[vMenu] [Permissions] " + Newtonsoft.Json.JsonConvert.SerializeObject(Permissions, Newtonsoft.Json.Formatting.None));
            }
        }
#endif
#if SERVER
        /// <summary>
        /// Gets the full permission ace name for the specific <see cref="Permission"/> enum.
        /// </summary>
        /// <param name="permission"></param>
        /// <returns></returns>
        private static string GetAceName(Permission permission)
        {
            var name = permission.ToString();

            var prefix = "vMenu.";

            switch (name.Substring(0, 2))
            {
                case "OP":
                    prefix += "OnlinePlayers";
                    break;
                case "PO":
                    prefix += "PlayerOptions";
                    break;
                case "VO":
                    prefix += "VehicleOptions";
                    break;
                case "VS":
                    prefix += "VehicleSpawner";
                    break;
                case "SV":
                    prefix += "SavedVehicles";
                    break;
                case "PV":
                    prefix += "PersonalVehicle";
                    break;
                case "PA":
                    prefix += "PlayerAppearance";
                    break;
                case "TO":
                    prefix += "TimeOptions";
                    break;
                case "WO":
                    prefix += "WeatherOptions";
                    break;
                case "WP":
                    prefix += "WeaponOptions";
                    break;
                case "WL":
                    prefix += "WeaponLoadouts";
                    break;
                case "MS":
                    prefix += "MiscSettings";
                    break;
                case "VC":
                    prefix += "VoiceChat";
                    break;
                default:
                    return prefix + name;
            }

            return prefix + "." + name.Substring(2);
        }
#endif
    }
}
