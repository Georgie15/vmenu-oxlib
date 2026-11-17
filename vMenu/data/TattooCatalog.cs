using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace vMenuClient.data
{
    /// <summary>
    /// A tattoo the game's tattoo shops sell to a freemode character, as the shop data gives it:
    /// hashes only (GET_TATTOO_SHOP_DLC_ITEM_DATA).
    /// </summary>
    public struct ShopTattoo
    {
        public int gender;
        public uint collectionHash;
        public uint nameHash;

        /// <param name="gender">0 = male (shop character type 3), 1 = female (type 4)</param>
        public ShopTattoo(int gender, uint collectionHash, uint nameHash)
        {
            this.gender = gender;
            this.collectionHash = collectionHash;
            this.nameHash = nameHash;
        }
    }

    /// <summary>
    /// Builds the character creator's tattoo list from vMenu's list (overlays.json) and what the
    /// running game reports. Natives come in as arguments, so this has no FiveM dependency.
    ///
    /// - The game's own zone for each listed tattoo (GET_PED_DECORATION_ZONE_FROM_HASHES) must
    ///   match the list, otherwise this game build doesn't have it and it's left out.
    /// - Tattoos the game's shops sell that the list doesn't have (a newer DLC) are added under
    ///   the game's zone. Their names aren't known, so they're named by hash ("0x" + 8 hex digits),
    ///   which is also what a character saves for them.
    /// - If the game's answers don't line up with the list (a native behaving differently), the
    ///   list is used as is: nothing is hidden and nothing is added.
    /// </summary>
    public static class TattooCatalog
    {
        /// <summary>Michael, Franklin and Trevor's tattoos. MP shops never offer them to freemode characters.</summary>
        public const string SinglePlayerCollection = "singleplayer_overlays";

        /// <summary>The original GTA Online tattoos, present in every game build: used to check the zone native.</summary>
        public const string BaseCollection = "multiplayer_overlays";

        public sealed class Result
        {
            public List<Tattoo> Tattoos = new List<Tattoo>();
            /// <summary>Listed tattoos kept.</summary>
            public int FromList;
            /// <summary>Listed tattoos left out because this game build doesn't have them.</summary>
            public int NotInGame;
            /// <summary>Tattoos only the game's shops know about.</summary>
            public int FromShops;
            /// <summary>The zone native agreed with the list, so missing tattoos could be left out.</summary>
            public bool GameChecked;
            /// <summary>The shop data matched known tattoos, so unknown ones could be added.</summary>
            public bool ShopsUsed;
        }

        /// <summary>
        /// GET_HASH_KEY (rage atStringHash, Jenkins one-at-a-time): ASCII A-Z lower-cased, '\' read as '/'.
        /// </summary>
        public static uint Joaat(string text)
        {
            uint hash = 0;
            foreach (var b in Encoding.UTF8.GetBytes(text ?? string.Empty))
            {
                uint c = b;
                if (c >= 'A' && c <= 'Z')
                {
                    c += 'a' - 'A';
                }
                else if (c == '\\')
                {
                    c = '/';
                }
                hash += c;
                hash += hash << 10;
                hash ^= hash >> 6;
            }
            hash += hash << 3;
            hash ^= hash >> 11;
            hash += hash << 15;
            return hash;
        }

        /// <summary>The name saved for a tattoo or collection known only by its hash.</summary>
        public static string HashName(uint hash) => "0x" + hash.ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>Reads a name written by <see cref="HashName"/>. Real tattoo and collection names never look like one.</summary>
        public static bool TryParseHashName(string name, out uint hash)
        {
            hash = 0;
            return name != null && name.Length == 10 && name.StartsWith("0x", StringComparison.Ordinal)
                && uint.TryParse(name.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hash);
        }

        private static ulong Key(uint collectionHash, uint nameHash) => ((ulong)collectionHash << 32) | nameHash;

        private static uint HashOfName(string name) => TryParseHashName(name, out var hash) ? hash : Joaat(name);

        /// <summary>The tattoos that have real names, by hash, for <see cref="UseNames"/>.</summary>
        public static Dictionary<ulong, KeyValuePair<string, string>> NamedByHash(IEnumerable<Tattoo> tattoos)
        {
            var named = new Dictionary<ulong, KeyValuePair<string, string>>();
            foreach (var tattoo in tattoos)
            {
                if (string.IsNullOrEmpty(tattoo.name) || string.IsNullOrEmpty(tattoo.collectionName)
                    || TryParseHashName(tattoo.name, out _) || TryParseHashName(tattoo.collectionName, out _))
                {
                    continue;
                }
                named[Key(Joaat(tattoo.collectionName), Joaat(tattoo.name))] = new KeyValuePair<string, string>(tattoo.collectionName, tattoo.name);
            }
            return named;
        }

        /// <summary>
        /// A tattoo saved by hash (known then only from the game's shops) that the list now names is
        /// saved under its name instead, so the menu shows it as worn; a copy already saved by name is
        /// kept and the hash one dropped. Returns whether anything changed.
        /// </summary>
        public static bool UseNames(List<KeyValuePair<string, string>> saved, Func<Dictionary<ulong, KeyValuePair<string, string>>> named)
        {
            if (saved == null)
            {
                return false;
            }
            Dictionary<ulong, KeyValuePair<string, string>> lookup = null;
            var changed = false;
            for (var i = saved.Count - 1; i >= 0; i--)
            {
                var tattoo = saved[i];
                if (!TryParseHashName(tattoo.Key, out _) && !TryParseHashName(tattoo.Value, out _))
                {
                    continue;
                }
                lookup ??= named();
                if (lookup == null || !lookup.TryGetValue(Key(HashOfName(tattoo.Key), HashOfName(tattoo.Value)), out var withName))
                {
                    continue;
                }
                if (saved.Contains(withName))
                {
                    saved.RemoveAt(i);
                }
                else
                {
                    saved[i] = withName;
                }
                changed = true;
            }
            return changed;
        }

        /// <param name="listed">vMenu's tattoo list (overlays.json).</param>
        /// <param name="zoneOf">GET_PED_DECORATION_ZONE_FROM_HASHES(collection, name), or null to use the list as is.</param>
        /// <param name="shopTattoos">Every tattoo the game's shops sell to freemode characters.</param>
        public static Result Build(IList<Tattoo> listed, Func<uint, uint, int> zoneOf, IList<ShopTattoo> shopTattoos)
        {
            var result = new Result();
            var candidates = new List<Tattoo>();
            var known = new HashSet<ulong>();
            foreach (var tattoo in listed)
            {
                if (string.IsNullOrEmpty(tattoo.name) || string.IsNullOrEmpty(tattoo.collectionName))
                {
                    continue;
                }
                known.Add(Key(Joaat(tattoo.collectionName), Joaat(tattoo.name)));
                if (!string.Equals(tattoo.collectionName, SinglePlayerCollection, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(tattoo);
                }
            }

            // Ask the game for each listed tattoo's zone, then only trust the answers if the base
            // collection, which every build has, comes back as listed.
            var zones = new int[candidates.Count];
            var baseListed = 0;
            var baseMatched = 0;
            if (zoneOf != null)
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    zones[i] = zoneOf(Joaat(candidates[i].collectionName), Joaat(candidates[i].name));
                    if (string.Equals(candidates[i].collectionName, BaseCollection, StringComparison.OrdinalIgnoreCase))
                    {
                        baseListed++;
                        if (zones[i] == (int)candidates[i].zoneId)
                        {
                            baseMatched++;
                        }
                    }
                }
            }
            result.GameChecked = zoneOf != null && baseListed > 0 && baseMatched * 10 >= baseListed * 9;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (result.GameChecked && zones[i] != (int)candidates[i].zoneId)
                {
                    result.NotInGame++;
                    continue;
                }
                result.Tattoos.Add(candidates[i]);
                result.FromList++;
            }

            // The shop data is read at fixed offsets. Only trust it if most of what it returns is a
            // tattoo already listed; otherwise the offsets don't fit this game build.
            if (!result.GameChecked || shopTattoos == null || shopTattoos.Count == 0)
            {
                return result;
            }
            var matched = 0;
            foreach (var item in shopTattoos)
            {
                if (known.Contains(Key(item.collectionHash, item.nameHash)))
                {
                    matched++;
                }
            }
            result.ShopsUsed = matched * 2 >= shopTattoos.Count;
            if (!result.ShopsUsed)
            {
                return result;
            }

            var added = new HashSet<ulong>[] { new HashSet<ulong>(), new HashSet<ulong>() };
            foreach (var item in shopTattoos)
            {
                var key = Key(item.collectionHash, item.nameHash);
                if (item.gender is not (0 or 1) || known.Contains(key) || !added[item.gender].Add(key))
                {
                    continue;
                }
                var zone = zoneOf(item.collectionHash, item.nameHash);
                if (zone < (int)TattooZone.ZONE_TORSO || zone > (int)TattooZone.ZONE_RIGHT_LEG)
                {
                    continue;
                }
                result.Tattoos.Add(new Tattoo(item.gender, HashName(item.nameHash), HashName(item.collectionHash), (TattooZone)zone, "TYPE_TATTOO"));
                result.FromShops++;
            }
            return result;
        }
    }
}
