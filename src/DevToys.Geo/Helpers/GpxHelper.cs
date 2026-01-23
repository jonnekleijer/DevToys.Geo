using System.Globalization;
using System.Text;
using System.Xml;
using DevToys.Api;
using DevToys.Geo.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;

namespace DevToys.Geo.Helpers;

internal static partial class GpxHelper
{
    /// <summary>
    /// Convert a GPX string to GeoJSON.
    /// </summary>
    internal static ResultInfo<string> ConvertToGeoJson(
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
            var geometries = ParseGpxToGeometries(input, logger);

            if (geometries.Count == 0)
            {
                return new(string.Empty, false);
            }

            var geoJsonWriter = new GeoJsonWriter();
            var stringBuilder = new StringBuilder();
            using var stringWriter = new StringWriter(stringBuilder);
            using var jsonTextWriter = new JsonTextWriter(stringWriter);

            switch (indentationMode)
            {
                case Indentation.TwoSpaces:
                    jsonTextWriter.Formatting = Formatting.Indented;
                    jsonTextWriter.IndentChar = ' ';
                    jsonTextWriter.Indentation = 2;
                    break;

                case Indentation.FourSpaces:
                    jsonTextWriter.Formatting = Formatting.Indented;
                    jsonTextWriter.IndentChar = ' ';
                    jsonTextWriter.Indentation = 4;
                    break;

                default:
                    throw new NotSupportedException();
            }

            Geometry outputGeometry;
            if (geometries.Count == 1)
            {
                outputGeometry = geometries[0];
            }
            else
            {
                var factory = new GeometryFactory();
                outputGeometry = new GeometryCollection(geometries.ToArray(), factory);
            }

            geoJsonWriter.Write(outputGeometry, jsonTextWriter);
            return new(stringBuilder.ToString(), true);
        }
        catch (OperationCanceledException)
        {
            return new(string.Empty, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GPX to GeoJSON Converter");
            return new(string.Empty, false);
        }
    }

    /// <summary>
    /// Convert a GPX string to WKT.
    /// </summary>
    internal static ResultInfo<string> ConvertToWkt(
        string? input,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new(string.Empty, false);
        }

        try
        {
            var geometries = ParseGpxToGeometries(input, logger);

            if (geometries.Count == 0)
            {
                return new(string.Empty, false);
            }

            var wktList = geometries.Select(g => g.AsText()).ToList();
            return new(string.Join(Environment.NewLine, wktList), true);
        }
        catch (OperationCanceledException)
        {
            return new(string.Empty, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GPX to WKT Converter");
            return new(string.Empty, false);
        }
    }

    /// <summary>
    /// Convert a GeoJSON string to GPX.
    /// </summary>
    internal static ResultInfo<string> ConvertFromGeoJson(
        string? input,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new(string.Empty, false);
        }

        try
        {
            var geoJsonReader = new GeoJsonReader();
            var geometry = geoJsonReader.Read<Geometry>(input);

            return WriteGeometryToGpx(geometry, logger);
        }
        catch (OperationCanceledException)
        {
            return new(string.Empty, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GeoJSON to GPX Converter");
            return new(string.Empty, false);
        }
    }

    /// <summary>
    /// Convert a WKT string to GPX.
    /// </summary>
    internal static ResultInfo<string> ConvertFromWkt(
        string? input,
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
            var geometries = new List<Geometry>();

            // Handle multiple WKT geometries (one per line)
            var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var geometry = wktReader.Read(line.Trim());
                    geometries.Add(geometry);
                }
            }

            if (geometries.Count == 0)
            {
                return new(string.Empty, false);
            }

            Geometry outputGeometry;
            if (geometries.Count == 1)
            {
                outputGeometry = geometries[0];
            }
            else
            {
                var factory = new GeometryFactory();
                outputGeometry = new GeometryCollection(geometries.ToArray(), factory);
            }

            return WriteGeometryToGpx(outputGeometry, logger);
        }
        catch (OperationCanceledException)
        {
            return new(string.Empty, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WKT to GPX Converter");
            return new(string.Empty, false);
        }
    }

    private static List<Geometry> ParseGpxToGeometries(string input, ILogger logger)
    {
        var geometries = new List<Geometry>();
        var factory = new GeometryFactory();

        using var stringReader = new StringReader(input);
        var gpxReader = GpxReader.Create(stringReader, new GpxReaderSettings
        {
            IgnoreBadDateTime = true,
            IgnoreUnexpectedChildrenOfTopLevelElement = true,
            IgnoreVersionAttribute = true
        });

        // Read waypoints
        while (gpxReader.MoveToNextFeature())
        {
            var feature = gpxReader.FeatureType;

            switch (feature)
            {
                case GpxFeatureType.Waypoint:
                    var waypoint = gpxReader.ReadWaypoint();
                    var point = factory.CreatePoint(new Coordinate(
                        (double)waypoint.Longitude,
                        (double)waypoint.Latitude,
                        waypoint.ElevationInMeters.HasValue ? (double)waypoint.ElevationInMeters.Value : double.NaN));
                    geometries.Add(point);
                    break;

                case GpxFeatureType.Route:
                    var route = gpxReader.ReadRoute();
                    if (route.Waypoints.Count >= 2)
                    {
                        var routeCoords = route.Waypoints.Select(wp => new Coordinate(
                            (double)wp.Longitude,
                            (double)wp.Latitude,
                            wp.ElevationInMeters.HasValue ? (double)wp.ElevationInMeters.Value : double.NaN)).ToArray();
                        var routeLine = factory.CreateLineString(routeCoords);
                        geometries.Add(routeLine);
                    }
                    else if (route.Waypoints.Count == 1)
                    {
                        var wp = route.Waypoints[0];
                        var routePoint = factory.CreatePoint(new Coordinate(
                            (double)wp.Longitude,
                            (double)wp.Latitude,
                            wp.ElevationInMeters.HasValue ? (double)wp.ElevationInMeters.Value : double.NaN));
                        geometries.Add(routePoint);
                    }
                    break;

                case GpxFeatureType.Track:
                    var track = gpxReader.ReadTrack();
                    foreach (var segment in track.Segments)
                    {
                        if (segment.Waypoints.Count >= 2)
                        {
                            var trackCoords = segment.Waypoints.Select(wp => new Coordinate(
                                (double)wp.Longitude,
                                (double)wp.Latitude,
                                wp.ElevationInMeters.HasValue ? (double)wp.ElevationInMeters.Value : double.NaN)).ToArray();
                            var trackLine = factory.CreateLineString(trackCoords);
                            geometries.Add(trackLine);
                        }
                        else if (segment.Waypoints.Count == 1)
                        {
                            var wp = segment.Waypoints[0];
                            var trackPoint = factory.CreatePoint(new Coordinate(
                                (double)wp.Longitude,
                                (double)wp.Latitude,
                                wp.ElevationInMeters.HasValue ? (double)wp.ElevationInMeters.Value : double.NaN));
                            geometries.Add(trackPoint);
                        }
                    }
                    break;
            }
        }

        return geometries;
    }

    private static ResultInfo<string> WriteGeometryToGpx(Geometry geometry, ILogger logger)
    {
        var stringBuilder = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        };

        using (var stringWriter = new StringWriter(stringBuilder))
        using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
        {
            var gpxWriterSettings = new GpxWriterSettings
            {
                TimeZoneInfo = TimeZoneInfo.Utc
            };

            using var gpxWriter = GpxWriter.Create(xmlWriter, gpxWriterSettings);

            WriteGeometryToGpxWriter(geometry, gpxWriter, logger);
        }

        return new(stringBuilder.ToString(), true);
    }

    private static void WriteGeometryToGpxWriter(Geometry geometry, GpxWriter gpxWriter, ILogger logger)
    {
        switch (geometry)
        {
            case Point point:
                WritePointAsWaypoint(point, gpxWriter);
                break;

            case MultiPoint multiPoint:
                foreach (var p in multiPoint.Geometries)
                {
                    WritePointAsWaypoint((Point)p, gpxWriter);
                }
                break;

            case LineString lineString:
                WriteLineStringAsTrack(lineString, gpxWriter);
                break;

            case MultiLineString multiLineString:
                foreach (var ls in multiLineString.Geometries)
                {
                    WriteLineStringAsTrack((LineString)ls, gpxWriter);
                }
                break;

            case Polygon polygon:
                // Convert polygon exterior ring to track
                WriteLinearRingAsTrack(polygon.ExteriorRing, gpxWriter);
                break;

            case MultiPolygon multiPolygon:
                foreach (var p in multiPolygon.Geometries)
                {
                    WriteLinearRingAsTrack(((Polygon)p).ExteriorRing, gpxWriter);
                }
                break;

            case GeometryCollection geometryCollection:
                foreach (var g in geometryCollection.Geometries)
                {
                    WriteGeometryToGpxWriter(g, gpxWriter, logger);
                }
                break;

            default:
                logger.LogWarning("Unsupported geometry type for GPX: {GeometryType}", geometry.GeometryType);
                break;
        }
    }

    private static void WritePointAsWaypoint(Point point, GpxWriter gpxWriter)
    {
        var waypoint = new GpxWaypoint(
            new GpxLongitude(point.X),
            new GpxLatitude(point.Y),
            elevationInMeters: double.IsNaN(point.Z) ? null : point.Z);

        gpxWriter.WriteWaypoint(waypoint);
    }

    private static void WriteLineStringAsTrack(LineString lineString, GpxWriter gpxWriter)
    {
        var trackWaypoints = lineString.Coordinates.Select(coord =>
            new GpxTrackPoint(
                new GpxLongitude(coord.X),
                new GpxLatitude(coord.Y),
                elevationInMeters: double.IsNaN(coord.Z) ? null : coord.Z)).ToImmutableGpxWaypointTable();

        var segment = new GpxTrackSegment(trackWaypoints, null);
        var track = new GpxTrack(
            name: null,
            comment: null,
            description: null,
            source: null,
            links: default,
            number: null,
            classification: null,
            segments: new[] { segment }.ToImmutableArray(),
            extensions: null);

        gpxWriter.WriteTrack(track);
    }

    private static void WriteLinearRingAsTrack(LineString ring, GpxWriter gpxWriter)
    {
        WriteLineStringAsTrack(ring, gpxWriter);
    }
}
