using DevToys.Api;
using DevToys.Geo.Models;
using DevToys.Geo.Tools.GeoJsonWkt;
using MaxRev.Gdal.Core;
using Microsoft.Extensions.Logging;

namespace DevToys.Geo.Helpers;

internal static class GeoJsonWktHelper
{
    static GeoJsonWktHelper()
    {
        GdalBase.ConfigureAll();
    }

    public static async ValueTask<ResultInfo<string>> ConvertAsync(
        string input,
        GeoJsonToWktConversion conversion,
        Indentation indentation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

        ResultInfo<string> conversionResult;
        switch (conversion)
        {
            case GeoJsonToWktConversion.GeoJsonToWkt:
                conversionResult = WktHelper.ConvertFromGeoJson(input, indentation, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoJsonWktConverter.InvalidGeoJSON, false);
                }
                break;
            case GeoJsonToWktConversion.WktToGeoJson:
                conversionResult = GeoJsonHelper.ConvertFromWkt(input, indentation, logger, cancellationToken);
                if (!conversionResult.HasSucceeded && string.IsNullOrWhiteSpace(conversionResult.Data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(GeoJsonWktConverter.InvalidWKT, false);
                }
                break;
            default:
                throw new NotSupportedException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return conversionResult;
    }
}
