namespace DevToys.Geo.Models;

/// <summary>
/// Collection of commonly used EPSG coordinate reference systems.
/// </summary>
internal static class EpsgPresets
{
    /// <summary>
    /// All predefined EPSG presets for the dropdown.
    /// </summary>
    internal static readonly EpsgPreset[] All =
    [
        // Geographic CRS
        new(4326, "WGS 84", "World Geodetic System 1984 (GPS)"),
        new(4269, "NAD83", "North American Datum 1983"),
        new(4258, "ETRS89", "European Terrestrial Reference System 1989"),

        // Web/Projected CRS
        new(3857, "Web Mercator", "WGS 84 / Pseudo-Mercator (Google Maps, OpenStreetMap)"),

        // National/Regional CRS
        new(28992, "RD New", "Amersfoort / RD New (Netherlands)"),
        new(2154, "Lambert 93", "RGF93 / Lambert-93 (France)"),
        new(27700, "OSGB 1936", "British National Grid"),
        new(2056, "CH1903+ / LV95", "Switzerland"),
        new(31370, "Belgian Lambert 72", "Belgium"),
        new(3035, "ETRS89-LAEA", "Europe Equal Area (INSPIRE)"),

        // UTM Zones (ETRS89)
        new(25832, "ETRS89 / UTM 32N", "UTM Zone 32N (Central Europe)"),
        new(25833, "ETRS89 / UTM 33N", "UTM Zone 33N (Eastern Europe)"),

        // UTM Zones (WGS 84)
        new(32610, "WGS 84 / UTM 10N", "UTM Zone 10N (US West Coast)"),
        new(32611, "WGS 84 / UTM 11N", "UTM Zone 11N"),
        new(32617, "WGS 84 / UTM 17N", "UTM Zone 17N (US East Coast)"),
        new(32618, "WGS 84 / UTM 18N", "UTM Zone 18N"),
        new(32632, "WGS 84 / UTM 32N", "UTM Zone 32N"),
        new(32633, "WGS 84 / UTM 33N", "UTM Zone 33N"),
    ];

    /// <summary>
    /// Default source EPSG code (WGS 84).
    /// </summary>
    internal const int DefaultSourceEpsg = 4326;

    /// <summary>
    /// Default target EPSG code (Web Mercator).
    /// </summary>
    internal const int DefaultTargetEpsg = 3857;

    /// <summary>
    /// Find a preset by EPSG code.
    /// </summary>
    internal static EpsgPreset? FindByCode(int code)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].Code == code)
            {
                return All[i];
            }
        }
        return null;
    }
}
