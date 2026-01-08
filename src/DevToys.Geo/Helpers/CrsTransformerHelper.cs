using System.Reflection;
using System.Text;
using DevToys.Api;
using DevToys.Geo.Models;
using DevToys.Geo.Tools.CrsTransformer;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;

namespace DevToys.Geo.Helpers;

internal static class CrsTransformerHelper
{
    private static readonly CoordinateSystemFactory _csFactory = new();
    private static readonly CoordinateTransformationFactory _ctFactory = new();
    private static readonly Dictionary<int, CoordinateSystem> _csCache = new();
    private static readonly Dictionary<int, string> _sridDatabase = new();
    private static readonly object _cacheLock = new();
    private static bool _sridDatabaseLoaded;

    /// <summary>
    /// Transform geometry from source CRS to target CRS.
    /// </summary>
    internal static async ValueTask<ResultInfo<string>> TransformAsync(
        string input,
        CrsInputFormat inputFormat,
        int sourceEpsg,
        int targetEpsg,
        Indentation indentation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(input))
        {
            return new(string.Empty, false);
        }

        if (sourceEpsg == targetEpsg)
        {
            // No transformation needed
            return new(input, true);
        }

        try
        {
            return inputFormat switch
            {
                CrsInputFormat.GeoJson => TransformGeoJson(input, sourceEpsg, targetEpsg, indentation, logger),
                CrsInputFormat.Wkt => TransformWkt(input, sourceEpsg, targetEpsg, logger),
                _ => throw new NotSupportedException($"Input format {inputFormat} is not supported.")
            };
        }
        catch (OperationCanceledException)
        {
            return new(string.Empty, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CRS transformation error");
            return new(CrsTransformer.TransformationError + ": " + ex.Message, false);
        }
    }

    /// <summary>
    /// Transform GeoJSON input.
    /// </summary>
    private static ResultInfo<string> TransformGeoJson(
        string input,
        int sourceEpsg,
        int targetEpsg,
        Indentation indentation,
        ILogger logger)
    {
        var geoJsonReader = new GeoJsonReader();

        // Parse GeoJSON to determine type
        var geoJsonObject = JObject.Parse(input);
        var type = geoJsonObject["type"]?.ToString();

        if (string.IsNullOrEmpty(type))
        {
            logger.LogError("Invalid GeoJSON: Missing 'type' property.");
            return new(CrsTransformer.InvalidInput, false);
        }

        var transform = GetMathTransform(sourceEpsg, targetEpsg);

        switch (type)
        {
            case "FeatureCollection":
                var featureCollection = geoJsonReader.Read<NetTopologySuite.Features.FeatureCollection>(input);
                foreach (var feature in featureCollection)
                {
                    if (feature.Geometry != null)
                    {
                        feature.Geometry = TransformGeometry(feature.Geometry, transform);
                    }
                }
                return SerializeToGeoJson(featureCollection, indentation);

            case "Feature":
                var singleFeature = geoJsonReader.Read<NetTopologySuite.Features.Feature>(input);
                if (singleFeature.Geometry != null)
                {
                    singleFeature.Geometry = TransformGeometry(singleFeature.Geometry, transform);
                }
                return SerializeToGeoJson(singleFeature, indentation);

            case "Point":
            case "MultiPoint":
            case "LineString":
            case "MultiLineString":
            case "Polygon":
            case "MultiPolygon":
            case "GeometryCollection":
                var geometry = geoJsonReader.Read<Geometry>(input);
                var transformedGeometry = TransformGeometry(geometry, transform);
                return SerializeToGeoJson(transformedGeometry, indentation);

            default:
                logger.LogError("Unsupported GeoJSON type: {Type}", type);
                return new(CrsTransformer.InvalidInput, false);
        }
    }

    /// <summary>
    /// Transform WKT input (supports multiple WKT geometries separated by newlines).
    /// </summary>
    private static ResultInfo<string> TransformWkt(
        string input,
        int sourceEpsg,
        int targetEpsg,
        ILogger logger)
    {
        var wktReader = new WKTReader();
        var results = new List<string>();
        var transform = GetMathTransform(sourceEpsg, targetEpsg);

        // Split input by newlines for batch processing
        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
            {
                continue;
            }

            try
            {
                var geometry = wktReader.Read(trimmedLine);
                var transformedGeometry = TransformGeometry(geometry, transform);
                results.Add(transformedGeometry.AsText());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to transform WKT line: {Line}", trimmedLine);
                results.Add($"<Error: {ex.Message}>");
            }
        }

        return new(string.Join(Environment.NewLine, results), true);
    }

    /// <summary>
    /// Get or create a MathTransform for the given EPSG codes.
    /// </summary>
    private static MathTransform GetMathTransform(int sourceEpsg, int targetEpsg)
    {
        var sourceCs = GetCoordinateSystem(sourceEpsg);
        var targetCs = GetCoordinateSystem(targetEpsg);
        var transformation = _ctFactory.CreateFromCoordinateSystems(sourceCs, targetCs);
        return transformation.MathTransform;
    }

    /// <summary>
    /// Get or create a CoordinateSystem for the given EPSG code.
    /// </summary>
    private static CoordinateSystem GetCoordinateSystem(int epsgCode)
    {
        lock (_cacheLock)
        {
            if (_csCache.TryGetValue(epsgCode, out var cached))
            {
                return cached;
            }

            var wkt = GetCoordinateSystemWkt(epsgCode);
            var cs = _csFactory.CreateFromWkt(wkt);
            _csCache[epsgCode] = cs;
            return cs;
        }
    }

    /// <summary>
    /// Load the SRID database from the embedded resource.
    /// </summary>
    private static void EnsureSridDatabaseLoaded()
    {
        if (_sridDatabaseLoaded)
        {
            return;
        }

        lock (_cacheLock)
        {
            if (_sridDatabaseLoaded)
            {
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("SRID.csv");

            if (stream == null)
            {
                throw new InvalidOperationException("SRID.csv resource not found in assembly.");
            }

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(';');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var epsgPart = line.Substring(0, separatorIndex);
                var wktPart = line.Substring(separatorIndex + 1);

                if (int.TryParse(epsgPart, out var epsgCode) && !string.IsNullOrWhiteSpace(wktPart))
                {
                    _sridDatabase[epsgCode] = wktPart;
                }
            }

            _sridDatabaseLoaded = true;
        }
    }

    /// <summary>
    /// Get the WKT definition for a coordinate system by EPSG code.
    /// </summary>
    private static string GetCoordinateSystemWkt(int epsgCode)
    {
        EnsureSridDatabaseLoaded();

        if (_sridDatabase.TryGetValue(epsgCode, out var wkt))
        {
            return wkt;
        }

        throw new NotSupportedException($"EPSG:{epsgCode} is not supported. The coordinate system was not found in the SRID database.");
    }

    /// <summary>
    /// Get the total number of supported EPSG codes.
    /// </summary>
    internal static int GetSupportedEpsgCount()
    {
        EnsureSridDatabaseLoaded();
        return _sridDatabase.Count;
    }

    /// <summary>
    /// Get all supported EPSG codes.
    /// </summary>
    internal static IReadOnlyCollection<int> GetSupportedEpsgCodes()
    {
        EnsureSridDatabaseLoaded();
        return _sridDatabase.Keys;
    }

    /// <summary>
    /// Get all supported EPSG codes with their names.
    /// </summary>
    internal static IEnumerable<(int Code, string Name)> GetAllEpsgCodesWithNames()
    {
        EnsureSridDatabaseLoaded();

        foreach (var kvp in _sridDatabase.OrderBy(x => x.Key))
        {
            var name = ExtractNameFromWkt(kvp.Value);
            yield return (kvp.Key, name);
        }
    }

    /// <summary>
    /// Extract the coordinate system name from a WKT string.
    /// </summary>
    private static string ExtractNameFromWkt(string wkt)
    {
        // WKT format: GEOGCS["Name",...] or PROJCS["Name",...]
        // Find the first quoted string
        int firstQuote = wkt.IndexOf('"');
        if (firstQuote < 0)
        {
            return "Unknown";
        }

        int secondQuote = wkt.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0)
        {
            return "Unknown";
        }

        return wkt.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    /// <summary>
    /// Transform a NetTopologySuite Geometry using ProjNet.
    /// </summary>
    private static Geometry TransformGeometry(Geometry geometry, MathTransform transform)
    {
        return TransformGeometryRecursive(geometry, transform);
    }

    /// <summary>
    /// Recursively transform all coordinates in a geometry.
    /// </summary>
    private static Geometry TransformGeometryRecursive(Geometry geometry, MathTransform transform)
    {
        var factory = geometry.Factory ?? new GeometryFactory();

        return geometry switch
        {
            Point point => TransformPoint(point, transform, factory),
            LineString lineString when lineString is not LinearRing => TransformLineString(lineString, transform, factory),
            LinearRing linearRing => TransformLinearRing(linearRing, transform, factory),
            Polygon polygon => TransformPolygon(polygon, transform, factory),
            MultiPoint multiPoint => TransformMultiPoint(multiPoint, transform, factory),
            MultiLineString multiLineString => TransformMultiLineString(multiLineString, transform, factory),
            MultiPolygon multiPolygon => TransformMultiPolygon(multiPolygon, transform, factory),
            GeometryCollection collection => TransformGeometryCollection(collection, transform, factory),
            _ => throw new NotSupportedException($"Geometry type {geometry.GeometryType} is not supported.")
        };
    }

    private static Point TransformPoint(Point point, MathTransform transform, GeometryFactory factory)
    {
        var coord = TransformCoordinate(point.Coordinate, transform);
        return factory.CreatePoint(coord);
    }

    private static LineString TransformLineString(LineString lineString, MathTransform transform, GeometryFactory factory)
    {
        var coords = lineString.Coordinates.Select(c => TransformCoordinate(c, transform)).ToArray();
        return factory.CreateLineString(coords);
    }

    private static LinearRing TransformLinearRing(LinearRing linearRing, MathTransform transform, GeometryFactory factory)
    {
        var coords = linearRing.Coordinates.Select(c => TransformCoordinate(c, transform)).ToArray();
        return factory.CreateLinearRing(coords);
    }

    private static Polygon TransformPolygon(Polygon polygon, MathTransform transform, GeometryFactory factory)
    {
        var shell = factory.CreateLinearRing(
            polygon.ExteriorRing.Coordinates.Select(c => TransformCoordinate(c, transform)).ToArray());

        var holes = polygon.InteriorRings
            .Select(ring => factory.CreateLinearRing(
                ring.Coordinates.Select(c => TransformCoordinate(c, transform)).ToArray()))
            .ToArray();

        return factory.CreatePolygon(shell, holes);
    }

    private static MultiPoint TransformMultiPoint(MultiPoint multiPoint, MathTransform transform, GeometryFactory factory)
    {
        var points = multiPoint.Geometries
            .Cast<Point>()
            .Select(p => TransformPoint(p, transform, factory))
            .ToArray();
        return factory.CreateMultiPoint(points);
    }

    private static MultiLineString TransformMultiLineString(MultiLineString multiLineString, MathTransform transform, GeometryFactory factory)
    {
        var lineStrings = multiLineString.Geometries
            .Cast<LineString>()
            .Select(ls => TransformLineString(ls, transform, factory))
            .ToArray();
        return factory.CreateMultiLineString(lineStrings);
    }

    private static MultiPolygon TransformMultiPolygon(MultiPolygon multiPolygon, MathTransform transform, GeometryFactory factory)
    {
        var polygons = multiPolygon.Geometries
            .Cast<Polygon>()
            .Select(p => TransformPolygon(p, transform, factory))
            .ToArray();
        return factory.CreateMultiPolygon(polygons);
    }

    private static GeometryCollection TransformGeometryCollection(GeometryCollection collection, MathTransform transform, GeometryFactory factory)
    {
        var geometries = collection.Geometries
            .Select(g => TransformGeometryRecursive(g, transform))
            .ToArray();
        return factory.CreateGeometryCollection(geometries);
    }

    /// <summary>
    /// Transform a single coordinate using ProjNet.
    /// </summary>
    private static NtsCoordinate TransformCoordinate(NtsCoordinate coord, MathTransform transform)
    {
        var result = transform.Transform(coord.X, coord.Y);

        return double.IsNaN(coord.Z)
            ? new NtsCoordinate(result.x, result.y)
            : new CoordinateZ(result.x, result.y, coord.Z);
    }

    /// <summary>
    /// Serialize an object to GeoJSON with proper indentation.
    /// </summary>
    private static ResultInfo<string> SerializeToGeoJson(object geoObject, Indentation indentation)
    {
        var geoJsonWriter = new GeoJsonWriter();
        var stringBuilder = new StringBuilder();
        using var stringWriter = new StringWriter(stringBuilder);
        using var jsonTextWriter = new JsonTextWriter(stringWriter);

        jsonTextWriter.Formatting = Formatting.Indented;
        jsonTextWriter.IndentChar = ' ';
        jsonTextWriter.Indentation = indentation switch
        {
            Indentation.TwoSpaces => 2,
            Indentation.FourSpaces => 4,
            _ => 2
        };

        geoJsonWriter.Write(geoObject, jsonTextWriter);

        return new(stringBuilder.ToString(), true);
    }

    /// <summary>
    /// Detect the input format (GeoJSON or WKT).
    /// </summary>
    internal static CrsInputFormat? DetectFormat(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        input = input.Trim();

        // Check for GeoJSON (starts with { and contains "type")
        if (input.StartsWith('{') && input.Contains("\"type\""))
        {
            return CrsInputFormat.GeoJson;
        }

        // Check for WKT patterns
        string[] wktKeywords = ["POINT", "LINESTRING", "POLYGON", "MULTIPOINT",
            "MULTILINESTRING", "MULTIPOLYGON", "GEOMETRYCOLLECTION"];

        var upperInput = input.ToUpperInvariant();
        if (wktKeywords.Any(kw => upperInput.StartsWith(kw)))
        {
            return CrsInputFormat.Wkt;
        }

        return null;
    }

    /// <summary>
    /// Check if an EPSG code is supported.
    /// </summary>
    internal static bool IsValidEpsgCode(int epsgCode)
    {
        EnsureSridDatabaseLoaded();
        return _sridDatabase.ContainsKey(epsgCode);
    }
}
