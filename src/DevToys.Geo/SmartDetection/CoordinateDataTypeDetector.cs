using DevToys.Api;
using DevToys.Geo.Helpers;
using Microsoft.Extensions.Logging;
using System.ComponentModel.Composition;

namespace DevToys.Geo.SmartDetection;

[Export(typeof(IDataTypeDetector))]
[DataTypeName(InternalName, baseName: PredefinedCommonDataTypeNames.Text)]
internal sealed partial class CoordinateDataTypeDetector : IDataTypeDetector
{
    internal const string InternalName = "Coordinate";

    private readonly ILogger _logger;

    [ImportingConstructor]
    public CoordinateDataTypeDetector()
    {
        _logger = this.Log();
    }

    public ValueTask<DataDetectionResult> TryDetectDataAsync(
        object data,
        DataDetectionResult? resultFromBaseDetector,
        CancellationToken cancellationToken)
    {
        if (resultFromBaseDetector is not null
            && resultFromBaseDetector.Data is string dataString)
        {
            if (CoordinateFormatHelper.IsValidCoordinate(dataString))
            {
                return ValueTask.FromResult(new DataDetectionResult(Success: true, Data: dataString));
            }
        }

        return ValueTask.FromResult(DataDetectionResult.Unsuccessful);
    }
}
