using DevToys.Api;
using System.ComponentModel.Composition;
using DevToys.Geo.Helpers;
using Microsoft.Extensions.Logging;

namespace DevToys.Geo.SmartDetection;

[Export(typeof(IDataTypeDetector))]
[DataTypeName(InternalName, baseName: PredefinedCommonDataTypeNames.Text)]
internal sealed partial class WktDataTypeDetector : IDataTypeDetector
{
    internal const string InternalName = "Wkt";

    private readonly ILogger Logger;

    [ImportingConstructor]
    public WktDataTypeDetector()
    {
        Logger = this.Log();
    }

    public ValueTask<DataDetectionResult> TryDetectDataAsync(
        object data,
        DataDetectionResult? resultFromBaseDetector,
        CancellationToken cancellationToken)
    {
        if (resultFromBaseDetector is not null
            && resultFromBaseDetector.Data is string dataString)
        {
            if (WktHelper.IsValid(dataString, Logger))
            {
                return ValueTask.FromResult(new DataDetectionResult(Success: true, Data: dataString));
            }
        }

        return ValueTask.FromResult(DataDetectionResult.Unsuccessful);
    }
}
