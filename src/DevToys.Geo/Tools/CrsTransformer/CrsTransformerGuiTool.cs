using DevToys.Api;
using DevToys.Geo.Helpers;
using DevToys.Geo.Models;
using DevToys.Geo.SmartDetection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.Composition;
using static DevToys.Api.GUI;

namespace DevToys.Geo.Tools.CrsTransformer;

[Export(typeof(IGuiTool))]
[Name("CrsTransformer")]
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uF4A1',
    GroupName = "Geo",
    ResourceManagerAssemblyIdentifier = nameof(DevToysGeoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.Geo.Tools.CrsTransformer.CrsTransformer",
    ShortDisplayTitleResourceName = nameof(CrsTransformer.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(CrsTransformer.LongDisplayTitle),
    DescriptionResourceName = nameof(CrsTransformer.Description),
    AccessibleNameResourceName = nameof(CrsTransformer.AccessibleName))]
[AcceptedDataTypeName(PredefinedCommonDataTypeNames.Json)]
[AcceptedDataTypeName(WktDataTypeDetector.InternalName)]
public sealed class CrsTransformerGuiTool : IGuiTool, IDisposable
{
    private const string GeoJsonLanguage = "json";
    private const string WktLanguage = "plaintext";

    // Settings definitions
    private static readonly SettingDefinition<int> _sourceEpsgSetting
        = new(name: $"{nameof(CrsTransformerGuiTool)}.{nameof(_sourceEpsgSetting)}",
              defaultValue: EpsgPresets.DefaultSourceEpsg);

    private static readonly SettingDefinition<int> _targetEpsgSetting
        = new(name: $"{nameof(CrsTransformerGuiTool)}.{nameof(_targetEpsgSetting)}",
              defaultValue: EpsgPresets.DefaultTargetEpsg);

    private static readonly SettingDefinition<CrsInputFormat> _inputFormatSetting
        = new(name: $"{nameof(CrsTransformerGuiTool)}.{nameof(_inputFormatSetting)}",
              defaultValue: CrsInputFormat.GeoJson);

    private static readonly SettingDefinition<Indentation> _indentationSetting
        = new(name: $"{nameof(CrsTransformerGuiTool)}.{nameof(_indentationSetting)}",
              defaultValue: Indentation.TwoSpaces);

    private enum GridColumn { Content }
    private enum GridRow { Header, Content }

    private readonly DisposableSemaphore _semaphore = new();
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;

    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("crs-transformer-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("crs-transformer-output-text-area");
    private readonly IUISingleLineTextInput _customSourceEpsgInput = SingleLineTextInput("crs-custom-source-epsg");
    private readonly IUISingleLineTextInput _customTargetEpsgInput = SingleLineTextInput("crs-custom-target-epsg");
    private readonly IUISelectDropDownList _sourceEpsgDropdown;
    private readonly IUISelectDropDownList _targetEpsgDropdown;

    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public CrsTransformerGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        // Build EPSG dropdown items
        var epsgItems = EpsgPresets.All
            .Select(p => Item($"EPSG:{p.Code} - {p.Name}", p.Code))
            .ToArray();

        _sourceEpsgDropdown = SelectDropDownList("crs-source-epsg-dropdown")
            .Select(GetEpsgDropdownIndex(_settingsProvider.GetSetting(_sourceEpsgSetting)));

        _targetEpsgDropdown = SelectDropDownList("crs-target-epsg-dropdown")
            .Select(GetEpsgDropdownIndex(_settingsProvider.GetSetting(_targetEpsgSetting)));

        // Set initial language
        UpdateLanguage(_settingsProvider.GetSetting(_inputFormatSetting));
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
                        Label().Text(CrsTransformer.Configuration),

                        // Source CRS setting
                        Setting("crs-source-setting")
                            .Icon("FluentSystemIcons", '\uF4A1')
                            .Title(CrsTransformer.SourceCrs)
                            .Description(CrsTransformer.SourceCrsDescription)
                            .InteractiveElement(
                                Stack().Horizontal().SmallSpacing().WithChildren(
                                    _sourceEpsgDropdown
                                        .WithItems(BuildEpsgDropdownItems())
                                        .OnItemSelected(OnSourceEpsgDropdownSelected),
                                    _customSourceEpsgInput
                                        .Title(CrsTransformer.CustomEpsg)
                                        .OnTextChanged(OnCustomSourceEpsgChanged)
                                )
                            ),

                        // Target CRS setting
                        Setting("crs-target-setting")
                            .Icon("FluentSystemIcons", '\uF4A1')
                            .Title(CrsTransformer.TargetCrs)
                            .Description(CrsTransformer.TargetCrsDescription)
                            .InteractiveElement(
                                Stack().Horizontal().SmallSpacing().WithChildren(
                                    _targetEpsgDropdown
                                        .WithItems(BuildEpsgDropdownItems())
                                        .OnItemSelected(OnTargetEpsgDropdownSelected),
                                    _customTargetEpsgInput
                                        .Title(CrsTransformer.CustomEpsg)
                                        .OnTextChanged(OnCustomTargetEpsgChanged),
                                    Button("crs-swap-button")
                                        .Icon("FluentSystemIcons", '\uF18D')
                                        .AccentAppearance()
                                        .OnClick(OnSwapCrs)
                                )
                            ),

                        // Input format setting
                        Setting("crs-input-format-setting")
                            .Icon("FluentSystemIcons", '\uF18D')
                            .Title(CrsTransformer.InputFormat)
                            .Description(CrsTransformer.InputFormatDescription)
                            .Handle(
                                _settingsProvider,
                                _inputFormatSetting,
                                OnInputFormatChanged,
                                Item(CrsTransformer.GeoJson, CrsInputFormat.GeoJson),
                                Item(CrsTransformer.Wkt, CrsInputFormat.Wkt)
                            ),

                        // Indentation setting
                        Setting("crs-indentation-setting")
                            .Icon("FluentSystemIcons", '\uF6F8')
                            .Title(CrsTransformer.Indentation)
                            .Description(CrsTransformer.IndentationDescription)
                            .Handle(
                                _settingsProvider,
                                _indentationSetting,
                                OnIndentationChanged,
                                Item(CrsTransformer.TwoSpaces, Indentation.TwoSpaces),
                                Item(CrsTransformer.FourSpaces, Indentation.FourSpaces)
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
                                .Title(CrsTransformer.Input)
                                .OnTextChanged(OnInputTextChanged))
                        .WithRightPaneChild(
                            _outputTextArea
                                .Title(CrsTransformer.Output)
                                .ReadOnly()
                                .Extendable())
                )
            )
        );

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (parsedData is not string data)
        {
            return;
        }

        // Auto-detect format
        var detectedFormat = CrsTransformerHelper.DetectFormat(data);
        if (detectedFormat.HasValue)
        {
            _settingsProvider.SetSetting(_inputFormatSetting, detectedFormat.Value);
            UpdateLanguage(detectedFormat.Value);
        }

        _inputTextArea.Text(data);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private static IUIDropDownListItem[] BuildEpsgDropdownItems()
    {
        return EpsgPresets.All
            .Select(p => Item($"EPSG:{p.Code} - {p.Name}", p.Code))
            .ToArray();
    }

    private void OnSourceEpsgDropdownSelected(IUIDropDownListItem? item)
    {
        if (item?.Value is int epsgCode)
        {
            _settingsProvider.SetSetting(_sourceEpsgSetting, epsgCode);
            _customSourceEpsgInput.Text(string.Empty);
            StartTransform(_inputTextArea.Text);
        }
    }

    private void OnTargetEpsgDropdownSelected(IUIDropDownListItem? item)
    {
        if (item?.Value is int epsgCode)
        {
            _settingsProvider.SetSetting(_targetEpsgSetting, epsgCode);
            _customTargetEpsgInput.Text(string.Empty);
            StartTransform(_inputTextArea.Text);
        }
    }

    private void OnCustomSourceEpsgChanged(string text)
    {
        if (int.TryParse(text, out int epsgCode) && epsgCode > 0)
        {
            _settingsProvider.SetSetting(_sourceEpsgSetting, epsgCode);
            StartTransform(_inputTextArea.Text);
        }
    }

    private void OnCustomTargetEpsgChanged(string text)
    {
        if (int.TryParse(text, out int epsgCode) && epsgCode > 0)
        {
            _settingsProvider.SetSetting(_targetEpsgSetting, epsgCode);
            StartTransform(_inputTextArea.Text);
        }
    }

    private void OnSwapCrs()
    {
        int source = _settingsProvider.GetSetting(_sourceEpsgSetting);
        int target = _settingsProvider.GetSetting(_targetEpsgSetting);

        _settingsProvider.SetSetting(_sourceEpsgSetting, target);
        _settingsProvider.SetSetting(_targetEpsgSetting, source);

        // Update dropdowns
        _sourceEpsgDropdown.Select(GetEpsgDropdownIndex(target));
        _targetEpsgDropdown.Select(GetEpsgDropdownIndex(source));

        // Clear custom inputs
        _customSourceEpsgInput.Text(string.Empty);
        _customTargetEpsgInput.Text(string.Empty);

        // Swap input/output content
        string outputContent = _outputTextArea.Text;
        _inputTextArea.Text(outputContent);
    }

    private void OnInputFormatChanged(CrsInputFormat format)
    {
        UpdateLanguage(format);
        StartTransform(_inputTextArea.Text);
    }

    private void OnIndentationChanged(Indentation indentation)
    {
        StartTransform(_inputTextArea.Text);
    }

    private void OnInputTextChanged(string text)
    {
        StartTransform(text);
    }

    private void StartTransform(string text)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        WorkTask = TransformAsync(
            text,
            _settingsProvider.GetSetting(_inputFormatSetting),
            _settingsProvider.GetSetting(_sourceEpsgSetting),
            _settingsProvider.GetSetting(_targetEpsgSetting),
            _settingsProvider.GetSetting(_indentationSetting),
            _cancellationTokenSource.Token);
    }

    private async Task TransformAsync(
        string input,
        CrsInputFormat inputFormat,
        int sourceEpsg,
        int targetEpsg,
        Indentation indentation,
        CancellationToken cancellationToken)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

            ResultInfo<string> result = await CrsTransformerHelper.TransformAsync(
                input,
                inputFormat,
                sourceEpsg,
                targetEpsg,
                indentation,
                _logger,
                cancellationToken);

            _outputTextArea.Text(result.Data ?? string.Empty);
        }
    }

    private void UpdateLanguage(CrsInputFormat format)
    {
        string language = format switch
        {
            CrsInputFormat.GeoJson => GeoJsonLanguage,
            CrsInputFormat.Wkt => WktLanguage,
            _ => GeoJsonLanguage
        };

        _inputTextArea.Language(language);
        _outputTextArea.Language(language);
    }

    private static int GetEpsgDropdownIndex(int epsgCode)
    {
        for (int i = 0; i < EpsgPresets.All.Length; i++)
        {
            if (EpsgPresets.All[i].Code == epsgCode)
            {
                return i;
            }
        }
        return 0; // Default to first item if not found
    }
}
