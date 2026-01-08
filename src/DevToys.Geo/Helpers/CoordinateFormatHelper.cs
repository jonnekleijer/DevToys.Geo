using System.Globalization;
using System.Text.RegularExpressions;
using DevToys.Api;
using DevToys.Geo.Models;
using DevToys.Geo.Tools.CoordinateConverter;
using Microsoft.Extensions.Logging;

namespace DevToys.Geo.Helpers;

internal static partial class CoordinateFormatHelper
{
    // DD pattern: "40.7128, -74.0060" or "40.7128 -74.0060"
    private static readonly Regex DdPattern = DdRegex();

    // DMS pattern: "40° 42' 46.08" N, 74° 0' 21.6" W" (various separators)
    private static readonly Regex DmsPattern = DmsRegex();

    // DDM pattern: "40° 42.768' N, 74° 0.360' W"
    private static readonly Regex DdmPattern = DdmRegex();

    [GeneratedRegex(@"^\s*(-?\d+\.?\d*)\s*[,\s]\s*(-?\d+\.?\d*)\s*$", RegexOptions.Compiled)]
    private static partial Regex DdRegex();

    [GeneratedRegex(@"^\s*(\d+)\s*[°]\s*(\d+)\s*[′']\s*(\d+\.?\d*)\s*[″""]\s*([NSns])\s*[,\s]\s*(\d+)\s*[°]\s*(\d+)\s*[′']\s*(\d+\.?\d*)\s*[″""]\s*([EWew])\s*$", RegexOptions.Compiled)]
    private static partial Regex DmsRegex();

    [GeneratedRegex(@"^\s*(\d+)\s*[°]\s*(\d+\.?\d*)\s*[′']\s*([NSns])\s*[,\s]\s*(\d+)\s*[°]\s*(\d+\.?\d*)\s*[′']\s*([EWew])\s*$", RegexOptions.Compiled)]
    private static partial Regex DdmRegex();

    internal static async ValueTask<ResultInfo<string>> ConvertAsync(
        string input,
        CoordinateFormat inputFormat,
        CoordinateFormat outputFormat,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

        ResultInfo<Coordinate> parseResult = Parse(input, inputFormat, logger);
        if (!parseResult.HasSucceeded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(CoordinateConverter.InvalidCoordinate, false);
        }

        ResultInfo<string> formatResult = Format(parseResult.Data, outputFormat);
        if (!formatResult.HasSucceeded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(CoordinateConverter.ParseError, false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return formatResult;
    }

    internal static ResultInfo<Coordinate> Parse(string? input, CoordinateFormat format, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new(default, false);
        }

        try
        {
            return format switch
            {
                CoordinateFormat.DecimalDegrees => ParseDD(input),
                CoordinateFormat.DegreesMinutesSeconds => ParseDMS(input),
                CoordinateFormat.DegreesDecimalMinutes => ParseDDM(input),
                _ => throw new NotSupportedException()
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Coordinate parsing error");
            return new(default, false);
        }
    }

    internal static ResultInfo<string> Format(Coordinate coord, CoordinateFormat format)
    {
        try
        {
            string result = format switch
            {
                CoordinateFormat.DecimalDegrees => FormatDD(coord),
                CoordinateFormat.DegreesMinutesSeconds => FormatDMS(coord),
                CoordinateFormat.DegreesDecimalMinutes => FormatDDM(coord),
                _ => throw new NotSupportedException()
            };
            return new(result, true);
        }
        catch
        {
            return new(string.Empty, false);
        }
    }

    internal static CoordinateFormat? DetectFormat(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        input = input.Trim();

        if (DmsPattern.IsMatch(input))
        {
            return CoordinateFormat.DegreesMinutesSeconds;
        }

        if (DdmPattern.IsMatch(input))
        {
            return CoordinateFormat.DegreesDecimalMinutes;
        }

        if (DdPattern.IsMatch(input))
        {
            return CoordinateFormat.DecimalDegrees;
        }

        return null;
    }

    internal static bool IsValidCoordinate(string? input)
    {
        return DetectFormat(input) != null;
    }

    private static ResultInfo<Coordinate> ParseDD(string input)
    {
        Match match = DdPattern.Match(input);
        if (!match.Success)
        {
            return new(default, false);
        }

        double lat = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double lon = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        if (!IsValidLatitude(lat) || !IsValidLongitude(lon))
        {
            return new(default, false);
        }

        return new(new Coordinate(lat, lon), true);
    }

    private static ResultInfo<Coordinate> ParseDMS(string input)
    {
        Match match = DmsPattern.Match(input);
        if (!match.Success)
        {
            return new(default, false);
        }

        int latDeg = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int latMin = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        double latSec = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        char latDir = char.ToUpperInvariant(match.Groups[4].Value[0]);

        int lonDeg = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
        int lonMin = int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture);
        double lonSec = double.Parse(match.Groups[7].Value, CultureInfo.InvariantCulture);
        char lonDir = char.ToUpperInvariant(match.Groups[8].Value[0]);

        double lat = latDeg + (latMin / 60.0) + (latSec / 3600.0);
        double lon = lonDeg + (lonMin / 60.0) + (lonSec / 3600.0);

        if (latDir == 'S') lat = -lat;
        if (lonDir == 'W') lon = -lon;

        if (!IsValidLatitude(lat) || !IsValidLongitude(lon))
        {
            return new(default, false);
        }

        return new(new Coordinate(lat, lon), true);
    }

    private static ResultInfo<Coordinate> ParseDDM(string input)
    {
        Match match = DdmPattern.Match(input);
        if (!match.Success)
        {
            return new(default, false);
        }

        int latDeg = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double latMin = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        char latDir = char.ToUpperInvariant(match.Groups[3].Value[0]);

        int lonDeg = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        double lonMin = double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
        char lonDir = char.ToUpperInvariant(match.Groups[6].Value[0]);

        double lat = latDeg + (latMin / 60.0);
        double lon = lonDeg + (lonMin / 60.0);

        if (latDir == 'S') lat = -lat;
        if (lonDir == 'W') lon = -lon;

        if (!IsValidLatitude(lat) || !IsValidLongitude(lon))
        {
            return new(default, false);
        }

        return new(new Coordinate(lat, lon), true);
    }

    private static string FormatDD(Coordinate coord)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:F6}, {1:F6}",
            coord.Latitude,
            coord.Longitude);
    }

    private static string FormatDMS(Coordinate coord)
    {
        (int latDeg, int latMin, double latSec, char latDir) = ToDMS(coord.Latitude, true);
        (int lonDeg, int lonMin, double lonSec, char lonDir) = ToDMS(coord.Longitude, false);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}° {1}' {2:F2}\" {3}, {4}° {5}' {6:F2}\" {7}",
            latDeg, latMin, latSec, latDir,
            lonDeg, lonMin, lonSec, lonDir);
    }

    private static string FormatDDM(Coordinate coord)
    {
        (int latDeg, double latMin, char latDir) = ToDDM(coord.Latitude, true);
        (int lonDeg, double lonMin, char lonDir) = ToDDM(coord.Longitude, false);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}° {1:F4}' {2}, {3}° {4:F4}' {5}",
            latDeg, latMin, latDir,
            lonDeg, lonMin, lonDir);
    }

    private static (int degrees, int minutes, double seconds, char direction) ToDMS(double decimalDegrees, bool isLatitude)
    {
        char direction = isLatitude
            ? (decimalDegrees >= 0 ? 'N' : 'S')
            : (decimalDegrees >= 0 ? 'E' : 'W');

        double absValue = Math.Abs(decimalDegrees);
        int degrees = (int)absValue;
        double minutesDecimal = (absValue - degrees) * 60;
        int minutes = (int)minutesDecimal;
        double seconds = (minutesDecimal - minutes) * 60;

        return (degrees, minutes, seconds, direction);
    }

    private static (int degrees, double minutes, char direction) ToDDM(double decimalDegrees, bool isLatitude)
    {
        char direction = isLatitude
            ? (decimalDegrees >= 0 ? 'N' : 'S')
            : (decimalDegrees >= 0 ? 'E' : 'W');

        double absValue = Math.Abs(decimalDegrees);
        int degrees = (int)absValue;
        double minutes = (absValue - degrees) * 60;

        return (degrees, minutes, direction);
    }

    private static bool IsValidLatitude(double lat) => lat >= -90 && lat <= 90;
    private static bool IsValidLongitude(double lon) => lon >= -180 && lon <= 180;
}
