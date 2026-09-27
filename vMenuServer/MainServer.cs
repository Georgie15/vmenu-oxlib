using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using Newtonsoft.Json;

using vMenuShared;

using static CitizenFX.Core.Native.API;
using static vMenuServer.DebugLog;
using static vMenuShared.ConfigManager;

namespace vMenuServer
{

    public static class DebugLog
    {
        public enum LogLevel
        {
            error = 1,
            success = 2,
            info = 4,
            warning = 3,
            none = 0
        }

        /// <summary>
        /// Global log data function, only logs when debugging is enabled.
        /// </summary>
        /// <param name="data"></param>
        public static void Log(dynamic data, LogLevel level = LogLevel.none)
        {
            if (MainServer.DebugMode || level == LogLevel.error || level == LogLevel.warning)
            {
                var prefix = "[vMenu] ";
                if (level == LogLevel.error)
                {
                    prefix = "^1[vMenu] [ERROR]^7 ";
                }
                else if (level == LogLevel.info)
                {
                    prefix = "^5[vMenu] [INFO]^7 ";
                }
                else if (level == LogLevel.success)
                {
                    prefix = "^2[vMenu] [SUCCESS]^7 ";
                }
                else if (level == LogLevel.warning)
                {
                    prefix = "^3[vMenu] [WARNING]^7 ";
                }
                Debug.WriteLine($"{prefix}[DEBUG LOG] {data.ToString()}");
            }
        }
    }

    public class MainServer : BaseScript
    {
        // -------- helper for rate-limits (avoid ValueTuple) --------
        private sealed class Counter
        {
            public int Hits;
            public long WindowStart;   // <-- long (was int)
        }

        #region vars
        // Debug shows more information when doing certain things. Leave it off to improve performance!
        public static bool DebugMode = GetResourceMetadata(GetCurrentResourceName(), "server_debug_mode", 0) == "true";

        public static string Version { get { return GetResourceMetadata(GetCurrentResourceName(), "version", 0); } }

        // Time
        private int CurrentHours
        {
            get { return MathUtil.Clamp(GetSettingsInt(Setting.vmenu_current_hour), 0, 23); }
            set { SetConvarReplicated(Setting.vmenu_current_hour.ToString(), MathUtil.Clamp(value, 0, 23).ToString()); }
        }
        private int CurrentMinutes
        {
            get { return MathUtil.Clamp(GetSettingsInt(Setting.vmenu_current_minute), 0, 59); }
            set { SetConvarReplicated(Setting.vmenu_current_minute.ToString(), MathUtil.Clamp(value, 0, 59).ToString()); }
        }
        private int MinuteClockSpeed
        {
            get
            {
                var value = GetSettingsInt(Setting.vmenu_ingame_minute_duration);
                if (value < 100)
                {
                    value = 2000;
                }

                return value;
            }
        }
        private bool FreezeTime
        {
            get { return GetSettingsBool(Setting.vmenu_freeze_time); }
            set { SetConvarReplicated(Setting.vmenu_freeze_time.ToString(), value.ToString().ToLower()); }
        }
        private bool IsServerTimeSynced { get { return GetSettingsBool(Setting.vmenu_sync_to_machine_time); } }


        // Weather
        private string CurrentWeather
        {
            get
            {
                var value = GetSettingsString(Setting.vmenu_current_weather, "CLEAR");
                if (!WeatherTypes.Contains(value.ToUpper()))
                {
                    return "CLEAR";
                }
                return value;
            }
            set
            {
                if (string.IsNullOrEmpty(value) || !WeatherTypes.Contains(value.ToUpper()))
                {
                    SetConvarReplicated(Setting.vmenu_current_weather.ToString(), "CLEAR");
                }
                SetConvarReplicated(Setting.vmenu_current_weather.ToString(), value.ToUpper());
            }
        }
        private bool DynamicWeatherEnabled
        {
            get { return GetSettingsBool(Setting.vmenu_enable_dynamic_weather); }
            set { SetConvarReplicated(Setting.vmenu_enable_dynamic_weather.ToString(), value.ToString().ToLower()); }
        }
        private bool ManualSnowEnabled
        {
            get { return GetSettingsBool(Setting.vmenu_enable_snow); }
            set { SetConvarReplicated(Setting.vmenu_enable_snow.ToString(), value.ToString().ToLower()); }
        }
        private bool BlackoutEnabled
        {
            get { return GetSettingsBool(Setting.vmenu_blackout_enabled); }
            set { SetConvarReplicated(Setting.vmenu_blackout_enabled.ToString(), value.ToString().ToLower()); }
        }
        private bool VehicleBlackoutEnabled
        {
            get { return GetSettingsBool(Setting.vmenu_vehicle_blackout_enabled); }
            set { SetConvarReplicated(Setting.vmenu_vehicle_blackout_enabled.ToString(), value.ToString().ToLower()); }
        }
        private int DynamicWeatherMinutes
        {
            get { return Math.Max(GetSettingsInt(Setting.vmenu_dynamic_weather_timer), 1); }
        }
        private long lastWeatherChange = 0;

        private readonly List<string> CloudTypes = new List<string>
        {
            "Cloudy 01",
            "RAIN",
            "horizonband1",
            "horizonband2",
            "Puffs",
            "Wispy",
            "Horizon",
            "Stormy 01",
            "Clear 01",
            "Snowy 01",
            "Contrails",
            "altostratus",
            "Nimbus",
            "Cirrus",
            "cirrocumulus",
            "stratoscumulus",
            "horizonband3",
            "Stripey",
            "horsey",
            "shower",
        };
        private readonly List<string> WeatherTypes = new List<string>
        {
            "EXTRASUNNY",
            "CLEAR",
            "NEUTRAL",
            "SMOG",
            "FOGGY",
            "CLOUDS",
            "OVERCAST",
            "CLEARING",
            "RAIN",
            "THUNDER",
            "BLIZZARD",
            "SNOW",
            "SNOWLIGHT",
            "XMAS",
            "HALLOWEEN"
        };

        // ---- Anti-spam for vMenu:RequestPlayerList ----
        private const int PLAYERLIST_REQ_WINDOW_MS = 2000;   // 2s window
        private const int PLAYERLIST_REQ_MAX_BURST = 3;      // allow up to 3 requests per window
        private readonly Dictionary<string, Counter> _playerListReqs = new Dictionary<string, Counter>();

        // ---- Anti-spam for vMenu:SendMessageToPlayer ----
        private const int PM_REQ_WINDOW_MS = 2000;   // 2s window
        private const int PM_REQ_MAX_BURST = 3;      // max 3 PMs per window
        private const int PM_MAX_LEN = 300;          // clamp PM length
        private readonly Dictionary<string, Counter> _pmReqs = new Dictionary<string, Counter>();

        // ---- Anti-spam for vMenu:ClearArea ----
        private const int CLEAR_REQ_WINDOW_MS = 2000;
        private const int CLEAR_REQ_MAX_BURST = 2;
        private readonly Dictionary<string, Counter> _clearReqs = new Dictionary<string, Counter>();

        // ---- Anti-spam for vMenu:SaveTeleportLocation ----
        private const int SAVE_TP_REQ_WINDOW_MS = 4000;
        private const int SAVE_TP_REQ_MAX_BURST = 2;
        private readonly Dictionary<string, Counter> _saveTpReqs = new Dictionary<string, Counter>();
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor.
        /// </summary>
        public MainServer()
        {
            // name check
            if (GetCurrentResourceName() != "vMenu")
            {
                var InvalidNameException = new Exception("\r\n\r\n^1[vMenu] INSTALLATION ERROR!\r\nThe name of the resource is not valid. " +
                    "Please change the folder name from '^3" + GetCurrentResourceName() + "^1' to '^2vMenu^1' (case sensitive) instead!\r\n\r\n\r\n^7");
                try
                {
                    throw InvalidNameException;
                }
                catch (Exception e)
                {
                    Debug.Write(e.Message);
                }
            }
            else
            {
                // Add event handlers.
                EventHandlers.Add("vMenu:GetPlayerIdentifiers", new Action<int, NetworkCallbackDelegate>((TargetPlayer, CallbackFunction) =>
                {
                    var data = new List<string>();
                    Players[TargetPlayer].Identifiers.ToList().ForEach(e =>
                    {
                        if (!e.Contains("ip:"))
                        {
                            data.Add(e);
                        }
                    });
                    CallbackFunction(JsonConvert.SerializeObject(data));
                }));
                // check addons file for errors
                var addons = LoadResourceFile(GetCurrentResourceName(), "config/addons.json") ?? "{}";
                try
                {
                    JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(addons);
                }
                catch (JsonReaderException ex)
                {
                    Debug.WriteLine($"\n\n^1[vMenu] [ERROR] ^7Your addons.json file contains a problem! Error details: {ex.Message}\n\n");
                }

                // check if permissions are setup (correctly)
                if (!GetSettingsBool(Setting.vmenu_use_permissions))
                {
                    Debug.WriteLine("^3[vMenu] [WARNING] vMenu is set up to ignore permissions!\nIf you did this on purpose then you can ignore this warning.\nIf you did not set this on purpose, then you must have made a mistake while setting up vMenu.\nPlease read the vMenu documentation (^5https://docs.vespura.com/vmenu^3).\nMost likely you are not executing the permissions.cfg (correctly).^7");
                }

                Tick += PlayersFirstTick;

                // Start the loops
                if (GetSettingsBool(Setting.vmenu_enable_weather_sync))
                {
                    Tick += WeatherLoop;
                }

                if (GetSettingsBool(Setting.vmenu_enable_time_sync))
                {
                    Tick += TimeLoop;
                }

                GlobalState.Set("vmenu_onesync", GetConvar("onesync", "off") == "on", true);
            }
        }
        #endregion

        [EventHandler("vMenu:RequestPermissions")]
        internal void RequestPermissions([FromSource] Player sourcePlayer)
        {
            if (sourcePlayer == null)
            {
                return;
            }

            if (DebugMode)
            {
                Debug.WriteLine($"[vMenu] Permission bootstrap requested by {sourcePlayer.Name} ({sourcePlayer.Handle}).");
            }

            PermissionsManager.SetPermissionsForPlayer(sourcePlayer);
        }

        #region command handler
        [Command("vmenuserver", Restricted = true)]
        internal void ServerCommandHandler(int source, List<object> args, string _)
        {
            if (args != null)
            {
                if (args.Count > 0)
                {
                    if (args[0].ToString().ToLower() == "debug")
                    {
                        DebugMode = !DebugMode;
                        if (source < 1)
                        {
                            Debug.WriteLine($"Debug mode is now set to: {DebugMode}.");
                        }
                        else
                        {
                            Players[source].TriggerEvent("chatMessage", $"vMenu Debug mode is now set to: {DebugMode}.");
                        }
                        return;
                    }
                    else if (args[0].ToString().ToLower() == "unban" && (source < 1))
                    {
                        if (args.Count() > 1 && !string.IsNullOrEmpty(args[1].ToString()))
                        {
                            var uuid = args[1].ToString().Trim();
                            var bans = BanManager.GetBanList();
                            var banRecord = bans.Find(b => { return b.uuid.ToString() == uuid; });
                            if (banRecord != null)
                            {
                                BanManager.RemoveBan(banRecord);
                                Debug.WriteLine("Player has been successfully unbanned.");
                            }
                            else
                            {
                                Debug.WriteLine($"Could not find a banned player with the provided uuid '{uuid}'.");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("You did not specify a player to unban, you must enter the FULL playername. Usage: vmenuserver unban \"playername\"");
                        }
                        return;
                    }
                    else if (args[0].ToString().ToLower() == "weather")
                    {
                        if (args.Count < 2 || string.IsNullOrEmpty(args[1].ToString()))
                        {
                            Debug.WriteLine("[vMenu] Invalid command syntax. Use 'vmenuserver weather <weatherType>' instead.");
                        }
                        else
                        {
                            var wtype = args[1].ToString().ToUpper();
                            if (WeatherTypes.Contains(wtype))
                            {
                                TriggerEvent("vMenu:UpdateServerWeather", wtype, DynamicWeatherEnabled, ManualSnowEnabled);
                                Debug.WriteLine($"[vMenu] Weather is now set to: {wtype}");
                            }
                            else if (wtype.ToLower() == "dynamic")
                            {
                                if (args.Count == 3 && !string.IsNullOrEmpty(args[2].ToString()))
                                {
                                    if ((args[2].ToString().ToLower() ?? $"{DynamicWeatherEnabled}") == "true")
                                    {
                                        TriggerEvent("vMenu:UpdateServerWeather", CurrentWeather, true, ManualSnowEnabled);
                                        Debug.WriteLine("[vMenu] Dynamic weather is now turned on.");
                                    }
                                    else if ((args[2].ToString().ToLower() ?? $"{DynamicWeatherEnabled}") == "false")
                                    {
                                        TriggerEvent("vMenu:UpdateServerWeather", CurrentWeather, false, ManualSnowEnabled);
                                        Debug.WriteLine("[vMenu] Dynamic weather is now turned off.");
                                    }
                                    else
                                    {
                                        Debug.WriteLine("[vMenu] Invalid command usage. Correct syntax: vmenuserver weather dynamic <true|false>");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("[vMenu] Invalid command usage. Correct syntax: vmenuserver weather dynamic <true|false>");
                                }

                            }
                            else
                            {
                                Debug.WriteLine("[vMenu] This weather type is not valid!");
                            }
                        }
                    }
                    else if (args[0].ToString().ToLower() == "time")
                    {
                        if (args.Count == 2)
                        {
                            if (args[1].ToString().ToLower() == "freeze")
                            {
                                TriggerEvent("vMenu:UpdateServerTime", CurrentHours, CurrentMinutes, !FreezeTime);
                                Debug.WriteLine($"Time is now {(FreezeTime ? "frozen" : "not frozen")}.");
                            }
                            else
                            {
                                Debug.WriteLine("Invalid syntax. Use: ^5vmenuserver time <freeze|<hour> <minute>>^7 instead.");
                            }
                        }
                        else if (args.Count > 2)
                        {
                            if (int.TryParse(args[1].ToString(), out var hour))
                            {
                                if (int.TryParse(args[2].ToString(), out var minute))
                                {
                                    if (hour >= 0 && hour < 24)
                                    {
                                        if (minute >= 0 && minute < 60)
                                        {
                                            TriggerEvent("vMenu:UpdateServerTime", hour, minute, FreezeTime);
                                            Debug.WriteLine($"Time is now {(hour < 10 ? ("0" + hour.ToString()) : hour.ToString())}:{(minute < 10 ? ("0" + minute.ToString()) : minute.ToString())}.");
                                        }
                                        else
                                        {
                                            Debug.WriteLine("Invalid minute provided. Value must be between 0 and 59.");
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("Invalid hour provided. Value must be between 0 and 23.");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("Invalid syntax. Use: ^5vmenuserver time <freeze|<hour> <minute>>^7 instead.");
                                }
                            }
                            else
                            {
                                Debug.WriteLine("Invalid syntax. Use: ^5vmenuserver time <freeze|<hour> <minute>>^7 instead.");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("Invalid syntax. Use: ^5vmenuserver time <freeze|<hour> <minute>>^7 instead.");
                        }
                    }
                    else if (args[0].ToString().ToLower() == "ban" && source < 1)  // only via server console (server id < 1)
                    {
                        if (args.Count > 3)
                        {
                            Player p = null;

                            var findByServerId = args[1].ToString().ToLower() == "id";
                            var identifier = args[2].ToString().ToLower();

                            if (findByServerId)
                            {
                                if (Players.Any(player => player.Handle == identifier))
                                {
                                    p = Players.Single(pl => pl.Handle == identifier);
                                }
                                else
                                {
                                    Debug.WriteLine("[vMenu] Could not find this player, make sure they are online.");
                                    return;
                                }
                            }
                            else
                            {
                                if (Players.Any(player => player.Name.ToLower() == identifier.ToLower()))
                                {
                                    p = Players.Single(pl => pl.Name.ToLower() == identifier.ToLower());
                                }
                                else
                                {
                                    Debug.WriteLine("[vMenu] Could not find this player, make sure they are online.");
                                    return;
                                }
                            }

                            var reason = "Banned by staff for:";
                            args.GetRange(3, args.Count - 3).ForEach(arg => reason += " " + arg);

                            if (p != null)
                            {
                                var ban = new BanManager.BanRecord(
                                    BanManager.GetSafePlayerName(p.Name),
                                    p.Identifiers.ToList(),
                                    new DateTime(3000, 1, 1),
                                    reason,
                                    "Server Console",
                                    new Guid()
                                );

                                BanManager.AddBan(ban);
                                BanManager.BanLog($"[vMenu] Player {p.Name}^7 has been banned by Server Console for [{reason}].");
                                TriggerEvent("vMenu:BanSuccessful", JsonConvert.SerializeObject(ban).ToString());
                                var timeRemaining = BanManager.GetRemainingTimeMessage(ban.bannedUntil.Subtract(DateTime.Now));
                                p.Drop($"You are banned from this server. Ban time remaining: {timeRemaining}. Banned by: {ban.bannedBy}. Ban reason: {ban.banReason}. Additional information: {vMenuShared.ConfigManager.GetSettingsString(vMenuShared.ConfigManager.Setting.vmenu_default_ban_message_information)}.");
                            }
                            else
                            {
                                Debug.WriteLine("[vMenu] Player not found, could not ban player.");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("[vMenu] Not enough arguments, syntax: ^5vmenuserver ban <id|name> <server id|username> <reason>^7.");
                        }
                    }
                    else if (args[0].ToString().ToLower() == "help")
                    {
                        Debug.WriteLine("Available commands:");
                        Debug.WriteLine("(server console only): vmenuserver ban <id|name> <server id|username> <reason> (player must be online!)");
                        Debug.WriteLine("(server console only): vmenuserver unban <uuid>");
                        Debug.WriteLine("vmenuserver weather <new weather type | dynamic <true | false>>");
                        Debug.WriteLine("vmenuserver time <freeze|<hour> <minute>>");
                        Debug.WriteLine("vmenuserver migrate (This copies all banned players in the bans.json file to the new ban system in vMenu v3.3.0, you only need to do this once)");
                    }
                    else
                    {
                        Debug.WriteLine($"vMenu is currently running version: {Version}. Try ^5vmenuserver help^7 for info.");
                    }
                }
                else
                {
                    Debug.WriteLine($"vMenu is currently running version: {Version}. Try ^5vmenuserver help^7 for info.");
                }
            }
            else
            {
                Debug.WriteLine($"vMenu is currently running version: {Version}. Try ^5vmenuserver help^7 for info.");
            }
        }
        #endregion

        #region kick players from personal vehicle
        [EventHandler("vMenu:GetOutOfCar")]
        internal void GetOutOfCar([FromSource] Player source, int vehicleNetId)
        {
            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.PVKickPassengers, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.PVAll, source))
            {
                BanManager.BanCheater(source);
                return;
            }

            Entity vehicle = Entity.FromNetworkId(vehicleNetId);
            if (vehicle is null)
            {
                return;
            }

            int vehicleHandle = vehicle.Handle;
            for (int i = -1; i < 15; i++)
            {
                int pedHandle = GetPedInVehicleSeat(vehicleHandle, i);
                if (pedHandle == 0 || !IsPedAPlayer(pedHandle))
                {
                    continue;
                }

                int playerHandle = NetworkGetEntityOwner(pedHandle);
                Player player = GetPlayerFromServerId(playerHandle);

                if (player is null || player == source)
                {
                    continue;
                }

                int warpOutFlag = 16;
                TaskLeaveVehicle(pedHandle, vehicleHandle, warpOutFlag);
                player.TriggerEvent("vMenu:Notify", "The owner of the vehicle has kicked you out.");
            }
        }
        #endregion

        #region clear area near pos
        [EventHandler("vMenu:ClearArea")]
        internal void ClearAreaNearPos([FromSource] Player source)
        {
            if (source == null) return;

            bool allowed =
                IsPlayerAceAllowed(source.Handle, "vMenu.MiscSettings.All") ||
                IsPlayerAceAllowed(source.Handle, "vMenu.Everything");
            if (!allowed) return;

            long now = GetGameTimer();   // <-- long
            Counter st;
            if (_clearReqs.TryGetValue(source.Handle, out st))
            {
                if (now - st.WindowStart > CLEAR_REQ_WINDOW_MS) { st.Hits = 0; st.WindowStart = now; }
                st.Hits++;
                _clearReqs[source.Handle] = st;
                if (st.Hits > CLEAR_REQ_MAX_BURST) return;
            }
            else
            {
                _clearReqs[source.Handle] = new Counter { Hits = 1, WindowStart = now };
            }

            Ped ped = source.Character;
            if (ped is null || !DoesEntityExist(ped.Handle))
            {
                return;
            }

            TriggerClientEvent("vMenu:ClearArea", ped.Position);
        }
        #endregion

        #region Manage weather and time changes.
        private async Task TimeLoop()
        {
            if (IsServerTimeSynced)
            {
                var currentTime = DateTime.Now;
                CurrentMinutes = currentTime.Minute;
                CurrentHours = currentTime.Hour;

                await Delay(60000);
            }
            else
            {
                if (!FreezeTime)
                {
                    if ((CurrentMinutes + 1) > 59)
                    {
                        CurrentMinutes = 0;
                        if ((CurrentHours + 1) > 23)
                        {
                            CurrentHours = 0;
                        }
                        else
                        {
                            CurrentHours++;
                        }
                    }
                    else
                    {
                        CurrentMinutes++;
                    }
                }
                await Delay(MinuteClockSpeed);
            }
        }

        private async Task WeatherLoop()
        {
            if (DynamicWeatherEnabled)
            {
                await Delay(DynamicWeatherMinutes * 60000);

                if (GetSettingsBool(Setting.vmenu_enable_weather_sync))
                {
                    if (CurrentWeather == "XMAS" || CurrentWeather == "HALLOWEEN" || CurrentWeather == "NEUTRAL")
                    {
                        DynamicWeatherEnabled = false;
                        return;
                    }

                    if (GetGameTimer() - lastWeatherChange > (DynamicWeatherMinutes * 60000))
                    {
                        RefreshWeather();

                        if (DebugMode)
                        {
                            Log($"Changing weather, new weather: {CurrentWeather}");
                        }
                    }
                }
            }
            else
            {
                await Delay(5000);
            }
        }

        private void RefreshWeather()
        {
            var random = new Random().Next(20);
            if (CurrentWeather == "RAIN" || CurrentWeather == "THUNDER")
            {
                CurrentWeather = "CLEARING";
            }
            else if (CurrentWeather == "CLEARING")
            {
                CurrentWeather = "CLOUDS";
            }
            else
            {
                switch (random)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                        CurrentWeather = CurrentWeather == "EXTRASUNNY" ? "CLEAR" : "EXTRASUNNY";
                        break;
                    case 6:
                    case 7:
                    case 8:
                        CurrentWeather = CurrentWeather == "SMOG" ? "FOGGY" : "SMOG";
                        break;
                    case 9:
                    case 10:
                    case 11:
                    case 12:
                    case 13:
                    case 14:
                        CurrentWeather = CurrentWeather == "CLOUDS" ? "OVERCAST" : "CLOUDS";
                        break;
                    case 15:
                        CurrentWeather = CurrentWeather == "OVERCAST" ? "THUNDER" : "OVERCAST";
                        break;
                    case 16:
                        CurrentWeather = CurrentWeather == "CLOUDS" ? "EXTRASUNNY" : "RAIN";
                        break;
                    default:
                        CurrentWeather = CurrentWeather == "FOGGY" ? "SMOG" : "FOGGY";
                        break;
                }
            }
        }
        #endregion

        #region Sync weather & time with clients
        [EventHandler("vMenu:UpdateServerWeather")]
        internal void UpdateWeather([FromSource] Player source, string newWeather, bool dynamicWeatherNew, bool enableSnow)
        {
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.Menu") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.All"))
            {
                BanManager.BanCheater(source);
                return;
            }

            if (newWeather == "XMAS" || newWeather == "SNOWLIGHT" || newWeather == "SNOW" || newWeather == "BLIZZARD")
            {
                enableSnow = true;
            }

            CurrentWeather = newWeather;
            DynamicWeatherEnabled = dynamicWeatherNew;
            ManualSnowEnabled = enableSnow;

            lastWeatherChange = GetGameTimer();
        }

        [EventHandler("vMenu:UpdateServerBlackout")]
        internal void UpdateBlackout([FromSource] Player source, bool value)
        {
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.Blackout") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.All"))
            {
                BanManager.BanCheater(source);
                return;
            }

            BlackoutEnabled = value;
        }

        [EventHandler("vMenu:UpdateServerVehicleBlackout")]
        internal void UpdateVehicleBlackout([FromSource] Player source, bool value)
        {
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.VehicleBlackout") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.All"))
            {
                BanManager.BanCheater(source);
                return;
            }

            VehicleBlackoutEnabled = value;
        }

        [EventHandler("vMenu:UpdateServerWeatherCloudsType")]
        internal void UpdateWeatherCloudsType([FromSource] Player source, bool removeClouds)
        {
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.RemoveClouds") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.RandomizeClouds"))
            {
                BanManager.BanCheater(source);
                return;
            }

            if (removeClouds)
            {
                TriggerClientEvent("vMenu:SetClouds", 0f, "removed");
            }
            else
            {
                var opacity = float.Parse(new Random().NextDouble().ToString());
                var type = CloudTypes[new Random().Next(0, CloudTypes.Count)];
                TriggerClientEvent("vMenu:SetClouds", opacity, type);
            }
        }

        [EventHandler("vMenu:UpdateServerTime")]
        internal void UpdateTime([FromSource] Player source, int newHours, int newMinutes, bool freezeTimeNew)
        {
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.TimeOptions.Menu") && !IsPlayerAceAllowed(source.Handle, "vMenu.TimeOptions.All"))
            {
                BanManager.BanCheater(source);
                return;
            }

            CurrentHours = newHours;
            CurrentMinutes = newMinutes;
            FreezeTime = freezeTimeNew;
        }
        #endregion

        #region Online Players Menu Actions
        [EventHandler("vMenu:KickPlayer")]
        internal void KickPlayer([FromSource] Player source, int target, string kickReason = "You have been kicked from the server.")
        {
            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.OPKick, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.OPAll, source))
            {
                BanManager.BanCheater(source);
                return;
            }

            Player targetPlayer = GetPlayerFromServerId(target);
            if (targetPlayer is null)
            {
                source.TriggerEvent("vMenu:Notify", "Failed to kick target, because the target could not be found. Did they already leave?");
                return;
            }

            if (PermissionsManager.IsAllowed(PermissionsManager.Permission.DontKickMe, targetPlayer))
            {
                source.TriggerEvent("vMenu:Notify", "Sorry, this player can ~r~not ~w~be kicked.");
                return;
            }

            KickLog($"Player: {source.Name} has kicked: {targetPlayer.Name} for: {kickReason}.");
            source.TriggerEvent("vMenu:Notify", $"The target player (~y~{targetPlayer.Name}~s~) has been kicked.");

            targetPlayer.Drop(kickReason);
        }

        [EventHandler("vMenu:KillPlayer")]
        internal void KillPlayer([FromSource] Player source, int target)
        {
            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.OPKill, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.OPAll, source))
            {
                BanManager.BanCheater(source);
                return;
            }

            Player targetPlayer = GetPlayerFromServerId(target);
            if (targetPlayer is null)
            {
                return;
            }

            targetPlayer.TriggerEvent("vMenu:KillMe", source.Name);
        }

        [EventHandler("vMenu:SummonPlayer")]
        internal async void SummonPlayer([FromSource] Player source, int target, int numberOfSeats)
        {
            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.OPSummon, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.OPAll, source))
            {
                BanManager.BanCheater(source);
                return;
            }

            Player targetPlayer = GetPlayerFromServerId(target);
            if (targetPlayer is null)
            {
                return;
            }

            Ped targetPed = targetPlayer.Character;
            if (targetPed is null || !DoesEntityExist(targetPed.Handle))
            {
                return;
            }

            Ped sourcePed = source.Character;
            if (sourcePed is null || !DoesEntityExist(sourcePed.Handle))
            {
                return;
            }

            int sourcePedVehicle = GetVehiclePedIsIn(sourcePed.Handle, false);
            if (sourcePedVehicle == 0)
            {
                targetPed.Position = sourcePed.Position;
                return;
            }

            bool seatFound = false;
            numberOfSeats -= 1; // seat index starts at -1

            for (int i = -1; i < numberOfSeats; i++)
            {
                if (GetPedInVehicleSeat(sourcePedVehicle, i) != 0)
                {
                    continue;
                }

                Vector3 priorPosition = targetPed.Position;
                Vector3 newPosition = sourcePed.Position + new Vector3(0f, 0f, 5f);
                seatFound = true;
                targetPed.Position = newPosition;

                long timeout = GetGameTimer() + 1500;
                while (timeout > GetGameTimer() && priorPosition.DistanceToSquared(targetPed.Position) < newPosition.DistanceToSquared(targetPed.Position))
                {
                    await Delay(100);
                }

                if (timeout < GetGameTimer())
                {
                    source.TriggerEvent("vMenu:Notify", "Failed to teleport player.");
                    break;
                }

                SetPedIntoVehicle(targetPed.Handle, sourcePedVehicle, i);
                break;
            }

            if (!seatFound)
            {
                source.TriggerEvent("vMenu:Notify", "No free seats in your vehicle for summoned player.");
            }
        }

        [EventHandler("vMenu:SendMessageToPlayer")]
        internal void SendPrivateMessage([FromSource] Player source, int target, string message)
        {
            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.OPSendMessage, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.OPAll, source))
            {
                BanManager.BanCheater(source);
                return;
            }

            long now = GetGameTimer();   // <-- long
            Counter st;
            if (_pmReqs.TryGetValue(source.Handle, out st))
            {
                if (now - st.WindowStart > PM_REQ_WINDOW_MS) { st.Hits = 0; st.WindowStart = now; }
                st.Hits++;
                _pmReqs[source.Handle] = st;
                if (st.Hits > PM_REQ_MAX_BURST) return;
            }
            else
            {
                _pmReqs[source.Handle] = new Counter { Hits = 1, WindowStart = now };
            }

            message = message ?? string.Empty;
            if (message.Length > PM_MAX_LEN) message = message.Substring(0, PM_MAX_LEN);

            bool sourcePmsDisabled = source.State.Get("vmenu_pms_disabled") ?? false;
            if (sourcePmsDisabled)
            {
                source.TriggerEvent("vMenu:Notify", "You can't send a private message if you have private messages disabled yourself. Enable them in the Misc Settings menu and try again.");
                return;
            }

            Player targetPlayer = GetPlayerFromServerId(target);
            if (targetPlayer is null)
            {
                source.TriggerEvent("vMenu:Notify", "Failed to send message because the target could not be found. Did they disconnect?");
                return;
            }

            bool targetPmsDisabled = targetPlayer.State.Get("vmenu_pms_disabled") ?? false;
            if (targetPmsDisabled)
            {
                source.TriggerEvent("vMenu:Notify", $"Sorry, your private message to ~y~{source.Name}~s~ could not be delivered because they have private messages disabled.");
                return;
            }

            targetPlayer.TriggerEvent("vMenu:PrivateMessage", source.Handle, message);

            foreach (string playerHandle in joinedPlayers)
            {
                if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.OPSeePrivateMessages, playerHandle)
                    && !PermissionsManager.IsAllowed(PermissionsManager.Permission.OPAll, playerHandle))
                {
                    continue;
                }

                Player player = GetPlayerFromServerId(playerHandle);
                player?.TriggerEvent("vMenu:Notify", $"[vMenu Staff Log] ~y~{source.Name}~s~ sent a PM to ~y~{targetPlayer.Name}~s~: {message}");
            }
        }
        #endregion

        #region logging and update checks notifications
        private static void KickLog(string kickLogMesage)
        {
            if (GetSettingsBool(Setting.vmenu_log_kick_actions))
            {
                var file = LoadResourceFile(GetCurrentResourceName(), "vmenu.log") ?? "";
                var date = DateTime.Now;
                var formattedDate = (date.Day < 10 ? "0" : "") + date.Day + "-" +
                    (date.Month < 10 ? "0" : "") + date.Month + "-" +
                    (date.Year < 10 ? "0" : "") + date.Year + " " +
                    (date.Hour < 10 ? "0" : "") + date.Hour + ":" +
                    (date.Minute < 10 ? "0" : "") + date.Minute + ":" +
                    (date.Second < 10 ? "0" : "") + date.Second;
                var outputFile = file + $"[\t{formattedDate}\t] [KICK ACTION] {kickLogMesage}\n";
                SaveResourceFile(GetCurrentResourceName(), "vmenu.log", outputFile, -1);
                Debug.WriteLine("^3[vMenu] [KICK]^7 " + kickLogMesage + "\n");
            }
        }
        #endregion

        #region Add teleport location (hardened)
        [EventHandler("vMenu:SaveTeleportLocation")]
        internal void AddTeleportLocation([FromSource] Player source, string locationJson)
        {
            if (source == null) return;

            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.MSTeleportSaveLocation, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.MSAll, source))
            {
                BanManager.BanCheater(source);
                return;
            }

            long now = GetGameTimer();   // <-- long
            Counter st;
            if (_saveTpReqs.TryGetValue(source.Handle, out st))
            {
                if (now - st.WindowStart > SAVE_TP_REQ_WINDOW_MS) { st.Hits = 0; st.WindowStart = now; }
                st.Hits++;
                _saveTpReqs[source.Handle] = st;
                if (st.Hits > SAVE_TP_REQ_MAX_BURST) return;
            }
            else
            {
                _saveTpReqs[source.Handle] = new Counter { Hits = 1, WindowStart = now };
            }

            TeleportLocation location;
            try
            {
                location = JsonConvert.DeserializeObject<TeleportLocation>(locationJson);
            }
            catch
            {
                Log("Teleport location could not be deserialized, location was not saved.", LogLevel.error);
                return;
            }

            if (string.IsNullOrWhiteSpace(location.name))
            {
                Log("Teleport location could not be deserialized, location was not saved.", LogLevel.error);
                return;
            }

            if (GetTeleportLocationsData().Any(loc => loc.name == location.name))
            {
                Log("A teleport location with this name already exists, location was not saved.", LogLevel.error);
                return;
            }
            var locs = GetLocations();
            locs.teleports.Add(location);
            if (!SaveResourceFile(GetCurrentResourceName(), "config/locations.json", JsonConvert.SerializeObject(locs, Formatting.Indented), -1))
            {
                Log("Could not save locations.json file, reason unknown.", LogLevel.error);
            }
            ConfigManager.InvalidateTeleportLocationsCache();
            TriggerClientEvent("vMenu:UpdateTeleportLocations", JsonConvert.SerializeObject(locs.teleports));
        }
        #endregion

        #region Infinity bits
        [EventHandler("vMenu:RequestPlayerList")]
        internal void RequestPlayerListFromPlayer([FromSource] Player player)
        {
            if (player == null) return;

            if (!(IsPlayerAceAllowed(player.Handle, "vMenu.OnlinePlayers.Menu")
               || IsPlayerAceAllowed(player.Handle, "vMenu.OnlinePlayers.All")
               || IsPlayerAceAllowed(player.Handle, "vMenu.Everything")))
            {
                return;
            }

            long now = GetGameTimer();   // <-- long
            Counter st;
            if (_playerListReqs.TryGetValue(player.Handle, out st))
            {
                if (now - st.WindowStart > PLAYERLIST_REQ_WINDOW_MS) { st.Hits = 0; st.WindowStart = now; }
                st.Hits++;
                _playerListReqs[player.Handle] = st;

                if (st.Hits > PLAYERLIST_REQ_MAX_BURST)
                {
                    if (DebugMode) Debug.WriteLine($"^3[vMenu] [WARNING]^7 Dropped burst RequestPlayerList from {player.Name}.");
                    return;
                }
            }
            else
            {
                _playerListReqs[player.Handle] = new Counter { Hits = 1, WindowStart = now };
            }

            player.TriggerEvent("vMenu:ReceivePlayerList", Players.Select(p => new
            {
                n = p.Name,
                s = int.Parse(p.Handle),
            }));
        }

        [EventHandler("vMenu:GetPlayerCoords")]
        internal void GetPlayerCoords([FromSource] Player source, int playerId, NetworkCallbackDelegate callback)
        {
            var coords = Vector3.Zero;
            if (PermissionsManager.IsAllowed(PermissionsManager.Permission.OPTeleport, source)
                || PermissionsManager.IsAllowed(PermissionsManager.Permission.OPAll, source))
            {
                Player targetPlayer = GetPlayerFromServerId(playerId);
                if (targetPlayer is not null)
                {
                    Ped targetPed = targetPlayer.Character;
                    if (targetPed is not null && DoesEntityExist(targetPed.Handle))
                    {
                        coords = targetPed.Position;
                    }
                }
            }

            _ = callback(coords);
        }
        #endregion

        #region Player join/quit
        private readonly HashSet<string> joinedPlayers = new HashSet<string>();

        private IEnumerable<Player> GetJoinQuitNotifPlayers()
        {
            List<Player> players = new();

            foreach (string playerHandle in joinedPlayers)
            {
                // Eligibility is computed once during each player's permission
                // bootstrap; re-running two ace checks per online player on every
                // join/quit cost 50ms+ per disconnect on a big principal graph.
                if (!PermissionsManager.JoinQuitNotifEligibility.TryGetValue(playerHandle, out var eligible) || !eligible)
                {
                    continue;
                }

                Player player = GetPlayerFromServerId(playerHandle);
                if (player is not null)
                {
                    players.Add(player);
                }
            }

            return players;
        }

        private async Task PlayersFirstTick()
        {
            Tick -= PlayersFirstTick;

            // Allow clients time to restart client scripts.
            await Delay(3000);

            foreach (var player in Players)
            {
                joinedPlayers.Add(player.Handle);
                // Awaited sequentially: with a full server this used to run the whole
                // ace enumeration for every player in one tick, freezing the server
                // for seconds on a vMenu resource restart.
                await PermissionsManager.SetPermissionsForPlayerAsync(player);
            }
        }

        [EventHandler("playerJoining")]
        internal void OnPlayerJoining([FromSource] Player sourcePlayer)
        {
            PermissionsManager.InvalidatePermissionSession(sourcePlayer.Handle);
            joinedPlayers.Add(sourcePlayer.Handle);
            PermissionsManager.SetPermissionsForPlayer(sourcePlayer);

            foreach (Player player in GetJoinQuitNotifPlayers())
            {
                player.TriggerEvent("vMenu:PlayerJoinQuit", sourcePlayer.Name, null);
            }
        }

        [EventHandler("playerDropped")]
        internal void OnPlayerDropped([FromSource] Player sourcePlayer, string reason)
        {
            PermissionsManager.InvalidatePermissionSession(sourcePlayer.Handle);
            if (!joinedPlayers.Contains(sourcePlayer.Handle))
            {
                return;
            }

            if (_playerListReqs.ContainsKey(sourcePlayer.Handle)) _playerListReqs.Remove(sourcePlayer.Handle);
            if (_pmReqs.ContainsKey(sourcePlayer.Handle)) _pmReqs.Remove(sourcePlayer.Handle);
            if (_clearReqs.ContainsKey(sourcePlayer.Handle)) _clearReqs.Remove(sourcePlayer.Handle);
            if (_saveTpReqs.ContainsKey(sourcePlayer.Handle)) _saveTpReqs.Remove(sourcePlayer.Handle);

            joinedPlayers.Remove(sourcePlayer.Handle);
            PermissionsManager.JoinQuitNotifEligibility.Remove(sourcePlayer.Handle);

            foreach (Player player in GetJoinQuitNotifPlayers())
            {
                player.TriggerEvent("vMenu:PlayerJoinQuit", sourcePlayer.Name, reason);
            }
        }
        #endregion

        #region Utilities
        private Player GetPlayerFromServerId(string serverId)
        {
            if (!int.TryParse(serverId, out int serverIdInt))
            {
                return null;
            }

            return GetPlayerFromServerId(serverIdInt);
        }

        private Player GetPlayerFromServerId(int serverId)
        {
            string serverIdString = serverId.ToString();
            if (serverId <= 0 || !DoesPlayerExist(serverIdString))
            {
                return null;
            }

            return Players[serverId];
        }
        #endregion
    }
}
