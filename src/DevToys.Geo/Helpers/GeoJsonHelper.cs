using DevToys.Api;
using DevToys.Geo.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

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
            var wktReader = new WKTReader();

            Geometry geometry = wktReader.Read(input);

            var geoJsonWriter = new GeoJsonWriter();

            // TODO: Format GeoJSON properly
            var result = geoJsonWriter.Write(geometry);

            return new(result, true);
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
