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

        /// <summary>
        /// The client asks for its permissions at start (and retries while its menu isn't built).
        /// A bootstrap runs ~300 ace checks and they queue one player at a time, so a client's
        /// requests are rate-limited. Another resource on this server calling
        /// TriggerEvent("vMenu:RequestPermissions", id) (DiscordAcePerms on a role refresh) has no
        /// event source, so it never named a player; it is left alone, as before: not limited, not logged.
        /// </summary>
        [EventHandler("vMenu:RequestPermissions")]
        internal void RequestPermissions([FromSource] Player sourcePlayer, [FromSource] string source)
        {
            if (sourcePlayer == null)
            {
                return;
            }

            if (RequestSource.IsClient(source) && !RequestGuard.Admit(sourcePlayer, RequestGuard.RequestPermissions))
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
                    else if (args[0].ToString().ToLower() == "help")
                    {
                        Debug.WriteLine("Available commands:");
                        Debug.WriteLine("vmenuserver weather <new weather type | dynamic <true | false>>");
                        Debug.WriteLine("vmenuserver time <freeze|<hour> <minute>>");
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
        private readonly PersonalVehicleRegistry personalVehicles = new PersonalVehicleRegistry();

        /// <summary>
        /// The client reports the vehicle it just set as its personal vehicle (0 to clear it). It is
        /// only recorded once this server sees the player in its driver seat. The seat can take a
        /// moment to sync, so the check is retried for about two seconds.
        /// </summary>
        [EventHandler("vMenu:SetPersonalVehicle")]
        internal async void SetPersonalVehicle([FromSource] Player source, object vehicleNetIdArg)
        {
            var request = RequestGuard.SetPersonalVehicle;
            if (!RequestGuard.Admit(source, request))
            {
                return;
            }
            if (!RequestPayload.TryInt(vehicleNetIdArg, out var vehicleNetId) || vehicleNetId < 0)
            {
                RequestGuard.Malformed(source, request, "the vehicle net id is not a whole number of 0 or more");
                return;
            }

            try
            {
                var playerHandle = source.Handle;
                var token = personalVehicles.Begin(playerHandle);
                if (vehicleNetId == 0)
                {
                    return;
                }

                for (var attempt = 0; attempt < 8; attempt++)
                {
                    if (!DoesPlayerExist(playerHandle) || !personalVehicles.IsCurrent(playerHandle, token))
                    {
                        return;
                    }

                    var vehicle = NetworkGetEntityFromNetworkId(vehicleNetId);
                    var ped = GetPlayerPed(playerHandle);
                    if (vehicle != 0 && DoesEntityExist(vehicle) && ped != 0 && GetPedInVehicleSeat(vehicle, -1) == ped)
                    {
                        personalVehicles.Set(playerHandle, token, vehicleNetId, vehicle, (uint)GetEntityModel(vehicle));
                        return;
                    }

                    await Delay(250);
                }
            }
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }

        [EventHandler("vMenu:GetOutOfCar")]
        internal void GetOutOfCar([FromSource] Player source, object vehicleNetIdArg)
        {
            var request = RequestGuard.GetOutOfCar;
            if (!RequestGuard.Admit(source, request))
            {
                return;
            }

            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.PVKickPassengers, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.PVAll, source))
            {
                RequestGuard.Denied(source, request, "vMenu.PersonalVehicle.KickPassengers");
                return;
            }

            if (!RequestPayload.TryInt(vehicleNetIdArg, out var vehicleNetId))
            {
                RequestGuard.Malformed(source, request, "the vehicle net id is not a whole number");
                return;
            }

            try
            {
                Entity vehicle = Entity.FromNetworkId(vehicleNetId);
                if (vehicle is null)
                {
                    return;
                }

                int vehicleHandle = vehicle.Handle;
                if (!personalVehicles.Owns(source.Handle, vehicleNetId, vehicleHandle, (uint)GetEntityModel(vehicleHandle)))
                {
                    source.TriggerEvent("vMenu:Notify", "You can only remove passengers from your own personal vehicle. Drive it once to set it as your personal vehicle.");
                    return;
                }

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
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }
        #endregion

        #region clear area near pos
        [EventHandler("vMenu:ClearArea")]
        internal void ClearAreaNearPos([FromSource] Player source)
        {
            var request = RequestGuard.ClearArea;
            if (!RequestGuard.Admit(source, request)) return;

            // The client shows Clear Area to anyone with vMenu.MiscSettings.ClearArea; this used to
            // require MiscSettings.All, so moderators pressed it and nothing happened.
            bool allowed =
                IsPlayerAceAllowed(source.Handle, "vMenu.MiscSettings.ClearArea") ||
                IsPlayerAceAllowed(source.Handle, "vMenu.MiscSettings.All") ||
                IsPlayerAceAllowed(source.Handle, "vMenu.Everything");
            if (!allowed)
            {
                RequestGuard.Denied(source, request, "vMenu.MiscSettings.ClearArea");
                return;
            }

            try
            {
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
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
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
        // These also run with no source when the vmenuserver console command triggers them; that
        // path skips the guard and the permission checks, as before.
        [EventHandler("vMenu:UpdateServerWeather")]
        internal void UpdateWeather([FromSource] Player source, object newWeatherArg, object dynamicWeatherArg, object enableSnowArg)
        {
            var request = RequestGuard.UpdateServerWeather;
            if (source != null && !RequestGuard.Admit(source, request))
            {
                return;
            }
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.Menu") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.All"))
            {
                RequestGuard.Denied(source, request, "vMenu.WeatherOptions.Menu");
                return;
            }
            if (!RequestPayload.TryString(newWeatherArg, 32, out var newWeather)
                || !RequestPayload.TryBool(dynamicWeatherArg, out var dynamicWeatherNew)
                || !RequestPayload.TryBool(enableSnowArg, out var enableSnow))
            {
                RequestGuard.Malformed(source, request, "expected a weather name (up to 32 characters) and two true/false values");
                return;
            }

            try
            {
                if (newWeather == "XMAS" || newWeather == "SNOWLIGHT" || newWeather == "SNOW" || newWeather == "BLIZZARD")
                {
                    enableSnow = true;
                }

                CurrentWeather = newWeather;
                DynamicWeatherEnabled = dynamicWeatherNew;
                ManualSnowEnabled = enableSnow;

                lastWeatherChange = GetGameTimer();
            }
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }

        [EventHandler("vMenu:UpdateServerBlackout")]
        internal void UpdateBlackout([FromSource] Player source, object valueArg)
        {
            var request = RequestGuard.UpdateServerBlackout;
            if (source != null && !RequestGuard.Admit(source, request))
            {
                return;
            }
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.Blackout") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.All"))
            {
                RequestGuard.Denied(source, request, "vMenu.WeatherOptions.Blackout");
                return;
            }
            if (!RequestPayload.TryBool(valueArg, out var value))
            {
                RequestGuard.Malformed(source, request, "expected true or false");
                return;
            }

            try
            {
                BlackoutEnabled = value;
            }
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }

        [EventHandler("vMenu:UpdateServerVehicleBlackout")]
        internal void UpdateVehicleBlackout([FromSource] Player source, object valueArg)
        {
            var request = RequestGuard.UpdateServerVehicleBlackout;
            if (source != null && !RequestGuard.Admit(source, request))
            {
                return;
            }
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.VehicleBlackout") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.All"))
            {
                RequestGuard.Denied(source, request, "vMenu.WeatherOptions.VehicleBlackout");
                return;
            }
            if (!RequestPayload.TryBool(valueArg, out var value))
            {
                RequestGuard.Malformed(source, request, "expected true or false");
                return;
            }

            try
            {
                VehicleBlackoutEnabled = value;
            }
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }

        [EventHandler("vMenu:UpdateServerWeatherCloudsType")]
        internal void UpdateWeatherCloudsType([FromSource] Player source, object removeCloudsArg)
        {
            var request = RequestGuard.UpdateServerWeatherCloudsType;
            if (source != null && !RequestGuard.Admit(source, request))
            {
                return;
            }
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.RemoveClouds") && !IsPlayerAceAllowed(source.Handle, "vMenu.WeatherOptions.RandomizeClouds"))
            {
                RequestGuard.Denied(source, request, "vMenu.WeatherOptions.RemoveClouds or RandomizeClouds");
                return;
            }
            if (!RequestPayload.TryBool(removeCloudsArg, out var removeClouds))
            {
                RequestGuard.Malformed(source, request, "expected true or false");
                return;
            }

            try
            {
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
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }

        [EventHandler("vMenu:UpdateServerTime")]
        internal void UpdateTime([FromSource] Player source, object newHoursArg, object newMinutesArg, object freezeTimeArg)
        {
            var request = RequestGuard.UpdateServerTime;
            if (source != null && !RequestGuard.Admit(source, request))
            {
                return;
            }
            if (source != null && !IsPlayerAceAllowed(source.Handle, "vMenu.TimeOptions.Menu") && !IsPlayerAceAllowed(source.Handle, "vMenu.TimeOptions.All"))
            {
                RequestGuard.Denied(source, request, "vMenu.TimeOptions.Menu");
                return;
            }
            if (!RequestPayload.TryInt(newHoursArg, out var newHours)
                || !RequestPayload.TryInt(newMinutesArg, out var newMinutes)
                || !RequestPayload.TryBool(freezeTimeArg, out var freezeTimeNew))
            {
                RequestGuard.Malformed(source, request, "expected an hour, a minute and true/false");
                return;
            }

            try
            {
                CurrentHours = newHours;
                CurrentMinutes = newMinutes;
                FreezeTime = freezeTimeNew;
            }
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
        }
        #endregion

        #region Add teleport location (hardened)
        // A saved location is a short name, a position and a heading: well under 1 KB of JSON.
        private const int MaxTeleportLocationJson = 4096;

        [EventHandler("vMenu:SaveTeleportLocation")]
        internal void AddTeleportLocation([FromSource] Player source, object locationJsonArg)
        {
            var request = RequestGuard.SaveTeleportLocation;
            if (!RequestGuard.Admit(source, request)) return;

            if (!PermissionsManager.IsAllowed(PermissionsManager.Permission.MSTeleportSaveLocation, source)
                && !PermissionsManager.IsAllowed(PermissionsManager.Permission.MSAll, source))
            {
                RequestGuard.Denied(source, request, "vMenu.MiscSettings.TeleportSaveLocation");
                return;
            }

            if (!RequestPayload.TryString(locationJsonArg, MaxTeleportLocationJson, out var locationJson))
            {
                RequestGuard.Malformed(source, request, $"the location is not text of up to {MaxTeleportLocationJson} characters");
                return;
            }

            try
            {
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
            catch (Exception exception)
            {
                RequestGuard.Failed(source, request, exception);
            }
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

        // playerJoining and playerDropped come from the server as "internal-net:<id>". C# handlers also
        // receive events clients send; FXServer drops client copies of these exact names, and vMenu
        // checks the source itself as well (older artifacts, other spellings), logging any copy.
        [EventHandler("playerJoining")]
        internal void OnPlayerJoining([FromSource] Player sourcePlayer, [FromSource] string source)
        {
            if (!RequestGuard.IsServerPlayerEvent(source, "playerJoining"))
            {
                return;
            }

            joinedPlayers.Add(sourcePlayer.Handle);
            PermissionsManager.SetPermissionsForPlayer(sourcePlayer);

            foreach (Player player in GetJoinQuitNotifPlayers())
            {
                player.TriggerEvent("vMenu:PlayerJoinQuit", sourcePlayer.Name, null);
            }
        }

        [EventHandler("playerDropped")]
        internal void OnPlayerDropped([FromSource] Player sourcePlayer, [FromSource] string source, object reasonArg)
        {
            if (!RequestGuard.IsServerPlayerEvent(source, "playerDropped"))
            {
                return;
            }

            var reason = reasonArg as string;
            RequestGuard.Forget(sourcePlayer.Handle);
            personalVehicles.Remove(sourcePlayer.Handle);
            if (!joinedPlayers.Contains(sourcePlayer.Handle))
            {
                return;
            }

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

        // Requests a player had no permission for are logged by RequestGuard.Denied (the security
        // log). vMenu's ban system was removed (never used; EasyAdmin handles bans).
        #endregion
    }
}
