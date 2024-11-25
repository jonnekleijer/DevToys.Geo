using System.Linq;
using DevToys.Api;
using DevToys.Geo.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json.Linq;

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

        var wktList = new List<string>();
        var geoJsonReader = new GeoJsonReader();

        try
        {
            // Parse the GeoJSON to inspect its type
            var geoJsonObject = JObject.Parse(input);
            var type = geoJsonObject["type"]?.ToString();

            if (string.IsNullOrEmpty(type))
            {
                // TODO: Move to GeoJSON validator
                logger.LogError("Invalid GeoJSON: Missing 'type' property.");
                return new(string.Empty, false);
            }

            switch (type)
            {
                case "FeatureCollection":
                    var featureCollection = geoJsonReader.Read<FeatureCollection>(input);
                    foreach (var feature in featureCollection)
                    {
                        if (feature.Geometry != null)
                        {
                            wktList.Add(feature.Geometry.AsText());
                        }
                    }
                    break;

                case "Feature":
                    var featureSingle = geoJsonReader.Read<Feature>(input);
                    if (featureSingle.Geometry != null)
                    {
                        wktList.Add(featureSingle.Geometry.AsText());
                    }
                    break;

                case "Point":
                case "MultiPoint":
                case "LineString":
                case "MultiLineString":
                case "Polygon":
                case "MultiPolygon":
                case "GeometryCollection":
                    var geometry = geoJsonReader.Read<Geometry>(input);
                    if (geometry != null)
                    {
                        wktList.Add(geometry.AsText());
                    }
                    break;

                default:
                    logger.LogError("Unsupported GeoJSON type: {type}", type);
                    break;
            }
            return new(string.Join(Environment.NewLine, wktList), true);
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
