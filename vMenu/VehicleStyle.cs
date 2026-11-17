using System;
using System.Globalization;

namespace vMenuClient
{
    /// <summary>
    /// Paint codes and roof liveries for Vehicle Options and saved vehicles. Kept free of game calls
    /// so it can be tested.
    /// </summary>
    internal static class VehicleStyle
    {
        /// <summary>
        /// Reads a hex colour code: #RRGGBB, with or without the #, in either case, or the #RGB short
        /// form (ox_lib's colour picker accepts it too). Anything else is refused.
        /// </summary>
        internal static bool TryParseColourCode(string text, out int red, out int green, out int blue)
        {
            red = green = blue = 0;
            var code = (text ?? string.Empty).Trim();
            if (code.StartsWith("#", StringComparison.Ordinal))
            {
                code = code.Substring(1);
            }
            if (code.Length == 3)
            {
                code = new string(new[] { code[0], code[0], code[1], code[1], code[2], code[2] });
            }
            if (code.Length != 6)
            {
                return false;
            }
            foreach (var c in code)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }
            red = int.Parse(code.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            green = int.Parse(code.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            blue = int.Parse(code.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }

        internal static string FormatColourCode(int red, int green, int blue)
        {
            return "#" + Clamp(red).ToString("X2", CultureInfo.InvariantCulture)
                + Clamp(green).ToString("X2", CultureInfo.InvariantCulture)
                + Clamp(blue).ToString("X2", CultureInfo.InvariantCulture);
        }

        private static int Clamp(int channel) => channel < 0 ? 0 : channel > 255 ? 255 : channel;

        /// <summary>
        /// Whether a saved vehicle's roof livery should be put back: only one it saved (saves made
        /// before roof liveries were saved have none, and spawn as before) that this vehicle has.
        /// </summary>
        internal static bool ShouldRestoreRoofLivery(int? saved, int roofLiveryCount)
        {
            return saved.HasValue && saved.Value >= 0 && saved.Value < roofLiveryCount;
        }

        /// <summary>
        /// The Roof Livery list starts with "None" (the game's -1), so list position = livery + 1.
        /// </summary>
        internal static int RoofLiveryListIndex(int roofLivery, int roofLiveryCount)
        {
            var index = roofLivery + 1;
            return index < 0 ? 0 : index > roofLiveryCount ? roofLiveryCount : index;
        }

        internal static int RoofLiveryFromListIndex(int listIndex) => listIndex - 1;
    }
}
