using System;
using System.Collections.Generic;
using System.Linq;

namespace vMenuClient
{
    /// <summary>
    /// The player's radio choices (Vehicle Options): what the radio does when they get into a vehicle,
    /// and which stations are blocked. Stations are stored by the game's name for them
    /// (RADIO_01_CLASS_ROCK), not by list position, because the station list changes between game
    /// builds. Kept free of game calls so it can be tested; VehicleRadio applies it.
    /// </summary>
    internal static class VehicleRadioSettings
    {
        /// <summary>The game's station name for "radio off" (SET_VEH_RADIO_STATION).</summary>
        internal const string Off = "OFF";

        /// <summary>The "when getting in" choice that never touches the radio. It is also the default.</summary>
        internal const string LeaveAlone = "";

        private const char Separator = ',';

        // Stations of CitizenFX's RadioStation enum, by value: what the old "Default radio station"
        // list saved (255 was Radio Off).
        private static readonly string[] LegacyStations =
        {
            "RADIO_01_CLASS_ROCK",            // LosSantosRockRadio
            "RADIO_02_POP",                   // NonStopPopFM
            "RADIO_03_HIPHOP_NEW",            // RadioLosSantos
            "RADIO_04_PUNK",                  // ChannelX
            "RADIO_05_TALK_01",               // WestCoastTalkRadio
            "RADIO_06_COUNTRY",               // RebelRadio
            "RADIO_07_DANCE_01",              // SoulwaxFM
            "RADIO_08_MEXICAN",               // EastLosFM
            "RADIO_09_HIPHOP_OLD",            // WestCoastClassics
            "RADIO_11_TALK_02",               // BlaineCountyRadio
            "RADIO_12_REGGAE",                // TheBlueArk
            "RADIO_13_JAZZ",                  // WorldWideFM
            "RADIO_14_DANCE_02",              // FlyloFM
            "RADIO_15_MOTOWN",                // TheLowdown
            "RADIO_16_SILVERLAKE",            // RadioMirrorPark
            "RADIO_17_FUNK",                  // Space
            "RADIO_18_90S_ROCK",              // VinewoodBoulevardRadio
            "RADIO_19_USER",                  // SelfRadio
            "RADIO_20_THELAB",                // TheLab
            "RADIO_21_DLC_XM17",              // BlondedLosSantos
            "RADIO_22_DLC_BATTLE_MIX1_RADIO", // LosSantosUndergroundRadio
        };

        private const int LegacyRadioOff = 255;

        /// <summary>
        /// What to tune when the player gets in: null to leave the radio alone, <see cref="Off"/>, or a
        /// station. A station the player has since blocked is left alone too.
        /// </summary>
        internal static string StationOnEntry(string entrySetting, ICollection<string> blocked)
        {
            if (string.IsNullOrWhiteSpace(entrySetting))
            {
                return null;
            }
            var station = entrySetting.Trim();
            if (station != Off && blocked != null && blocked.Contains(station))
            {
                return null;
            }
            return station;
        }

        /// <summary>
        /// The old "Default radio station" setting, carried over once: null when there is nothing to
        /// carry over. 0 is also what an unset setting reads as (and it forced Los Santos Rock Radio
        /// on every spawn), so it is left alone.
        /// </summary>
        internal static string MigrateLegacyDefault(int legacy)
        {
            if (legacy == LegacyRadioOff)
            {
                return Off;
            }
            if (legacy > 0 && legacy < LegacyStations.Length)
            {
                return LegacyStations[legacy];
            }
            return null;
        }

        internal static HashSet<string> ParseBlocked(string stored)
        {
            var stations = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(stored))
            {
                return stations;
            }
            foreach (var part in stored.Split(Separator))
            {
                var station = part.Trim();
                if (station.Length > 0 && station != Off)
                {
                    stations.Add(station);
                }
            }
            return stations;
        }

        internal static string FormatBlocked(IEnumerable<string> stations)
        {
            var sorted = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var name in stations ?? Enumerable.Empty<string>())
            {
                var station = name?.Trim();
                if (!string.IsNullOrEmpty(station) && station != Off && station.IndexOf(Separator) < 0)
                {
                    sorted.Add(station);
                }
            }
            return string.Join(Separator.ToString(), sorted);
        }

        /// <summary>
        /// The stations to offer, in the game's order: <paramref name="gameNames"/> without blanks,
        /// repeats, Off or hidden stations, then any of <paramref name="alsoInclude"/> the game didn't
        /// list (a blocked station can drop out of the game's list, and must stay to be unblocked).
        /// </summary>
        internal static List<string> StationList(IEnumerable<string> gameNames, IEnumerable<string> alsoInclude)
        {
            var stations = new List<string>();
            foreach (var name in (gameNames ?? Enumerable.Empty<string>()).Concat(alsoInclude ?? Enumerable.Empty<string>()))
            {
                var station = name?.Trim();
                if (string.IsNullOrEmpty(station) || station == Off || station.StartsWith("HIDDEN_", StringComparison.Ordinal) || stations.Contains(station))
                {
                    continue;
                }
                stations.Add(station);
            }
            return stations;
        }

        /// <summary>
        /// The "when getting in" list's values: leave alone, Off, then each station, plus the saved
        /// station if this game build doesn't list it, so it isn't lost.
        /// </summary>
        internal static List<string> EntryOptions(IEnumerable<string> stations, string entrySetting)
        {
            var options = new List<string> { LeaveAlone, Off };
            options.AddRange(StationList(stations, string.IsNullOrWhiteSpace(entrySetting) ? null : new[] { entrySetting }));
            return options;
        }

        internal static int EntryIndex(IList<string> options, string entrySetting)
        {
            var index = options.IndexOf(string.IsNullOrWhiteSpace(entrySetting) ? LeaveAlone : entrySetting.Trim());
            return index < 0 ? 0 : index;
        }
    }
}
