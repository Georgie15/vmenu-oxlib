using System.Collections.Generic;
using System.Threading.Tasks;

using CitizenFX.Core;

using static CitizenFX.Core.Native.API;
using static vMenuShared.PermissionsManager;

namespace vMenuClient
{
    /// <summary>
    /// Applies the player's radio choices (Vehicle Options, <see cref="VehicleRadioSettings"/>):
    /// blocked stations are locked and hidden from the radio wheel, and the "when getting in" choice
    /// is tuned when the player gets into a vehicle as its driver. Nothing here runs every frame, and
    /// the radio is never touched unless the player chose something.
    /// </summary>
    internal static class VehicleRadio
    {
        private static List<string> stations;
        private static bool initialized;

        /// <summary>
        /// VORadio is newer than the server build: a server without it answers with Vehicle Options
        /// itself, which is who had the old "Default radio station" list.
        /// </summary>
        internal static bool Allowed => IsAllowedWithFallback(Permission.VORadio, Permission.VOMenu);

        /// <summary>
        /// Once, when the Vehicle Options menu is built: carries over the old setting, reads the
        /// station list and blocks the player's blocked stations.
        /// </summary>
        internal static void Initialize()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;

            var legacy = UserDefaults.TakeLegacyVehicleDefaultRadio();
            var migrated = VehicleRadioSettings.MigrateLegacyDefault(legacy);
            if (migrated != null && string.IsNullOrEmpty(UserDefaults.VehicleEntryRadio))
            {
                UserDefaults.VehicleEntryRadio = migrated;
            }

            // Before blocking anything: a locked station may drop out of the game's list.
            Stations();
            ApplyBlocked(VehicleRadioSettings.ParseBlocked(UserDefaults.VehicleBlockedRadioStations));
        }

        /// <summary>
        /// The game's stations (RADIO_01_CLASS_ROCK and so on), read once, plus any blocked station
        /// the game no longer lists.
        /// </summary>
        internal static IList<string> Stations()
        {
            if (stations != null)
            {
                return stations;
            }
            var names = new List<string>();
            // GET_NUM_UNLOCKED_RADIO_STATIONS. Names past the end come back empty and are skipped.
            var count = MaxRadioStationIndex();
            for (var index = 0; index <= count; index++)
            {
                names.Add(GetRadioStationName(index));
            }
            var list = VehicleRadioSettings.StationList(names, VehicleRadioSettings.ParseBlocked(UserDefaults.VehicleBlockedRadioStations));
            // Asked before the game's audio was ready: try again next time.
            if (list.Count > 0)
            {
                stations = list;
            }
            return list;
        }

        internal static string DisplayName(string station)
        {
            if (station == VehicleRadioSettings.LeaveAlone)
            {
                return "Leave As Is";
            }
            if (station == VehicleRadioSettings.Off)
            {
                return "Radio Off";
            }
            var label = GetLabelText(station);
            return string.IsNullOrEmpty(label) || label == "NULL" ? station : label;
        }

        internal static void SetBlocked(string station, bool blocked)
        {
            var saved = VehicleRadioSettings.ParseBlocked(UserDefaults.VehicleBlockedRadioStations);
            if (blocked)
            {
                saved.Add(station);
            }
            else
            {
                saved.Remove(station);
            }
            UserDefaults.VehicleBlockedRadioStations = VehicleRadioSettings.FormatBlocked(saved);
            Block(station, blocked);
        }

        internal static void UnblockAll()
        {
            foreach (var station in VehicleRadioSettings.ParseBlocked(UserDefaults.VehicleBlockedRadioStations))
            {
                Block(station, false);
            }
            UserDefaults.VehicleBlockedRadioStations = "";
        }

        /// <summary>
        /// The player got into <paramref name="vehicle"/> (or chose to apply their choice to it now).
        /// Blocks their blocked stations again, in case something unblocked them, and tunes their
        /// "when getting in" choice if they have one and are driving.
        /// </summary>
        internal static void ApplyTo(int vehicle)
        {
            if (!initialized || !Allowed)
            {
                return;
            }
            var blocked = VehicleRadioSettings.ParseBlocked(UserDefaults.VehicleBlockedRadioStations);
            ApplyBlocked(blocked);
            var station = VehicleRadioSettings.StationOnEntry(UserDefaults.VehicleEntryRadio, blocked);
            if (station != null)
            {
                _ = TuneAsync(vehicle, station);
            }
        }

        private static void ApplyBlocked(HashSet<string> blocked)
        {
            if (!Allowed)
            {
                return;
            }
            foreach (var station in blocked)
            {
                Block(station, true);
            }
        }

        private static void Block(string station, bool blocked)
        {
            LockRadioStation(station, blocked);
            SetRadioStationIsVisible(station, !blocked);
        }

        private static async Task TuneAsync(int vehicle, string station)
        {
            // The game tunes the radio itself as you get in and the engine starts. Set the choice
            // once it's done, giving up on waiting after a few seconds.
            var giveUpAt = GetGameTimer() + 3000;
            do
            {
                await BaseScript.Delay(50);
            }
            while (IsRadioRetuning() && GetGameTimer() < giveUpAt);

            if (!DoesEntityExist(vehicle) || GetPedInVehicleSeat(vehicle, -1) != PlayerPedId() || !DoesPlayerVehHaveRadio())
            {
                return;
            }
            SetVehRadioStation(vehicle, station);
        }
    }
}
