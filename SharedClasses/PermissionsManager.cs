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

        private static readonly Dictionary<Permission, bool> allowedPerms = new();
        /// <summary>
        /// Private function that handles client side permission requests.
        /// </summary>
        /// <param name="permission"></param>
        /// <returns></returns>
        private static bool IsAllowedClient(Permission permission, bool checkAnyway)
        {
            if (ArePermissionsSetup || checkAnyway)
            {
                var staffPermissionAllowed = (
                    Permissions.ContainsKey(Permission.Staff) && Permissions[Permission.Staff]
                ) || (
                    Permissions.ContainsKey(Permission.Everything) && Permissions[Permission.Everything]
                );
                // Return false immediately if the staff only convar is set and the user is not a staff member.
                if (ConfigManager.GetSettingsBool(ConfigManager.Setting.vmenu_menu_staff_only) && !staffPermissionAllowed)
                {
                    return false;
                }

                if (allowedPerms.ContainsKey(permission) && allowedPerms[permission])
                {
                    return true;
                }
                else if (!allowedPerms.ContainsKey(permission))
                {
                    allowedPerms[permission] = false;
                }

                // Get a list of all permissions that are (parents) of the current permission, including the current permission.
                var permissionsToCheck = GetPermissionAndParentPermissions(permission);

                // Check if any of those permissions is allowed, if so, return true.
                if (permissionsToCheck.Any(p => Permissions.ContainsKey(p) && Permissions[p]))
                {
                    allowedPerms[permission] = true;
                    return true;
                }
            }
            return false;
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

        // One scheduler task owns the queue. At most one pending request per
        // handle, plus the active request; duplicates share completion, while a
        // request during active work retains one follow-up scan.
        private sealed class PermissionRequest
        {
            internal Player Player;
            internal string License;
            internal object Session;
            internal readonly TaskCompletionSource<bool> Completion = new();
        }
        private static readonly LinkedList<PermissionRequest> PermissionQueue = new();
        private static readonly Dictionary<string, LinkedListNode<PermissionRequest>> PendingPermissions = new();
        private static readonly Dictionary<string, object> PermissionSessions = new();
        private static bool permissionWorkerRunning;

        public static void InvalidatePermissionSession(string handle)
        {
            PermissionSessions.Remove(handle);
            JoinQuitNotifEligibility.Remove(handle);
            if (PendingPermissions.TryGetValue(handle, out var pending))
            {
                PendingPermissions.Remove(handle);
                PermissionQueue.Remove(pending);
                pending.Value.Completion.TrySetResult(false);
            }
        }

        private static bool IsCurrentPermissionSession(PermissionRequest request)
        {
            return PermissionSessions.TryGetValue(request.Player.Handle, out var session)
                && ReferenceEquals(session, request.Session)
                && DoesPlayerExist(request.Player.Handle)
                && request.Player.Identifiers["license"] == request.License;
        }

        private static async Task ProcessPermissionQueueAsync()
        {
            permissionWorkerRunning = true;
            try
            {
                while (PermissionQueue.First != null)
                {
                    var request = PermissionQueue.First.Value;
                    PermissionQueue.RemoveFirst();
                    PendingPermissions.Remove(request.Player.Handle);
                    try
                    {
                        await BuildPermissionsForPlayerAsync(request);
                    }
                    finally
                    {
                        request.Completion.TrySetResult(true);
                    }
                }
            }
            finally
            {
                permissionWorkerRunning = false;
            }
        }

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
        public static Task SetPermissionsForPlayerAsync(Player player)
        {
            if (player == null) return Task.FromResult(false);
            try
            {
                if (!DoesPlayerExist(player.Handle)) return Task.FromResult(false);
                var license = player.Identifiers["license"];
                if (string.IsNullOrEmpty(license)) return Task.FromResult(false);
                if (!PermissionSessions.TryGetValue(player.Handle, out var session))
                {
                    session = new object();
                    PermissionSessions[player.Handle] = session;
                }
                if (PendingPermissions.TryGetValue(player.Handle, out var pending))
                {
                    if (pending.Value.License == license && ReferenceEquals(pending.Value.Session, session))
                        return pending.Value.Completion.Task;
                    InvalidatePermissionSession(player.Handle);
                    session = new object();
                    PermissionSessions[player.Handle] = session;
                }
                var request = new PermissionRequest { Player = player, License = license, Session = session };
                PendingPermissions[player.Handle] = PermissionQueue.AddLast(request);
                if (!permissionWorkerRunning) _ = ProcessPermissionQueueAsync();
                return request.Completion.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[vMenu] Failed to queue permissions for player {player.Handle}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private static async Task BuildPermissionsForPlayerAsync(PermissionRequest request)
        {
            var player = request.Player;
            try
            {
                if (!IsCurrentPermissionSession(request)) return;

                var perms = new Dictionary<Permission, bool>();

                // If enabled in the permissions.cfg (disabled by default) then this will give me (only me) the option to trigger some debug commands and
                // try out menu options. This only works if I'm in-game on your server, and you have enabled server debugging mode, this way I will never
                // be able to do something without you actually allowing it.
                if (player.Identifiers.ToList().Any(id => id == "4510587c13e0b645eb8d24bc104601792277ab98") && IsPlayerAceAllowed(player.Handle, "vMenu.Dev") && ConfigManager.DebugMode)
                {
                    perms.Add(Permission.Everything, true);
                }

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
                                if (!IsCurrentPermissionSession(request))
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

                if (!IsCurrentPermissionSession(request)) return;

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
