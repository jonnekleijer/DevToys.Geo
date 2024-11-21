using Microsoft.Extensions.Logging;

namespace DevToys.Geo.Helpers;

internal static partial class WktHelper
{
    /// <summary>
    /// Detects whether the given string is a valid WKT or not.
    /// </summary>
    internal static bool IsValid(string? input, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        input = input!.Trim();

        if (long.TryParse(input, out _))
        {
            return false;
        }

        // TODO: Parse WKT and check if it is valid.

        return true;
    }
}
