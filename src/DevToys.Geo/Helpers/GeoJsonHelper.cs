using System.Globalization;
using System.Text;
using DevToys.Api;
using DevToys.Geo.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;

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

            var jsonSerializer = JsonSerializer.CreateDefault(new JsonSerializerSettings()
            {
                FloatParseHandling = FloatParseHandling.Decimal,
                Culture = CultureInfo.InvariantCulture
            });
            geoJsonWriter.Write(geometry, jsonTextWriter);
            return new(stringBuilder.ToString(), true);
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
