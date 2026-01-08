using System.Globalization;
using DevToys.Api;
using DevToys.Geo.Tools.CrsTransformer;
using DevToys.Geo.UnitTests.Mocks;
using FluentAssertions;

namespace DevToys.Geo.UnitTests.Tools;

public class CrsTransformerGuiToolTests : TestBase
{
    private readonly CrsTransformerGuiTool _tool;
    private readonly IUIMultiLineTextInput _inputTextArea;
    private readonly IUIMultiLineTextInput _outputTextArea;

    public CrsTransformerGuiToolTests()
    {
        _tool = new CrsTransformerGuiTool(new MockISettingsProvider());
        _inputTextArea = _tool.View.GetChildElementById("crs-transformer-input-text-area") as IUIMultiLineTextInput
            ?? throw new InvalidOperationException("Input text area not found.");
        _outputTextArea = _tool.View.GetChildElementById("crs-transformer-output-text-area") as IUIMultiLineTextInput
            ?? throw new InvalidOperationException("Output text area not found.");

        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact(DisplayName = "Tool view is created successfully")]
    public void ToolViewIsCreatedSuccessfully()
    {
        _tool.View.Should().NotBeNull();
        _inputTextArea.Should().NotBeNull();
        _outputTextArea.Should().NotBeNull();
    }

    [Fact(DisplayName = "Transform GeoJSON Point from WGS84 to Web Mercator")]
    public async Task TransformGeoJsonPointWgs84ToWebMercator()
    {
        // Default settings: WGS84 (4326) -> Web Mercator (3857)
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9041, 52.3676]}";
        _inputTextArea.Text(input);

        await _tool.WorkTask!;

        _outputTextArea.Text.Should().Contain("\"type\": \"Point\"");
        // Web Mercator X coordinate should be around 546000
        _outputTextArea.Text.Should().MatchRegex(@"54\d{4}");
    }

    [Fact(DisplayName = "Transform WKT Point from WGS84 to Web Mercator")]
    public async Task TransformWktPointWgs84ToWebMercator()
    {
        // Need to set format to WKT first
        var formatSetting = (IUISelectDropDownList?)((IUISetting?)_tool.View.GetChildElementById("crs-input-format-setting"))?.InteractiveElement;
        formatSetting?.Select(1); // Select WKT

        string input = "POINT (4.9041 52.3676)";
        _inputTextArea.Text(input);

        await _tool.WorkTask!;

        _outputTextArea.Text.Should().StartWith("POINT");
    }

    [Fact(DisplayName = "Empty input produces empty output")]
    public async Task EmptyInputProducesEmptyOutput()
    {
        _inputTextArea.Text("");

        await _tool.WorkTask!;

        _outputTextArea.Text.Should().BeEmpty();
    }

    [Theory(DisplayName = "OnDataReceived sets input text correctly")]
    [InlineData("{\"type\": \"Point\", \"coordinates\": [0, 0]}")]
    [InlineData("POINT (0 0)")]
    public void OnDataReceivedSetsInputTextCorrectly(string data)
    {
        _tool.OnDataReceived("text", data);

        _inputTextArea.Text.Should().Be(data);
    }

    [Fact(DisplayName = "Tool can be disposed without errors")]
    public void ToolCanBeDisposedWithoutErrors()
    {
        var action = () => _tool.Dispose();
        action.Should().NotThrow();
    }
}
