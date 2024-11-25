using DevToys.Api;
using DevToys.Geo.Helpers;
using DevToys.Geo.Models;
using DevToys.Geo.SmartDetection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.Composition;
using static DevToys.Api.GUI;

namespace DevToys.Geo.Tools.GeoJsonWkt;

[Export(typeof(IGuiTool))]
[Name("Geo")]
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uEEF2',
    GroupName = "Geo",
    ResourceManagerAssemblyIdentifier = nameof(DevToysGeoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.Geo.Tools.GeoJsonWkt.GeoJsonWktConverter",
    ShortDisplayTitleResourceName = nameof(GeoJsonWktConverter.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(GeoJsonWktConverter.LongDisplayTitle),
    DescriptionResourceName = nameof(GeoJsonWktConverter.Description),
    AccessibleNameResourceName = nameof(GeoJsonWktConverter.AccessibleName))]

public sealed class GeoJsonWktConverterGuiTool : IGuiTool, IDisposable
{
    private const string GeoJsonLanguage = "geojson";
    private const string WktLanguage = "wkt";

    private static readonly SettingDefinition<GeoJsonToWktConversion> _conversionMode
        = new(name: $"{nameof(GeoJsonWktConverterGuiTool)}.{nameof(_conversionMode)}", defaultValue: GeoJsonToWktConversion.GeoJsonToWkt);

    private static readonly SettingDefinition<Indentation> _indentationMode
        = new(name: $"{nameof(GeoJsonWktConverterGuiTool)}.{nameof(_indentationMode)}", defaultValue: Indentation.TwoSpaces);

    private enum GridColumn
    {
        Content
    }

    private enum GridRow
    {
        Header,
        Content,
        Footer
    }

    private readonly DisposableSemaphore _semaphore = new();
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("geojson-to-wkt-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("geojson-to-wkt-output-text-area");

    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public GeoJsonWktConverterGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        switch (_settingsProvider.GetSetting(_conversionMode))
        {
            case GeoJsonToWktConversion.GeoJsonToWkt:
                SetGeoJsonToWktConversion();
                break;
            case GeoJsonToWktConversion.WktToGeoJson:
                SetWktToGeoJsonConversion();
                break;
            default:
                throw new NotSupportedException();
        }
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
                    .Text(GeoJsonWktConverter.Configuration),
                    Setting("geojson-to-wkt-text-conversion-setting")
                    .Icon("FluentSystemIcons", '\uF18D')
                    .Title(GeoJsonWktConverter.ConversionTitle)
                    .Description(GeoJsonWktConverter.ConversionDescription)
                    .Handle(
                        _settingsProvider,
                        _conversionMode,
                        OnConversionModeChanged,
                        Item(GeoJsonWktConverter.GeoJSONToWKT, GeoJsonToWktConversion.GeoJsonToWkt),
                        Item(GeoJsonWktConverter.WKTToGeoJSON, GeoJsonToWktConversion.WktToGeoJson)
                    ),
                    Setting("geojson-to-wkt-text-indentation-setting")
                    .Icon("FluentSystemIcons", '\uF6F8')
                    .Title(GeoJsonWktConverter.Indentation)
                    .Handle(
                        _settingsProvider,
                        _indentationMode,
                        OnIndentationModelChanged,
                        Item(GeoJsonWktConverter.TwoSpaces, Indentation.TwoSpaces),
                        Item(GeoJsonWktConverter.FourSpaces, Indentation.FourSpaces)
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
                            .Title(GeoJsonWktConverter.Input)
                            .OnTextChanged(OnInputTextChanged))
                    .WithRightPaneChild(
                        _outputTextArea
                            .Title(GeoJsonWktConverter.Output)
                            .ReadOnly()
                            .Extendable())
            )
        )
    );

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (dataTypeName == PredefinedCommonDataTypeNames.Json &&
            parsedData is string geoJsonStrongTypedParsedData)
        {
            _inputTextArea.Language(GeoJsonLanguage);
            _outputTextArea.Language(WktLanguage);
            _settingsProvider.SetSetting(_conversionMode, GeoJsonToWktConversion.GeoJsonToWkt);
            _inputTextArea.Text(geoJsonStrongTypedParsedData);
        }

        if (dataTypeName == WktDataTypeDetector.InternalName &&
            parsedData is string wktStrongTypedParsedData)
        {
            _inputTextArea.Language(WktLanguage);
            _outputTextArea.Language(GeoJsonLanguage);
            _settingsProvider.SetSetting(_conversionMode, GeoJsonToWktConversion.WktToGeoJson);
            _inputTextArea.Text(wktStrongTypedParsedData);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private void OnConversionModeChanged(GeoJsonToWktConversion conversionMode)
    {
        switch (conversionMode)
        {
            case GeoJsonToWktConversion.GeoJsonToWkt:
                SetGeoJsonToWktConversion();
                break;
            case GeoJsonToWktConversion.WktToGeoJson:
                SetWktToGeoJsonConversion();
                break;
            default:
                throw new NotSupportedException();
        }

        _inputTextArea.Text(_outputTextArea.Text);
    }

    private void OnIndentationModelChanged(Indentation indentationMode)
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

        WorkTask = ConvertAsync(text, _settingsProvider.GetSetting(_conversionMode), _settingsProvider.GetSetting(_indentationMode), _cancellationTokenSource.Token);
    }

    private async Task ConvertAsync(string input, GeoJsonToWktConversion conversionModeSetting, Indentation indentationModeSetting, CancellationToken cancellationToken)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

            ResultInfo<string> conversionResult = await GeoJsonWktHelper.ConvertAsync(
                input,
                conversionModeSetting,
                indentationModeSetting,
                _logger,
                cancellationToken);
            _outputTextArea.Text(conversionResult.Data!);
        }
    }


    private void SetGeoJsonToWktConversion()
    {
        _inputTextArea
            .Language(GeoJsonLanguage);
        _outputTextArea
            .Language(WktLanguage);
    }

    private void SetWktToGeoJsonConversion()
    {
        _inputTextArea
            .Language(WktLanguage);
        _outputTextArea
            .Language(GeoJsonLanguage);
    }
}
