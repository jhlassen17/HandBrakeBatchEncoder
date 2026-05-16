using System;
using System.Collections.Generic;
using System.Text;

namespace HandBrakeBatchEncoder
{
    /// <summary>
    /// Descriptor that describes the various encoding strategies
    /// </summary>
    public static class EncodingOptions
    {
        /// <summary>
        /// Mode constants
        /// </summary>
        public const int XRMode = 1;
        public const int StandardPreset = 2;

        /// <summary>
        /// Parse a preset string into its corresponding int
        /// </summary>
        /// <param name="preset">The preset/mode in text</param>
        /// <returns>0 &gt;= good, &lt; bad</returns>
        public static int ParsePreset(string preset)
        {
            // Sanity check
            if (string.IsNullOrEmpty(preset)) return -1;

            // Clean up
            preset = preset.ToLower().Trim();

            // Switch it up
            return preset switch
            {
                "xr" => XRMode,
                "standard" => StandardPreset,
                _ => -1,
            };
        }

        /// <summary>
        /// Converts a preset identifier to its corresponding display name.
        /// </summary>
        /// <param name="preset">The integer value representing the preset to convert. Must be a valid preset identifier.</param>
        /// <returns>A string containing the display name of the specified preset. Returns an empty string if the preset is not
        /// recognized or is less than zero.</returns>
        public static string PresetToString(int preset)
        {
            // Sanity check
            if (preset < 0) return string.Empty;

            // Switch it up
            return preset switch
            {
                XRMode => "XR",
                StandardPreset => "Standard",
                _ => string.Empty,
            };
        }
    }
}
