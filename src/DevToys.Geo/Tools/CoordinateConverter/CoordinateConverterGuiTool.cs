using DevToys.Api;
using DevToys.Geo.Helpers;
using DevToys.Geo.Models;
using DevToys.Geo.SmartDetection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.Composition;
using static DevToys.Api.GUI;

namespace DevToys.Geo.Tools.CoordinateConverter;

[Export(typeof(IGuiTool))]
[Name("CoordinateConverter")]
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE81D',
    GroupName = "Geo",
    ResourceManagerAssemblyIdentifier = nameof(DevToysGeoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.Geo.Tools.CoordinateConverter.CoordinateConverter",
    ShortDisplayTitleResourceName = nameof(CoordinateConverter.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(CoordinateConverter.LongDisplayTitle),
    DescriptionResourceName = nameof(CoordinateConverter.Description),
    AccessibleNameResourceName = nameof(CoordinateConverter.AccessibleName))]
[AcceptedDataTypeName(CoordinateDataTypeDetector.InternalName)]
public sealed class CoordinateConverterGuiTool : IGuiTool, IDisposable
{
    private static readonly SettingDefinition<CoordinateFormat> _inputFormat
        = new(name: $"{nameof(CoordinateConverterGuiTool)}.{nameof(_inputFormat)}", defaultValue: CoordinateFormat.DecimalDegrees);

    private static readonly SettingDefinition<CoordinateFormat> _outputFormat
        = new(name: $"{nameof(CoordinateConverterGuiTool)}.{nameof(_outputFormat)}", defaultValue: CoordinateFormat.DegreesMinutesSeconds);

    private enum GridColumn
    {
        Content
    }

    private enum GridRow
    {
        Header,
        Content
    }

    private readonly DisposableSemaphore _semaphore = new();
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("coordinate-converter-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("coordinate-converter-output-text-area");

    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public CoordinateConverterGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;
    }

    internal Task? WorkTask { get; private set; }

    public UIToolView View
        => new(
            isScrollable: true,
            Grid()
                .ColumnLargeSpacing()
                .RowLargeSpacing()
                .Rows(
                    (GridRow.Header, Auto),
                    (GridRow.Content, new UIGridLength(1, UIGridUnitType.Fraction))
                )
                .Columns(
                    (GridColumn.Content, new UIGridLength(1, UIGridUnitType.Fraction))
                )
            .Cells(
                Cell(
                    GridRow.Header,
                    GridColumn.Content,
                    Stack().Vertical().WithChildren(
                        Label()
                            .Text(CoordinateConverter.Configuration),
                        Setting("coordinate-converter-input-format-setting")
                            .Icon("FluentSystemIcons", '\uF18D')
                            .Title(CoordinateConverter.InputFormat)
                            .Description(CoordinateConverter.InputFormatDescription)
                            .Handle(
                                _settingsProvider,
                                _inputFormat,
                                OnInputFormatChanged,
                                Item(CoordinateConverter.DecimalDegrees, CoordinateFormat.DecimalDegrees),
                                Item(CoordinateConverter.DegreesMinutesSeconds, CoordinateFormat.DegreesMinutesSeconds),
                                Item(CoordinateConverter.DegreesDecimalMinutes, CoordinateFormat.DegreesDecimalMinutes)
                            ),
                        Setting("coordinate-converter-output-format-setting")
                            .Icon("FluentSystemIcons", '\uF18D')
                            .Title(CoordinateConverter.OutputFormat)
                            .Description(CoordinateConverter.OutputFormatDescription)
                            .Handle(
                                _settingsProvider,
                                _outputFormat,
                                OnOutputFormatChanged,
                                Item(CoordinateConverter.DecimalDegrees, CoordinateFormat.DecimalDegrees),
                                Item(CoordinateConverter.DegreesMinutesSeconds, CoordinateFormat.DegreesMinutesSeconds),
                                Item(CoordinateConverter.DegreesDecimalMinutes, CoordinateFormat.DegreesDecimalMinutes)
                            )
                    )
                ),
                Cell(
                    GridRow.Content,
                    GridColumn.Content,
                    SplitGrid()
                        .Vertical()
                        .WithLeftPaneChild(
                            _inputTextArea
                                .Title(CoordinateConverter.Input)
                                .OnTextChanged(OnInputTextChanged))
                        .WithRightPaneChild(
                            _outputTextArea
                                .Title(CoordinateConverter.Output)
                                .ReadOnly()
                                .Extendable())
                )
            )
        );

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (dataTypeName == CoordinateDataTypeDetector.InternalName &&
            parsedData is string coordinateData)
        {
            // Auto-detect the input format and set it
            CoordinateFormat? detectedFormat = CoordinateFormatHelper.DetectFormat(coordinateData);
            if (detectedFormat.HasValue)
            {
                _settingsProvider.SetSetting(_inputFormat, detectedFormat.Value);

                // Set a different output format if same as input
                if (detectedFormat.Value == _settingsProvider.GetSetting(_outputFormat))
                {
                    CoordinateFormat newOutputFormat = detectedFormat.Value switch
                    {
                        CoordinateFormat.DecimalDegrees => CoordinateFormat.DegreesMinutesSeconds,
                        CoordinateFormat.DegreesMinutesSeconds => CoordinateFormat.DecimalDegrees,
                        CoordinateFormat.DegreesDecimalMinutes => CoordinateFormat.DecimalDegrees,
                        _ => CoordinateFormat.DegreesMinutesSeconds
                    };
                    _settingsProvider.SetSetting(_outputFormat, newOutputFormat);
                }
            }

            _inputTextArea.Text(coordinateData);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private void OnInputFormatChanged(CoordinateFormat format)
    {
        StartConvert(_inputTextArea.Text);
    }

    private void OnOutputFormatChanged(CoordinateFormat format)
    {
        StartConvert(_inputTextArea.Text);
    }

    private void OnInputTextChanged(string text)
    {
        StartConvert(text);
    }

    private void StartConvert(string text)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        WorkTask = ConvertAsync(
            text,
            _settingsProvider.GetSetting(_inputFormat),
            _settingsProvider.GetSetting(_outputFormat),
            _cancellationTokenSource.Token);
    }

    private async Task ConvertAsync(
        string input,
        CoordinateFormat inputFormat,
        CoordinateFormat outputFormat,
        CancellationToken cancellationToken)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

            ResultInfo<string> conversionResult = await CoordinateFormatHelper.ConvertAsync(
                input,
                inputFormat,
                outputFormat,
                _logger,
                cancellationToken);
            _outputTextArea.Text(conversionResult.Data!);
        }
    }
}
