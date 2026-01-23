using DevToys.Api;
using DevToys.Geo.Helpers;
using DevToys.Geo.Models;
using DevToys.Geo.SmartDetection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.Composition;
using static DevToys.Api.GUI;

namespace DevToys.Geo.Tools.GeoFormatConverter;

[Export(typeof(IGuiTool))]
[Name("GeoFormatConverter")]
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uEEF2',
    GroupName = "Geo",
    ResourceManagerAssemblyIdentifier = nameof(DevToysGeoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.Geo.Tools.GeoFormatConverter.GeoFormatConverter",
    ShortDisplayTitleResourceName = nameof(GeoFormatConverter.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(GeoFormatConverter.LongDisplayTitle),
    DescriptionResourceName = nameof(GeoFormatConverter.Description),
    AccessibleNameResourceName = nameof(GeoFormatConverter.AccessibleName))]

public sealed class GeoFormatConverterGuiTool : IGuiTool, IDisposable
{
    private const string GeoJsonLanguage = "geojson";
    private const string WktLanguage = "wkt";
    private const string XmlLanguage = "xml";
    private const string PlainTextLanguage = "plaintext";

    private static readonly SettingDefinition<GeoConversion> _conversionMode
        = new(name: $"{nameof(GeoFormatConverterGuiTool)}.{nameof(_conversionMode)}", defaultValue: GeoConversion.GeoJsonToWkt);

    private static readonly SettingDefinition<Indentation> _indentationMode
        = new(name: $"{nameof(GeoFormatConverterGuiTool)}.{nameof(_indentationMode)}", defaultValue: Indentation.TwoSpaces);

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
    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("geo-format-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("geo-format-output-text-area");

    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public GeoFormatConverterGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        SetLanguagesForConversion(_settingsProvider.GetSetting(_conversionMode));
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
                    .Text(GeoFormatConverter.Configuration),
                    Setting("geo-format-text-conversion-setting")
                    .Icon("FluentSystemIcons", '\uF18D')
                    .Title(GeoFormatConverter.ConversionTitle)
                    .Description(GeoFormatConverter.ConversionDescription)
                    .Handle(
                        _settingsProvider,
                        _conversionMode,
                        OnConversionModeChanged,
                        Item(GeoFormatConverter.GeoJSONToWKT, GeoConversion.GeoJsonToWkt),
                        Item(GeoFormatConverter.WKTToGeoJSON, GeoConversion.WktToGeoJson),
                        Item(GeoFormatConverter.GPXToGeoJSON, GeoConversion.GpxToGeoJson),
                        Item(GeoFormatConverter.GPXToWKT, GeoConversion.GpxToWkt),
                        Item(GeoFormatConverter.GeoJSONToGPX, GeoConversion.GeoJsonToGpx),
                        Item(GeoFormatConverter.WKTToGPX, GeoConversion.WktToGpx)
                    ),
                    Setting("geo-format-text-indentation-setting")
                    .Icon("FluentSystemIcons", '\uF6F8')
                    .Title(GeoFormatConverter.Indentation)
                    .Handle(
                        _settingsProvider,
                        _indentationMode,
                        OnIndentationModelChanged,
                        Item(GeoFormatConverter.TwoSpaces, Indentation.TwoSpaces),
                        Item(GeoFormatConverter.FourSpaces, Indentation.FourSpaces)
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
                            .Title(GeoFormatConverter.Input)
                            .OnTextChanged(OnInputTextChanged))
                    .WithRightPaneChild(
                        _outputTextArea
                            .Title(GeoFormatConverter.Output)
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
            _settingsProvider.SetSetting(_conversionMode, GeoConversion.GeoJsonToWkt);
            SetLanguagesForConversion(GeoConversion.GeoJsonToWkt);
            _inputTextArea.Text(geoJsonStrongTypedParsedData);
        }

        if (dataTypeName == WktDataTypeDetector.InternalName &&
            parsedData is string wktStrongTypedParsedData)
        {
            _settingsProvider.SetSetting(_conversionMode, GeoConversion.WktToGeoJson);
            SetLanguagesForConversion(GeoConversion.WktToGeoJson);
            _inputTextArea.Text(wktStrongTypedParsedData);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private void OnConversionModeChanged(GeoConversion conversionMode)
    {
        SetLanguagesForConversion(conversionMode);
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

    private async Task ConvertAsync(string input, GeoConversion conversionModeSetting, Indentation indentationModeSetting, CancellationToken cancellationToken)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

            ResultInfo<string> conversionResult = await GeoConversionHelper.ConvertAsync(
                input,
                conversionModeSetting,
                indentationModeSetting,
                _logger,
                cancellationToken);
            _outputTextArea.Text(conversionResult.Data!);
        }
    }

    private void SetLanguagesForConversion(GeoConversion conversionMode)
    {
        var (inputLang, outputLang) = GetLanguagesForConversion(conversionMode);
        _inputTextArea.Language(inputLang);
        _outputTextArea.Language(outputLang);
    }

    private static (string inputLanguage, string outputLanguage) GetLanguagesForConversion(GeoConversion conversionMode)
    {
        return conversionMode switch
        {
            GeoConversion.GeoJsonToWkt => (GeoJsonLanguage, WktLanguage),
            GeoConversion.WktToGeoJson => (WktLanguage, GeoJsonLanguage),
            GeoConversion.GpxToGeoJson => (XmlLanguage, GeoJsonLanguage),
            GeoConversion.GpxToWkt => (XmlLanguage, WktLanguage),
            GeoConversion.GeoJsonToGpx => (GeoJsonLanguage, XmlLanguage),
            GeoConversion.WktToGpx => (WktLanguage, XmlLanguage),
            _ => (PlainTextLanguage, PlainTextLanguage)
        };
    }
}
