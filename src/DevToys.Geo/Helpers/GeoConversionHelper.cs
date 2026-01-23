using DevToys.Api;
using DevToys.Geo.Models;
using DevToys.Geo.Tools.GeoJsonWkt;
using Microsoft.Extensions.Logging;

namespace DevToys.Geo.Helpers;

internal static class GeoConversionHelper
{
    public static async ValueTask<ResultInfo<string>> ConvertAsync(
        string input,
        GeoConversion conversion,
        Indentation indentation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

        ResultInfo<string> conversionResult;
        switch (conversion)
        {
            case GeoConversion.GeoJsonToWkt:
                conversionResult = WktHelper.ConvertFromGeoJson(input, indentation, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoFormatConverter.InvalidGeoJSON, false);
                }
                break;

            case GeoConversion.WktToGeoJson:
                conversionResult = GeoJsonHelper.ConvertFromWkt(input, indentation, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoFormatConverter.InvalidWKT, false);
                }
                break;

            case GeoConversion.GpxToGeoJson:
                conversionResult = GpxHelper.ConvertToGeoJson(input, indentation, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoFormatConverter.InvalidGPX, false);
                }
                break;

            case GeoConversion.GpxToWkt:
                conversionResult = GpxHelper.ConvertToWkt(input, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoFormatConverter.InvalidGPX, false);
                }
                break;

            case GeoConversion.GeoJsonToGpx:
                conversionResult = GpxHelper.ConvertFromGeoJson(input, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoFormatConverter.InvalidGeoJSON, false);
                }
                break;

            case GeoConversion.WktToGpx:
                conversionResult = GpxHelper.ConvertFromWkt(input, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoFormatConverter.InvalidWKT, false);
                }
                break;

            default:
                throw new NotSupportedException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return conversionResult;
    }
}
