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
    private static readonly object _cacheLock = new();

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
    /// Get the WKT definition for a coordinate system by EPSG code.
    /// </summary>
    private static string GetCoordinateSystemWkt(int epsgCode)
    {
        return epsgCode switch
        {
            // WGS 84
            4326 => "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]]",

            // WGS 84 / Pseudo-Mercator (Web Mercator)
            3857 => "PROJCS[\"WGS 84 / Pseudo-Mercator\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Mercator_1SP\"],PARAMETER[\"central_meridian\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"X\",EAST],AXIS[\"Y\",NORTH],AUTHORITY[\"EPSG\",\"3857\"]]",

            // NAD83
            4269 => "GEOGCS[\"NAD83\",DATUM[\"North_American_Datum_1983\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6269\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4269\"]]",

            // ETRS89
            4258 => "GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]]",

            // Amersfoort / RD New (Netherlands)
            28992 => "PROJCS[\"Amersfoort / RD New\",GEOGCS[\"Amersfoort\",DATUM[\"Amersfoort\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128,AUTHORITY[\"EPSG\",\"7004\"]],TOWGS84[565.4171,50.3319,465.5524,-0.398957,0.343988,-1.87740,4.0725],AUTHORITY[\"EPSG\",\"6289\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4289\"]],PROJECTION[\"Oblique_Stereographic\"],PARAMETER[\"latitude_of_origin\",52.15616055555555],PARAMETER[\"central_meridian\",5.38763888888889],PARAMETER[\"scale_factor\",0.9999079],PARAMETER[\"false_easting\",155000],PARAMETER[\"false_northing\",463000],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"X\",EAST],AXIS[\"Y\",NORTH],AUTHORITY[\"EPSG\",\"28992\"]]",

            // RGF93 / Lambert-93 (France)
            2154 => "PROJCS[\"RGF93 / Lambert-93\",GEOGCS[\"RGF93\",DATUM[\"Reseau_Geodesique_Francais_1993\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],TOWGS84[0,0,0,0,0,0,0],AUTHORITY[\"EPSG\",\"6171\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4171\"]],PROJECTION[\"Lambert_Conformal_Conic_2SP\"],PARAMETER[\"standard_parallel_1\",49],PARAMETER[\"standard_parallel_2\",44],PARAMETER[\"latitude_of_origin\",46.5],PARAMETER[\"central_meridian\",3],PARAMETER[\"false_easting\",700000],PARAMETER[\"false_northing\",6600000],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"X\",EAST],AXIS[\"Y\",NORTH],AUTHORITY[\"EPSG\",\"2154\"]]",

            // OSGB 1936 / British National Grid
            27700 => "PROJCS[\"OSGB 1936 / British National Grid\",GEOGCS[\"OSGB 1936\",DATUM[\"OSGB_1936\",SPHEROID[\"Airy 1830\",6377563.396,299.3249646,AUTHORITY[\"EPSG\",\"7001\"]],TOWGS84[446.448,-125.157,542.06,0.15,0.247,0.842,-20.489],AUTHORITY[\"EPSG\",\"6277\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4277\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",49],PARAMETER[\"central_meridian\",-2],PARAMETER[\"scale_factor\",0.9996012717],PARAMETER[\"false_easting\",400000],PARAMETER[\"false_northing\",-100000],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"27700\"]]",

            // CH1903+ / LV95 (Switzerland)
            2056 => "PROJCS[\"CH1903+ / LV95\",GEOGCS[\"CH1903+\",DATUM[\"CH1903+\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128,AUTHORITY[\"EPSG\",\"7004\"]],TOWGS84[674.374,15.056,405.346,0,0,0,0],AUTHORITY[\"EPSG\",\"6150\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4150\"]],PROJECTION[\"Hotine_Oblique_Mercator_Azimuth_Center\"],PARAMETER[\"latitude_of_center\",46.95240555555556],PARAMETER[\"longitude_of_center\",7.439583333333333],PARAMETER[\"azimuth\",90],PARAMETER[\"rectified_grid_angle\",90],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",2600000],PARAMETER[\"false_northing\",1200000],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Y\",EAST],AXIS[\"X\",NORTH],AUTHORITY[\"EPSG\",\"2056\"]]",

            // Belgian Lambert 72
            31370 => "PROJCS[\"Belge 1972 / Belgian Lambert 72\",GEOGCS[\"Belge 1972\",DATUM[\"Reseau_National_Belge_1972\",SPHEROID[\"International 1924\",6378388,297,AUTHORITY[\"EPSG\",\"7022\"]],TOWGS84[-106.8686,52.2978,-103.7239,0.3366,-0.457,1.8422,-1.2747],AUTHORITY[\"EPSG\",\"6313\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4313\"]],PROJECTION[\"Lambert_Conformal_Conic_2SP\"],PARAMETER[\"standard_parallel_1\",51.16666723333333],PARAMETER[\"standard_parallel_2\",49.8333339],PARAMETER[\"latitude_of_origin\",90],PARAMETER[\"central_meridian\",4.367486666666666],PARAMETER[\"false_easting\",150000.013],PARAMETER[\"false_northing\",5400088.438],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"X\",EAST],AXIS[\"Y\",NORTH],AUTHORITY[\"EPSG\",\"31370\"]]",

            // ETRS89-extended / LAEA Europe
            3035 => "PROJCS[\"ETRS89 / LAEA Europe\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]],PROJECTION[\"Lambert_Azimuthal_Equal_Area\"],PARAMETER[\"latitude_of_center\",52],PARAMETER[\"longitude_of_center\",10],PARAMETER[\"false_easting\",4321000],PARAMETER[\"false_northing\",3210000],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"X\",EAST],AXIS[\"Y\",NORTH],AUTHORITY[\"EPSG\",\"3035\"]]",

            // ETRS89 / UTM zone 32N
            25832 => "PROJCS[\"ETRS89 / UTM zone 32N\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",9],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"25832\"]]",

            // ETRS89 / UTM zone 33N
            25833 => "PROJCS[\"ETRS89 / UTM zone 33N\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"25833\"]]",

            // WGS 84 / UTM zone 10N
            32610 => "PROJCS[\"WGS 84 / UTM zone 10N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-123],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32610\"]]",

            // WGS 84 / UTM zone 11N
            32611 => "PROJCS[\"WGS 84 / UTM zone 11N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-117],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32611\"]]",

            // WGS 84 / UTM zone 17N
            32617 => "PROJCS[\"WGS 84 / UTM zone 17N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-81],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32617\"]]",

            // WGS 84 / UTM zone 18N
            32618 => "PROJCS[\"WGS 84 / UTM zone 18N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-75],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32618\"]]",

            // WGS 84 / UTM zone 32N
            32632 => "PROJCS[\"WGS 84 / UTM zone 32N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",9],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32632\"]]",

            // WGS 84 / UTM zone 33N
            32633 => "PROJCS[\"WGS 84 / UTM zone 33N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32633\"]]",

            _ => throw new NotSupportedException($"EPSG:{epsgCode} is not supported. Please use one of the preset EPSG codes.")
        };
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
        try
        {
            GetCoordinateSystemWkt(epsgCode);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
