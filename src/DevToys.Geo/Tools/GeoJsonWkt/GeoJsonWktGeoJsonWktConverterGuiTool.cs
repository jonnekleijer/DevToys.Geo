using DevToys.Api;
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
internal sealed class GeoJsonWktGeoJsonWktConverterGuiTool : IGuiTool, IDisposable
{
    private const string GeoJsonLanguage = "json";
    private const string WktLanguage = "yaml";

    private static readonly SettingDefinition<GeoJsonToWktConversion> ConversionMode
        = new(name: $"{nameof(GeoJsonWktGeoJsonWktConverterGuiTool)}.{nameof(ConversionMode)}", defaultValue: GeoJsonToWktConversion.GeoJsonToWkt);

    private static readonly SettingDefinition<Indentation> IndentationMode
        = new(name: $"{nameof(GeoJsonWktGeoJsonWktConverterGuiTool)}.{nameof(IndentationMode)}", defaultValue: Indentation.TwoSpaces);

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

    private readonly ILogger Logger;
    private readonly ISettingsProvider SettingsProvider;
    private readonly IUIMultiLineTextInput InputTextArea = MultiLineTextInput("geojson-to-wkt-input-text-area");
    private readonly IUIMultiLineTextInput OutputTextArea = MultiLineTextInput("geojson-to-wkt-output-text-area");

    private CancellationTokenSource? CancellationTokenSource;

    [ImportingConstructor]
    public GeoJsonWktGeoJsonWktConverterGuiTool(ISettingsProvider settingsProvider)
    {
        Logger = this.Log();
        SettingsProvider = settingsProvider;

        switch (SettingsProvider.GetSetting(ConversionMode))
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
                    Setting("json-to-yaml-text-conversion-setting")
                    .Icon("FluentSystemIcons", '\uF18D')
                    .Title(GeoJsonWktConverter.ConversionTitle)
                    .Description(GeoJsonWktConverter.ConversionDescription)
                    .Handle(
                        SettingsProvider,
                        ConversionMode,
                        OnConversionModeChanged,
                        Item(GeoJsonWktConverter.GeoJSONToWKT, GeoJsonToWktConversion.GeoJsonToWkt),
                        Item(GeoJsonWktConverter.WKTToGeoJSON, GeoJsonToWktConversion.WktToGeoJson)
                    ),
                    Setting("json-to-yaml-text-indentation-setting")
                    .Icon("FluentSystemIcons", '\uF6F8')
                    .Title(GeoJsonWktConverter.Indentation)
                    .Handle(
                        SettingsProvider,
                        IndentationMode,
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
                        InputTextArea
                            .Title(GeoJsonWktConverter.Input)
                            .OnTextChanged(OnInputTextChanged))
                    .WithRightPaneChild(
                        OutputTextArea
                            .Title(GeoJsonWktConverter.Output)
                            .ReadOnly()
                            .Extendable())
            )
        )
    );

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (dataTypeName == PredefinedCommonDataTypeNames.Json &&
            parsedData is string jsonStrongTypedParsedData)
        {
            InputTextArea.Language(GeoJsonLanguage);
            OutputTextArea.Language(WktLanguage);
            SettingsProvider.SetSetting(ConversionMode, GeoJsonToWktConversion.GeoJsonToWkt);
            InputTextArea.Text(jsonStrongTypedParsedData);
        }

        if (dataTypeName == WktDataTypeDetector.InternalName &&
            parsedData is string yamlStrongTypedParsedData)
        {
            InputTextArea.Language(WktLanguage);
            OutputTextArea.Language(GeoJsonLanguage);
            SettingsProvider.SetSetting(ConversionMode, GeoJsonToWktConversion.WktToGeoJson);
            InputTextArea.Text(yamlStrongTypedParsedData);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource?.Cancel();
        CancellationTokenSource?.Dispose();
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

        InputTextArea.Text(OutputTextArea.Text);
    }

    private void OnIndentationModelChanged(Indentation indentationMode)
    {
        // TODO: Implement a start conversion
        //StartConvert(InputTextArea.Text);
    }
    private void OnInputTextChanged(string text)
    {
        // TODO: Implement a start conversion
        //StartConvert(text);
    }

    private void SetGeoJsonToWktConversion()
    {
        InputTextArea
            .Language(GeoJsonLanguage);
        OutputTextArea
            .Language(WktLanguage);
    }

    private void SetWktToGeoJsonConversion()
    {
        InputTextArea
            .Language(WktLanguage);
        OutputTextArea
            .Language(GeoJsonLanguage);
    }
}
