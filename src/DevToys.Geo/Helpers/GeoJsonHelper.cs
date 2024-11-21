using DevToys.Api;
using DevToys.Geo.Models;
using Microsoft.Extensions.Logging;

namespace DevToys.Geo.Helpers;

internal static partial class GeoJsonHelper
{
    /// <summary>
    /// Convert a WKT string to GeoJSON.
    /// </summary>
    internal static ResultInfo<string> ConvertFromWkt(
        string? input,
        Indentation indentationMode,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new(string.Empty, false);
        }

        try
        {
            using var stringReader = new StringReader(input);
            
            cancellationToken.ThrowIfCancellationRequested();
            return new("Test", true);

        }
        catch (OperationCanceledException)
        {
            return new(string.Empty, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wkt to GeoJson Converter");
            return new(string.Empty, false);
        }
    }
}
