using DevToys.Api;
using DevToys.Geo.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace DevToys.Geo.Helpers;

internal static partial class WktHelper
{
    /// <summary>
    /// Convert a GeoJSON string to WKT.
    /// </summary>
    internal static ResultInfo<string> ConvertFromGeoJson(string input, Indentation indentation, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new(string.Empty, false);
        }

        try
        {
            var geoJsonReader = new GeoJsonReader();

            var features = geoJsonReader.Read<FeatureCollection>(input);

            List<Geometry> geometries = [];
            foreach (var feature in features)
            {
                geometries.Add(feature.Geometry);
            }
            var geometryCollection = new GeometryFactory().CreateGeometryCollection(geometries.ToArray());
            
            var result = geometryCollection.AsText();
            cancellationToken.ThrowIfCancellationRequested();

            return new(result, true);
        }
        catch (OperationCanceledException)
        {
            return new(string.Empty, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GeoJson to WKT Converter");
            return new(string.Empty, false);
        }
    }

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
