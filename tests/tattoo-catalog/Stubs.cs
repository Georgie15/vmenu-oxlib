// Stand-ins for what TattoosData.cs uses from FiveM, so the production file compiles and runs here.
using System;
using System.Collections.Generic;
using System.IO;

namespace CitizenFX.Core.Native
{
    public enum Hash : ulong { }

    public static class API
    {
        public static int GetHashKey(string name) => unchecked((int)vMenuClient.data.TattooCatalog.Joaat(name));

        public static Func<uint, uint, int> Zone = (collection, name) => -1;
        public static int GetPedDecorationZoneFromHashes(uint collection, uint name) => Zone(collection, name);

        public static Func<int, int> ShopCount = characterType => 0;
        public static int GetNumTattooShopDlcItems(int characterType) => ShopCount(characterType);
    }

    public static class Function
    {
        public static Func<ulong, object[], object> Handler = (hash, args) => throw new InvalidOperationException("no native stub");
        public static T Call<T>(Hash hash, params object[] args) => (T)Handler((ulong)hash, args);
    }
}

namespace vMenuClient
{
    public static class CommonFunctions
    {
        public static readonly List<string> Logged = new List<string>();
        public static void Log(string data) => Logged.Add(data);
    }
}

namespace vMenuClient.Properties
{
    internal static class Resources
    {
        internal static string OverlaysPath;
        internal static string overlays => File.ReadAllText(OverlaysPath);
    }
}
