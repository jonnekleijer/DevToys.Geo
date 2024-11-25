using System.Globalization;
using DevToys.Api;
using FluentAssertions;
using DevToys.Geo.Tools.GeoJsonWkt;
using DevToys.Geo.UnitTests.Mocks;


namespace DevToys.Geo.UnitTests.Tools;

public class JsonYamlConverterGuiToolTests : TestBase
{
    private readonly UIToolView _toolView;
    private readonly GeoJsonWktConverterGuiTool _tool;
    private readonly IUIMultiLineTextInput _inputTextArea;
    private readonly IUIMultiLineTextInput _outputTextArea;

    public JsonYamlConverterGuiToolTests()
    {
        _tool = new GeoJsonWktConverterGuiTool(new MockISettingsProvider());
        _toolView = _tool.View;
        _inputTextArea = _toolView.GetChildElementById("geojson-to-wkt-input-text-area") as IUIMultiLineTextInput 
            ?? throw new InvalidOperationException("Input text area not found.");
        _outputTextArea = _toolView.GetChildElementById("geojson-to-wkt-output-text-area") as IUIMultiLineTextInput 
            ?? throw new InvalidOperationException("Output text area not found.");

        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    // Data from https://en.wikipedia.org/wiki/GeoJSON and https://en.wikipedia.org/wiki/Well-known_text_representation_of_geometry
    [Theory(DisplayName = "Convert GeoJSON with valid GeoJSON should return valid WKT")]
    [InlineData("{\"type\": \"Point\", \"coordinates\": [30.0, 10.0]}", "POINT (30 10)")]
    [InlineData("{\"type\": \"LineString\", \"coordinates\": [[30.0, 10.0], [10.0, 30.0],[40.0, 40.0]]}", "LINESTRING (30 10, 10 30, 40 40)")]
    [InlineData("{\"type\": \"Polygon\", \"coordinates\": [[[30.0, 10.0], [40.0, 40.0], [20.0, 40.0], [10.0, 20.0], [30.0, 10.0]]]}", "POLYGON ((30 10, 40 40, 20 40, 10 20, 30 10))")]
    [InlineData("{\"type\": \"Polygon\", \"coordinates\": [[[35.0, 10.0], [45.0, 45.0], [15.0, 40.0], [10.0, 20.0], [35.0, 10.0]], [[20.0, 30.0], [35.0, 35.0], [30.0, 20.0], [20.0, 30.0]]]}", "POLYGON ((35 10, 45 45, 15 40, 10 20, 35 10), (20 30, 35 35, 30 20, 20 30))")]
    [InlineData("{\"type\": \"MultiPoint\", \"coordinates\": [[10.0, 40.0], [40.0, 30.0], [20.0, 20.0], [30.0, 10.0]]}", "MULTIPOINT ((10 40), (40 30), (20 20), (30 10))")]
    [InlineData("{\"type\": \"MultiLineString\", \"coordinates\": [[[10.0, 10.0], [20.0, 20.0], [10.0, 40.0]], [[40.0, 40.0], [30.0, 30.0], [40.0, 20.0], [30.0, 10.0]]]}", "MULTILINESTRING ((10 10, 20 20, 10 40), (40 40, 30 30, 40 20, 30 10))")]
    [InlineData("{\"type\": \"MultiPolygon\", \"coordinates\": [[[[30.0, 20.0], [45.0, 40.0], [10.0, 40.0], [30.0, 20.0]]], [[[15.0, 5.0], [40.0, 10.0], [10.0, 20.0], [5.0, 10.0], [15.0, 5.0]]]]}", "MULTIPOLYGON (((30 20, 45 40, 10 40, 30 20)), ((15 5, 40 10, 10 20, 5 10, 15 5)))")]
    [InlineData("{\"type\": \"MultiPolygon\", \"coordinates\": [[[[40.0, 40.0], [20.0, 45.0], [45.0, 30.0], [40.0, 40.0]]], [[[20.0, 35.0], [10.0, 30.0], [10.0, 10.0], [30.0, 5.0], [45.0, 20.0], [20.0, 35.0]], [[30.0, 20.0], [20.0, 15.0], [20.0, 25.0], [30.0, 20.0]]]]}", "MULTIPOLYGON (((40 40, 20 45, 45 30, 40 40)), ((20 35, 10 30, 10 10, 30 5, 45 20, 20 35), (30 20, 20 15, 20 25, 30 20)))")]
    [InlineData("{\"type\": \"GeometryCollection\", \"geometries\": [{\"type\": \"Point\", \"coordinates\": [40.0, 10.0]}, {\"type\": \"LineString\", \"coordinates\": [[10.0, 10.0], [20.0, 20.0], [10.0, 40.0]]}, {\"type\": \"Polygon\", \"coordinates\": [[[40.0, 40.0], [20.0, 45.0], [45.0, 30.0], [40.0, 40.0]]]}]}", "GEOMETRYCOLLECTION (POINT (40 10), LINESTRING (10 10, 20 20, 10 40), POLYGON ((40 40, 20 45, 45 30, 40 40)))")]
    public async Task ConvertGeoJsonWithValidGeoJsonShouldReturnValidYaml(string input, string expectedOutput)
    {
        _inputTextArea.Text(input);
        await _tool.WorkTask!;
        _outputTextArea.Text.Should().Be(expectedOutput);
    }

    // Data from https://en.wikipedia.org/wiki/GeoJSON and https://en.wikipedia.org/wiki/Well-known_text_representation_of_geometry
    [Theory(DisplayName = "Convert Wkt with valid Wkt should return valid GeoJson")]
    [InlineData("POINT (30 10)", "{\"type\":\"Point\",\"coordinates\":[30.0,10.0]}")]
    [InlineData("LINESTRING (30 10, 10 30, 40 40)", "{\"type\":\"LineString\",\"coordinates\":[[30.0,10.0],[10.0,30.0],[40.0,40.0]]}")]
    [InlineData("POLYGON ((30 10, 40 40, 20 40, 10 20, 30 10))", "{\"type\":\"Polygon\",\"coordinates\":[[[30.0,10.0],[40.0,40.0],[20.0,40.0],[10.0,20.0],[30.0,10.0]]]}")]
    [InlineData("POLYGON ((35 10, 45 45, 15 40, 10 20, 35 10), (20 30, 35 35, 30 20, 20 30))", "{\"type\": \"Polygon\", \"coordinates\": [[[35.0, 10.0], [45.0, 45.0], [15.0, 40.0], [10.0, 20.0], [35.0, 10.0]], [[20.0, 30.0], [35.0, 35.0], [30.0, 20.0], [20.0, 30.0]]]}")]
    [InlineData("MULTIPOINT ((10 40), (40 30), (20 20), (30 10))", "{\"type\": \"MultiPoint\", \"coordinates\": [[10.0, 40.0], [40.0, 30.0], [20.0, 20.0], [30.0, 10.0]]}")]
    [InlineData("MULTILINESTRING ((10 10, 20 20, 10 40), (40 40, 30 30, 40 20, 30 10))", "{\"type\": \"MultiLineString\", \"coordinates\": [[[10.0, 10.0], [20.0, 20.0], [10.0, 40.0]], [[40.0, 40.0], [30.0, 30.0], [40.0, 20.0], [30.0, 10.0]]]}")]
    [InlineData("MULTIPOLYGON (((30 20, 45 40, 10 40, 30 20)), ((15 5, 40 10, 10 20, 5 10, 15 5)))", "{\"type\": \"MultiPolygon\", \"coordinates\": [[[[30.0, 20.0], [45.0, 40.0], [10.0, 40.0], [30.0, 20.0]]], [[[15.0, 5.0], [40.0, 10.0], [10.0, 20.0], [5.0, 10.0], [15.0, 5.0]]]]}")]
    [InlineData("MULTIPOLYGON (((40 40, 20 45, 45 30, 40 40)), ((20 35, 10 30, 10 10, 30 5, 45 20, 20 35), (30 20, 20 15, 20 25, 30 20)))", "{\"type\": \"MultiPolygon\", \"coordinates\": [[[[40.0, 40.0], [20.0, 45.0], [45.0, 30.0], [40.0, 40.0]]], [[[20.0, 35.0], [10.0, 30.0], [10.0, 10.0], [30.0, 5.0], [45.0, 20.0], [20.0, 35.0]], [[30.0, 20.0], [20.0, 15.0], [20.0, 25.0], [30.0, 20.0]]]]}")]
    [InlineData("GEOMETRYCOLLECTION (POINT (40 10), LINESTRING (10 10, 20 20, 10 40), POLYGON ((40 40, 20 45, 45 30, 40 40)))", "{\"type\": \"GeometryCollection\", \"geometries\": [{\"type\": \"Point\", \"coordinates\": [40.0, 10.0]}, {\"type\": \"LineString\", \"coordinates\": [[10.0, 10.0], [20.0, 20.0], [10.0, 40.0]]}, {\"type\": \"Polygon\", \"coordinates\": [[[40.0, 40.0], [20.0, 45.0], [45.0, 30.0], [40.0, 40.0]]]}]}")]
    public async Task ConvertWktWithValidWktShouldReturnValidGeoJson(string input, string expectedOutput)
    {
        var conversionSettings = (IUISelectDropDownList)((IUISetting)_toolView.GetChildElementById("geojson-to-wkt-text-conversion-setting")).InteractiveElement
            ?? throw new InvalidOperationException("Dropdown list for conversion not found.");
        conversionSettings.Select(1); // Select WktToGeoJson

        _inputTextArea.Text(input);
        await _tool.WorkTask!;
        _outputTextArea.Text.Should().Be(expectedOutput);
    }
}
