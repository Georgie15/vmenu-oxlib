// Run: dotnet run --project tests/tattoo-catalog
// Drives the production TattooCatalog and TattoosData (natives stubbed in Stubs.cs): vMenu's tattoo
// list, how it is checked against the running game, how newer shop tattoos are added and saved by
// hash, and the fall back to the list as is when the game's answers can't be trusted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using vMenuClient.data;
using Natives = CitizenFX.Core.Native;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static ulong Key(string collection, string name) => ((ulong)TattooCatalog.Joaat(collection) << 32) | TattooCatalog.Joaat(name);
    static ulong Key(uint collection, uint name) => ((ulong)collection << 32) | name;

    static int Main()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(dir, Path.Combine(parts))).Replace("\r\n", "\n");

        // --- Hashing and hash names --------------------------------------------------------
        Check(TattooCatalog.Joaat("adder") == 0xB779A091u, "joaat(adder)");
        Check(TattooCatalog.Joaat("ADDER") == 0xB779A091u, "joaat lower-cases like GET_HASH_KEY");
        Check(TattooCatalog.Joaat("weapon_pistol") == 0x1B06D571u, "joaat(weapon_pistol)");
        Check(TattooCatalog.Joaat("mp_m_freemode_01") == 0x705E61F2u, "joaat(mp_m_freemode_01)");
        Check(TattooCatalog.Joaat("mp_f_freemode_01") == 0x9C9EFFD8u, "joaat(mp_f_freemode_01)");
        foreach (var hash in new uint[] { 0, 1, 0xABCD, 0xDEADBEEF, uint.MaxValue })
        {
            Check(TattooCatalog.TryParseHashName(TattooCatalog.HashName(hash), out var back) && back == hash, "hash name round trip " + hash);
        }
        Check(TattooCatalog.HashName(0xABCD) == "0x0000ABCD", "hash names are 0x + 8 upper-case hex digits");
        foreach (var notHash in new[] { null, "", "0x123", "0xZZZZZZZZ", "1x0000ABCD", "0x0000ABCDE", "mpBeach_overlays" })
        {
            Check(!TattooCatalog.TryParseHashName(notHash, out _), "not a hash name: " + notHash);
        }
        Console.WriteLine("PASS: GET_HASH_KEY hashing, hash names round trip");

        // --- vMenu's list (overlays.json) -----------------------------------------------------
        var overlaysPath = Path.Combine(dir, "vMenu", "data", "overlays.json");
        var listed = JsonConvert.DeserializeObject<List<Tattoo>>(File.ReadAllText(overlaysPath));
        Check(listed.Count == 3429, "legacy v3.9.2's 3429 overlays");
        Check(listed.Select(t => (t.collectionName, t.name, t.gender)).Distinct().Count() == listed.Count, "no overlay listed twice");
        Check(listed.All(t => t.zoneId >= TattooZone.ZONE_TORSO && t.zoneId <= TattooZone.ZONE_RIGHT_LEG), "every overlay has a body zone");
        Check(listed.All(t => t.type is "TYPE_TATTOO" or "TYPE_BADGE"), "tattoos and badges only");
        Check(listed.All(t => t.gender is 0 or 1 or 2), "gender is male, female or both");
        Check(listed.All(t => t.collectionName.EndsWith("_overlays") && Regex.IsMatch(t.name, "^[A-Za-z0-9_]+$")), "plain collection and overlay names");
        Check(listed.All(t => !TattooCatalog.TryParseHashName(t.name, out _) && !TattooCatalog.TryParseHashName(t.collectionName, out _)),
            "no real name can be mistaken for a hash name");
        var collections = new HashSet<string>(listed.Select(t => t.collectionName));
        foreach (var added in new[] { "mpHeist3_overlays", "mpHeist4_overlays", "mpSum_overlays", "mpSum2_overlays", "mpChristmas3_overlays",
                     "mpVinewood_overlays", "mpSecurity_overlays", "mpTuner_overlays", "mp2023_01_overlays", "mp2023_02_overlays", "mp2024_02_overlays" })
        {
            Check(collections.Contains(added), "collection now listed: " + added);
        }
        // One overlay from each collection the previous list had: saved characters name these.
        var previous = new (string collection, string name, int gender)[]
        {
            ("mpAirraces_overlays", "MP_Airraces_Tattoo_000_M", 0), ("mpBattle_overlays", "MP_Battle_Clothing_000_M", 0),
            ("mpBeach_overlays", "MP_Bea_F_Back_000", 1), ("mpBeach_overlays", "MP_Bea_M_Back_000", 0),
            ("mpBiker_overlays", "MP_Biker_Award_000_M", 0), ("mpBusiness_overlays", "MP_Buis_M_Neck_000", 0),
            ("mpChristmas2017_overlays", "MP_Christmas2017_Tattoo_000_M", 0), ("mpChristmas2018_overlays", "MP_Christmas2018_Tat_000_M", 0),
            ("mpChristmas2_overlays", "MP_Xmas2_M_Tat_000", 0), ("mpExecutive_overlays", "MP_Securoserv_000_M", 0),
            ("mpGunrunning_overlays", "MP_Gunrunning_Award_000_M", 0), ("mpHalloween_overlays", "HW_Tee_000_M", 0),
            ("mpHeist_overlays", "MP_Award_M_Tshirt_004", 0), ("mpHipster_overlays", "FM_Hip_M_Hair_000_a", 0),
            ("mpImportExport_overlays", "MP_MP_ImportExport_Tat_000_M", 0), ("mpIndependance_overlays", "FM_Ind_M_Award_000", 0),
            ("mpIndependence_overlays", "FM_Ind_M_Award_000", 0), ("mpLowrider2_overlays", "MP_LR_Tat_000_M", 0),
            ("mpLowrider_overlays", "MP_LR_Tat_001_M", 0), ("mpLTS_overlays", "FM_LTS_M_Tshirt_000", 0),
            ("mpLuxe2_overlays", "MP_LUXE_LC_000_M", 0), ("mpLuxe_overlays", "MP_LUXE_LC_004_M", 0),
            ("mpPilot_overlays", "MP_Fli_M_Tshirt_000", 0), ("mpSmuggler_overlays", "MP_Smuggler_Graphic_000_M", 0),
            ("mpStunt_overlays", "MP_MP_Stunt_Tat_000_M", 0), ("mpValentines_overlays", "MP_Val_M_Tshirt_A", 0),
            ("mpxmas_604490_overlays", "MP_IHeartLC_000_M", 0), ("multiplayer_overlays", "FM_CREW_M_000_A", 0),
        };
        foreach (var p in previous)
        {
            Check(listed.Any(t => t.collectionName == p.collection && t.name == p.name && t.gender == p.gender), $"still listed, same spelling: {p.collection}/{p.name}");
        }
        Console.WriteLine($"PASS: overlays.json: {listed.Count} overlays in {collections.Count} collections, no duplicates, previous names kept");

        // --- TattooCatalog.Build ----------------------------------------------------------------
        var gameZones = listed.GroupBy(t => Key(t.collectionName, t.name)).ToDictionary(g => g.Key, g => (int)g.First().zoneId);
        Func<uint, uint, int> fullGame = (c, n) => gameZones.TryGetValue(Key(c, n), out var z) ? z : -1;
        var nonSp = listed.Where(t => t.collectionName != TattooCatalog.SinglePlayerCollection).ToList();
        Check(nonSp.Count == 3334, "95 single-player overlays are left out of freemode menus");

        var all = TattooCatalog.Build(listed, fullGame, new List<ShopTattoo>());
        Check(all.GameChecked && !all.ShopsUsed && all.NotInGame == 0 && all.FromShops == 0, "a current game: everything checked, nothing missing");
        Check(all.FromList == nonSp.Count && all.Tattoos.Count == nonSp.Count, "a current game keeps every freemode overlay");
        Check(all.Tattoos.All(t => t.collectionName != TattooCatalog.SinglePlayerCollection), "no single-player tattoos");

        var newer = new HashSet<string> { "mpChristmas3_overlays", "mp2023_01_overlays", "mp2023_02_overlays", "mp2024_02_overlays" };
        var newerHashes = new HashSet<uint>(newer.Select(TattooCatalog.Joaat));
        Func<uint, uint, int> olderGame = (c, n) => newerHashes.Contains(c) ? -1 : fullGame(c, n);
        var older = TattooCatalog.Build(listed, olderGame, null);
        Check(older.GameChecked && older.NotInGame == 109 + 19 + 66 + 2, "an older game build: its missing collections are counted");
        Check(older.Tattoos.All(t => !newer.Contains(t.collectionName)) && older.FromList == nonSp.Count - older.NotInGame, "and left out");

        foreach (var broken in new Func<uint, uint, int>[] { (c, n) => 0, (c, n) => 6, (c, n) => -1, null })
        {
            var asIs = TattooCatalog.Build(listed, broken, new List<ShopTattoo> { new ShopTattoo(0, 1, 2) });
            Check(!asIs.GameChecked && !asIs.ShopsUsed && asIs.NotInGame == 0 && asIs.FromShops == 0 && asIs.Tattoos.Count == nonSp.Count,
                "a zone native that doesn't agree with the base collection: the list is used as is");
        }

        ShopTattoo Shop(Tattoo t, int gender) => new ShopTattoo(gender, TattooCatalog.Joaat(t.collectionName), TattooCatalog.Joaat(t.name));
        var maleShop = nonSp.Where(t => t.type == "TYPE_TATTOO" && t.gender != 1).Select(t => Shop(t, 0)).ToList();
        var femaleShop = nonSp.Where(t => t.type == "TYPE_TATTOO" && t.gender != 0).Select(t => Shop(t, 1)).ToList();
        const uint newCollection = 0x11111111, newMale1 = 0x22222222, newMale2 = 0x33333333, newFemale = 0x44444444, noZone = 0x55555555;
        var shopZones = new Dictionary<ulong, int>
        {
            [Key(newCollection, newMale1)] = (int)TattooZone.ZONE_RIGHT_ARM,
            [Key(newCollection, newMale2)] = (int)TattooZone.ZONE_LEFT_LEG,
            [Key(newCollection, newFemale)] = (int)TattooZone.ZONE_HEAD,
            [Key(newCollection, noZone)] = (int)TattooZone.ZONE_NONE,
        };
        Func<uint, uint, int> withShops = (c, n) => shopZones.TryGetValue(Key(c, n), out var z) ? z : fullGame(c, n);
        maleShop.Add(new ShopTattoo(0, newCollection, newMale1));
        maleShop.Add(new ShopTattoo(0, newCollection, newMale2));
        maleShop.Add(new ShopTattoo(0, newCollection, newMale1));
        femaleShop.Add(new ShopTattoo(1, newCollection, newFemale));
        femaleShop.Add(new ShopTattoo(1, newCollection, noZone));
        femaleShop.Add(new ShopTattoo(1, 0x66666666, 0x77777777));
        var shops = TattooCatalog.Build(listed, withShops, maleShop.Concat(femaleShop).ToList());
        Check(shops.ShopsUsed && shops.FromShops == 3, "three new shop tattoos: duplicates and ones without a body zone skipped");
        var fromShops = shops.Tattoos.Skip(shops.FromList).ToList();
        Check(fromShops.Count == 3 && fromShops.All(t => t.type == "TYPE_TATTOO" && t.collectionName == "0x11111111"), "named by hash, as tattoos");
        Check(fromShops.Any(t => t.gender == 0 && t.name == "0x22222222" && t.zoneId == TattooZone.ZONE_RIGHT_ARM), "male, under the game's zone");
        Check(fromShops.Any(t => t.gender == 0 && t.name == "0x33333333" && t.zoneId == TattooZone.ZONE_LEFT_LEG), "male, second");
        Check(fromShops.Any(t => t.gender == 1 && t.name == "0x44444444" && t.zoneId == TattooZone.ZONE_HEAD), "female");
        Check(shops.FromList == nonSp.Count, "shop data doesn't change the listed tattoos");

        var garbage = Enumerable.Range(1, 500).Select(i => new ShopTattoo(i % 2, (uint)i * 7919u, (uint)i * 104729u)).ToList();
        Func<uint, uint, int> anyZone = (c, n) => gameZones.TryGetValue(Key(c, n), out var z) ? z : 0;
        var misread = TattooCatalog.Build(listed, anyZone, garbage);
        Check(misread.GameChecked && !misread.ShopsUsed && misread.FromShops == 0, "shop data that matches nothing listed (wrong offsets) adds nothing");
        Console.WriteLine("PASS: Build checks the list against the game, adds newer shop tattoos, and falls back to the list as is");

        // --- TattoosData end to end, natives stubbed ------------------------------------------------
        vMenuClient.Properties.Resources.OverlaysPath = overlaysPath;
        Natives.API.Zone = (c, n) => olderGame(c, n) is var z && z >= 0 ? z : (shopZones.TryGetValue(Key(c, n), out var s) ? s : -1);
        var shopByType = new Dictionary<int, List<ShopTattoo>> { [3] = maleShop, [4] = femaleShop };
        Natives.API.ShopCount = type => shopByType.TryGetValue(type, out var list) ? list.Count + 1 : 0;
        var calls = 0;
        Natives.Function.Handler = (hash, args) =>
        {
            Check(hash == 0xFF56381874F82086, "GET_TATTOO_SHOP_DLC_ITEM_DATA");
            int type = (int)args[0], index = (int)args[1];
            var buffer = (IntPtr)args[2];
            calls++;
            for (var offset = 0; offset < 0x200; offset += 4) Check(Marshal.ReadInt32(buffer, offset) == 0, "each call starts from a zeroed buffer");
            if (index == shopByType[type].Count) return false;
            // A longer struct than today's, with other fields set, to show only 0x10 and 0x18 are read.
            for (var offset = 0; offset < 0x100; offset += 4) Marshal.WriteInt32(buffer, offset, unchecked((int)0xCCCCCCCC));
            Marshal.WriteInt32(buffer, 0x10, unchecked((int)shopByType[type][index].collectionHash));
            Marshal.WriteInt32(buffer, 0x18, unchecked((int)shopByType[type][index].nameHash));
            return true;
        };
        TattoosData.GenerateTattoosData();
        Check(calls == maleShop.Count + femaleShop.Count + 2, "every shop item of both freemode characters is read");
        Check(vMenuClient.CommonFunctions.Logged.Any(l => l.Contains($"{nonSp.Count - older.NotInGame} from vMenu's list, {older.NotInGame} left out") && l.Contains("3 more from the game's shops")),
            "the debug log reports what was used");

        List<Tattoo>[] Male() => new[] { MaleTattoosCollection.TORSO, MaleTattoosCollection.HEAD, MaleTattoosCollection.LEFT_ARM, MaleTattoosCollection.RIGHT_ARM, MaleTattoosCollection.LEFT_LEG, MaleTattoosCollection.RIGHT_LEG, MaleTattoosCollection.BADGES };
        List<Tattoo>[] Female() => new[] { FemaleTattoosCollection.TORSO, FemaleTattoosCollection.HEAD, FemaleTattoosCollection.LEFT_ARM, FemaleTattoosCollection.RIGHT_ARM, FemaleTattoosCollection.LEFT_LEG, FemaleTattoosCollection.RIGHT_LEG, FemaleTattoosCollection.BADGES };
        var kept = nonSp.Where(t => !newer.Contains(t.collectionName) && !t.name.ToLower().Contains("hair_")).ToList();
        Check(Male().Sum(l => l.Count) == kept.Count(t => t.gender != 1) + 2, "male menus: listed tattoos and badges for this build, plus 2 from the shops");
        Check(Female().Sum(l => l.Count) == kept.Count(t => t.gender != 0) + 1, "female menus: listed ones plus 1 from the shops");
        Check(Male().Concat(Female()).SelectMany(l => l).All(t => !newer.Contains(t.collectionName) && !t.name.ToLower().Contains("hair_")
            && t.collectionName != TattooCatalog.SinglePlayerCollection), "nothing missing from the build, no hair overlays, no single-player tattoos");
        Check(MaleTattoosCollection.RIGHT_ARM.Any(t => t.name == "0x22222222") && MaleTattoosCollection.LEFT_LEG.Any(t => t.name == "0x33333333")
            && FemaleTattoosCollection.HEAD.Any(t => t.name == "0x44444444"), "shop tattoos land in their zone's menu");
        Check(MaleTattoosCollection.BADGES.Count > 0 && MaleTattoosCollection.BADGES.All(t => t.type == "TYPE_BADGE"), "badges get their own menu");
        var before = MaleTattoosCollection.TORSO.Count;
        TattoosData.GenerateTattoosData();
        Check(MaleTattoosCollection.TORSO.Count == before && calls == maleShop.Count + femaleShop.Count + 2, "built once");

        // A saved tattoo is applied by the hash of what was saved: names as before, shop tattoos by their hash.
        Check(TattoosData.HashOf("0x22222222") == 0x22222222u && TattoosData.HashOf("0x0000ABCD") == 0xABCDu, "hash names apply their hash");
        Check(TattoosData.HashOf("mpBeach_overlays") == unchecked((uint)Natives.API.GetHashKey("mpBeach_overlays"))
            && TattoosData.HashOf("MP_Bea_M_Back_000") == unchecked((uint)Natives.API.GetHashKey("MP_Bea_M_Back_000")), "names apply GET_HASH_KEY, as before");
        Console.WriteLine("PASS: TattoosData builds the menus from the list and the game, once, and applies saved names and hashes");

        // --- A tattoo saved by hash that a later list names ------------------------------------------
        var named = listed.First(t => t.collectionName != TattooCatalog.SinglePlayerCollection);
        var asHash = new KeyValuePair<string, string>(TattooCatalog.HashName(TattooCatalog.Joaat(named.collectionName)), TattooCatalog.HashName(TattooCatalog.Joaat(named.name)));
        var withName = new KeyValuePair<string, string>(named.collectionName, named.name);
        var unknown = new KeyValuePair<string, string>("0x11111111", "0x99999999");
        var plain = new KeyValuePair<string, string>("mpBeach_overlays", "MP_Bea_M_Back_000");
        var lookups = 0;
        Dictionary<ulong, KeyValuePair<string, string>> Lookup() { lookups++; return TattooCatalog.NamedByHash(listed); }
        var savedTattoos = new List<KeyValuePair<string, string>> { plain, asHash, unknown };
        Check(TattooCatalog.UseNames(savedTattoos, Lookup) && savedTattoos.SequenceEqual(new[] { plain, withName, unknown }) && lookups == 1,
            "saved by hash, now named: saved under its name, so the menu shows it worn; an unknown hash stays as it is");
        Check(!TattooCatalog.UseNames(savedTattoos, Lookup) && lookups == 2, "nothing more to change");
        var both = new List<KeyValuePair<string, string>> { withName, asHash };
        Check(TattooCatalog.UseNames(both, Lookup) && both.SequenceEqual(new[] { withName }), "worn both ways: the hash copy is dropped");
        var byName = new List<KeyValuePair<string, string>> { plain, withName };
        lookups = 0;
        Check(!TattooCatalog.UseNames(byName, Lookup) && lookups == 0 && !TattooCatalog.UseNames(null, Lookup), "names only: the lookup is never built");
        Check(TattooCatalog.NamedByHash(shops.Tattoos).Values.All(t => !TattooCatalog.TryParseHashName(t.Value, out _)), "shop-only tattoos are not names");
        Console.WriteLine("PASS: tattoos saved by hash are matched to names a later list adds");

        // --- Wiring ----------------------------------------------------------------------------------
        var mp = Read("vMenu", "menus", "MpPedCustomization.cs");
        Check(!Regex.IsMatch(mp, @"GetHashKey\((tattoo|tat)\.(Key|Value)\)"), "no saved tattoo is hashed with GET_HASH_KEY directly");
        Check(Regex.Matches(mp, @"SetPedDecoration\([^;]*TattoosData\.HashOf\((tattoo|tat)\.Key\), TattoosData\.HashOf\((tattoo|tat)\.Value\)\);").Count == 21,
            "all 21 places that apply a saved or previewed tattoo use TattoosData.HashOf");
        Check(mp.Contains("TattoosData.GenerateTattoosData();"), "the character editor builds the tattoo menus from TattoosData");
        var data = Read("vMenu", "data", "TattoosData.cs");
        Check(data.Contains("catch (Exception e)") && data.Contains("catalog = TattooCatalog.Build(listed, null, null);"), "any failure reading the game falls back to the list");
        var shopRead = data.IndexOf("shopTattoos = ReadShopTattoos();", StringComparison.Ordinal);
        Check(shopRead > 0 && shopRead < data.IndexOf("catalog = TattooCatalog.Build(listed, (collection, name) => GetPedDecorationZoneFromHashes(collection, name), shopTattoos);", StringComparison.Ordinal)
            && !data.Contains("ReadShopTattoos());"), "the shop read has its own try, so its failure keeps the zone check");
        Check(Regex.Matches(mp, @"TattoosData\.UseCatalogNames\(currentCharacter\.PedTatttoos\.\w+Tattoos\);").Count == 7,
            "each of the seven saved tattoo lists is matched to names before the menu compares it");
        Check(Regex.IsMatch(data, @"ShopItemBufferSize = 0x200;") && data.Contains("Marshal.FreeHGlobal(buffer);"), "the shop buffer is large and freed");
        Console.WriteLine("PASS: wiring (all tattoo applies go through HashOf, failures fall back to the list)");
        return 0;
    }
}
