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
    private const int MaxDropdownItems = 100; // Limit items shown in dropdown for performance

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

    // All EPSG codes loaded from SRID database
    private readonly List<(int Code, string Name)> _allEpsgCodes;

    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("crs-transformer-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("crs-transformer-output-text-area");
    private readonly IUISingleLineTextInput _sourceSearchInput = SingleLineTextInput("crs-source-search");
    private readonly IUISingleLineTextInput _targetSearchInput = SingleLineTextInput("crs-target-search");
    private readonly IUISelectDropDownList _sourceEpsgDropdown;
    private readonly IUISelectDropDownList _targetEpsgDropdown;

    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public CrsTransformerGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        // Load all EPSG codes from SRID database
        _allEpsgCodes = CrsTransformerHelper.GetAllEpsgCodesWithNames().ToList();

        _sourceEpsgDropdown = SelectDropDownList("crs-source-epsg-dropdown");
        _targetEpsgDropdown = SelectDropDownList("crs-target-epsg-dropdown");

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
                                    _sourceSearchInput
                                        .HideCommandBar()
                                        .OnTextChanged(OnSourceSearchChanged),
                                    _sourceEpsgDropdown
                                        .WithItems(BuildFilteredEpsgDropdownItems(string.Empty, _settingsProvider.GetSetting(_sourceEpsgSetting)))
                                        .OnItemSelected(OnSourceEpsgDropdownSelected)
                                )
                            ),

                        // Target CRS setting
                        Setting("crs-target-setting")
                            .Icon("FluentSystemIcons", '\uF4A1')
                            .Title(CrsTransformer.TargetCrs)
                            .Description(CrsTransformer.TargetCrsDescription)
                            .InteractiveElement(
                                Stack().Horizontal().SmallSpacing().WithChildren(
                                    _targetSearchInput
                                        .HideCommandBar()
                                        .OnTextChanged(OnTargetSearchChanged),
                                    _targetEpsgDropdown
                                        .WithItems(BuildFilteredEpsgDropdownItems(string.Empty, _settingsProvider.GetSetting(_targetEpsgSetting)))
                                        .OnItemSelected(OnTargetEpsgDropdownSelected),
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

    private IUIDropDownListItem[] BuildFilteredEpsgDropdownItems(string filter, int? selectedCode = null)
    {
        var items = new List<IUIDropDownListItem>();

        // If there's a selected code, always include it first
        if (selectedCode.HasValue)
        {
            var selected = _allEpsgCodes.FirstOrDefault(e => e.Code == selectedCode.Value);
            if (selected.Code != 0)
            {
                items.Add(Item($"EPSG:{selected.Code} - {selected.Name}", selected.Code));
            }
        }

        // Filter and add remaining items
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _allEpsgCodes.Take(MaxDropdownItems)
            : _allEpsgCodes
                .Where(e => e.Code.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(MaxDropdownItems);

        foreach (var epsg in filtered)
        {
            // Skip if already added as selected
            if (selectedCode.HasValue && epsg.Code == selectedCode.Value)
            {
                continue;
            }
            items.Add(Item($"EPSG:{epsg.Code} - {epsg.Name}", epsg.Code));
        }

        return items.ToArray();
    }

    private void OnSourceSearchChanged(string text)
    {
        // Only filter when at least 2 characters are typed
        if (text.Length >= 2)
        {
            var items = BuildFilteredEpsgDropdownItems(text, _settingsProvider.GetSetting(_sourceEpsgSetting));
            _sourceEpsgDropdown.WithItems(items);

            // If search text is a valid EPSG code, use it directly
            if (int.TryParse(text, out int epsgCode) && CrsTransformerHelper.IsValidEpsgCode(epsgCode))
            {
                _settingsProvider.SetSetting(_sourceEpsgSetting, epsgCode);
                _sourceEpsgDropdown.Select(0); // Select first item which should be the matching code
                StartTransform(_inputTextArea.Text);
            }
        }
        else if (string.IsNullOrEmpty(text))
        {
            // Reset to show current selection when search is cleared
            var items = BuildFilteredEpsgDropdownItems(string.Empty, _settingsProvider.GetSetting(_sourceEpsgSetting));
            _sourceEpsgDropdown.WithItems(items);
        }
    }

    private void OnTargetSearchChanged(string text)
    {
        // Only filter when at least 2 characters are typed
        if (text.Length >= 2)
        {
            var items = BuildFilteredEpsgDropdownItems(text, _settingsProvider.GetSetting(_targetEpsgSetting));
            _targetEpsgDropdown.WithItems(items);

            // If search text is a valid EPSG code, use it directly
            if (int.TryParse(text, out int epsgCode) && CrsTransformerHelper.IsValidEpsgCode(epsgCode))
            {
                _settingsProvider.SetSetting(_targetEpsgSetting, epsgCode);
                _targetEpsgDropdown.Select(0); // Select first item which should be the matching code
                StartTransform(_inputTextArea.Text);
            }
        }
        else if (string.IsNullOrEmpty(text))
        {
            // Reset to show current selection when search is cleared
            var items = BuildFilteredEpsgDropdownItems(string.Empty, _settingsProvider.GetSetting(_targetEpsgSetting));
            _targetEpsgDropdown.WithItems(items);
        }
    }

    private void OnSourceEpsgDropdownSelected(IUIDropDownListItem? item)
    {
        if (item?.Value is int epsgCode)
        {
            _settingsProvider.SetSetting(_sourceEpsgSetting, epsgCode);
            StartTransform(_inputTextArea.Text);
        }
    }

    private void OnTargetEpsgDropdownSelected(IUIDropDownListItem? item)
    {
        if (item?.Value is int epsgCode)
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

        // Update dropdowns with new items (including the swapped selections)
        _sourceEpsgDropdown.WithItems(BuildFilteredEpsgDropdownItems(_sourceSearchInput.Text, target));
        _targetEpsgDropdown.WithItems(BuildFilteredEpsgDropdownItems(_targetSearchInput.Text, source));
        _sourceEpsgDropdown.Select(0);
        _targetEpsgDropdown.Select(0);

        // Clear search inputs
        _sourceSearchInput.Text(string.Empty);
        _targetSearchInput.Text(string.Empty);

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
}
