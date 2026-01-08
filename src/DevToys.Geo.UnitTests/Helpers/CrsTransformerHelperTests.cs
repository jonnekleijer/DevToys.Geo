using System.Globalization;
using DevToys.Geo.Helpers;
using DevToys.Geo.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevToys.Geo.UnitTests.Helpers;

public class CrsTransformerHelperTests : TestBase
{
    private readonly ILogger _logger = NullLogger.Instance;

    #region Format Detection Tests

    [Theory(DisplayName = "Detect GeoJSON format correctly")]
    [InlineData("{\"type\": \"Point\", \"coordinates\": [0, 0]}")]
    [InlineData("{\"type\": \"LineString\", \"coordinates\": [[0, 0], [1, 1]]}")]
    [InlineData("{\"type\": \"Polygon\", \"coordinates\": [[[0, 0], [1, 0], [1, 1], [0, 0]]]}")]
    [InlineData("{\"type\": \"FeatureCollection\", \"features\": []}")]
    [InlineData("{\"type\": \"Feature\", \"geometry\": null}")]
    public void DetectGeoJsonFormatCorrectly(string input)
    {
        var result = CrsTransformerHelper.DetectFormat(input);
        result.Should().Be(CrsInputFormat.GeoJson);
    }

    [Theory(DisplayName = "Detect WKT format correctly")]
    [InlineData("POINT (0 0)")]
    [InlineData("LINESTRING (0 0, 1 1, 2 2)")]
    [InlineData("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))")]
    [InlineData("MULTIPOINT ((0 0), (1 1))")]
    [InlineData("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [InlineData("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)))")]
    [InlineData("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (0 0, 1 1))")]
    public void DetectWktFormatCorrectly(string input)
    {
        var result = CrsTransformerHelper.DetectFormat(input);
        result.Should().Be(CrsInputFormat.Wkt);
    }

    [Theory(DisplayName = "Invalid input returns null format")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("random text")]
    [InlineData("12345")]
    [InlineData("not a geometry")]
    public void InvalidInputReturnsNullFormat(string? input)
    {
        var result = CrsTransformerHelper.DetectFormat(input);
        result.Should().BeNull();
    }

    #endregion

    #region EPSG Validation Tests

    [Theory(DisplayName = "Supported EPSG codes are valid")]
    [InlineData(4326)]  // WGS 84
    [InlineData(3857)]  // Web Mercator
    [InlineData(28992)] // RD New (Netherlands)
    [InlineData(2154)]  // Lambert 93 (France)
    [InlineData(27700)] // British National Grid
    [InlineData(32632)] // UTM 32N
    public void SupportedEpsgCodesAreValid(int epsgCode)
    {
        var result = CrsTransformerHelper.IsValidEpsgCode(epsgCode);
        result.Should().BeTrue();
    }

    [Theory(DisplayName = "Unsupported EPSG codes are invalid")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999999)]
    public void UnsupportedEpsgCodesAreInvalid(int epsgCode)
    {
        var result = CrsTransformerHelper.IsValidEpsgCode(epsgCode);
        result.Should().BeFalse();
    }

    #endregion

    #region SRID Database Tests

    [Fact(DisplayName = "SRID database contains thousands of coordinate systems")]
    public void SridDatabaseContainsThousandsOfCoordinateSystems()
    {
        var count = CrsTransformerHelper.GetSupportedEpsgCount();
        count.Should().BeGreaterThan(5000, "SRID database should contain thousands of EPSG codes");
    }

    [Fact(DisplayName = "SRID database contains all common EPSG codes")]
    public void SridDatabaseContainsAllCommonEpsgCodes()
    {
        var codes = CrsTransformerHelper.GetSupportedEpsgCodes();

        // Common geographic CRS
        codes.Should().Contain(4326, "WGS 84");
        codes.Should().Contain(4269, "NAD83");
        codes.Should().Contain(4258, "ETRS89");

        // Common projected CRS
        codes.Should().Contain(3857, "Web Mercator");
        codes.Should().Contain(28992, "RD New (Netherlands)");
        codes.Should().Contain(2154, "Lambert 93 (France)");
        codes.Should().Contain(27700, "British National Grid");
        codes.Should().Contain(3035, "ETRS89-LAEA Europe");

        // UTM zones
        codes.Should().Contain(32601, "WGS 84 / UTM zone 1N");
        codes.Should().Contain(32660, "WGS 84 / UTM zone 60N");
        codes.Should().Contain(32701, "WGS 84 / UTM zone 1S");
        codes.Should().Contain(32760, "WGS 84 / UTM zone 60S");
    }

    [Theory(DisplayName = "Additional EPSG codes from SRID database are valid")]
    [InlineData(2000)]   // Anguilla 1957 / British West Indies Grid
    [InlineData(2193)]   // NZGD2000 / New Zealand Transverse Mercator
    [InlineData(3006)]   // SWEREF99 TM (Sweden)
    [InlineData(3395)]   // WGS 84 / World Mercator
    [InlineData(4167)]   // NZGD2000
    [InlineData(4230)]   // ED50
    [InlineData(5514)]   // S-JTSK / Krovak East North (Czech Republic)
    [InlineData(6668)]   // JGD2011 (Japan)
    [InlineData(26918)]  // NAD83 / UTM zone 18N
    public void AdditionalEpsgCodesFromSridDatabaseAreValid(int epsgCode)
    {
        var result = CrsTransformerHelper.IsValidEpsgCode(epsgCode);
        result.Should().BeTrue($"EPSG:{epsgCode} should be supported from the SRID database");
    }

    #endregion

    #region GeoJSON Transformation Tests

    [Fact(DisplayName = "Transform GeoJSON Point from WGS84 to Web Mercator")]
    public async Task TransformGeoJsonPointWgs84ToWebMercator()
    {
        // Amsterdam: 4.9041, 52.3676 (WGS84) -> approximately 546000, 6860000 (Web Mercator)
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9041, 52.3676]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("\"type\": \"Point\"");
        // Web Mercator X should be around 546000
        result.Data.Should().MatchRegex(@"54\d{4}");
    }

    [Fact(DisplayName = "Transform GeoJSON Point from WGS84 to RD New (Netherlands)")]
    public async Task TransformGeoJsonPointWgs84ToRdNew()
    {
        // Amsterdam: 4.9041, 52.3676 (WGS84) -> approximately 121000, 487000 (RD New)
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9041, 52.3676]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            28992,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("\"type\": \"Point\"");
        // RD New X should be around 121000
        result.Data.Should().MatchRegex(@"12\d{4}");
    }

    [Fact(DisplayName = "Transform GeoJSON LineString")]
    public async Task TransformGeoJsonLineString()
    {
        string input = "{\"type\": \"LineString\", \"coordinates\": [[4.9, 52.3], [5.0, 52.4]]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("\"type\": \"LineString\"");
    }

    [Fact(DisplayName = "Transform GeoJSON Polygon")]
    public async Task TransformGeoJsonPolygon()
    {
        string input = "{\"type\": \"Polygon\", \"coordinates\": [[[4.9, 52.3], [5.0, 52.3], [5.0, 52.4], [4.9, 52.4], [4.9, 52.3]]]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("\"type\": \"Polygon\"");
    }

    [Fact(DisplayName = "Transform GeoJSON Feature")]
    public async Task TransformGeoJsonFeature()
    {
        string input = "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Point\", \"coordinates\": [4.9, 52.3]}, \"properties\": {\"name\": \"Test\"}}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("\"type\": \"Feature\"");
        result.Data.Should().Contain("\"type\": \"Point\"");
    }

    [Fact(DisplayName = "Transform GeoJSON FeatureCollection")]
    public async Task TransformGeoJsonFeatureCollection()
    {
        string input = "{\"type\": \"FeatureCollection\", \"features\": [{\"type\": \"Feature\", \"geometry\": {\"type\": \"Point\", \"coordinates\": [4.9, 52.3]}, \"properties\": {}}]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("\"type\": \"FeatureCollection\"");
    }

    #endregion

    #region WKT Transformation Tests

    [Fact(DisplayName = "Transform WKT Point from WGS84 to Web Mercator")]
    public async Task TransformWktPointWgs84ToWebMercator()
    {
        string input = "POINT (4.9041 52.3676)";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().StartWith("POINT");
        // Web Mercator coordinates should be large numbers
        result.Data.Should().MatchRegex(@"POINT \(54\d+");
    }

    [Fact(DisplayName = "Transform WKT LineString")]
    public async Task TransformWktLineString()
    {
        string input = "LINESTRING (4.9 52.3, 5.0 52.4)";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().StartWith("LINESTRING");
    }

    [Fact(DisplayName = "Transform WKT Polygon")]
    public async Task TransformWktPolygon()
    {
        string input = "POLYGON ((4.9 52.3, 5.0 52.3, 5.0 52.4, 4.9 52.4, 4.9 52.3))";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().StartWith("POLYGON");
    }

    [Fact(DisplayName = "Batch WKT transformation processes multiple lines")]
    public async Task BatchWktTransformationProcessesMultipleLines()
    {
        string input = "POINT (4.9 52.3)\nPOINT (5.0 52.4)\nPOINT (5.1 52.5)";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        var lines = result.Data!.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
        lines.Should().AllSatisfy(line => line.Should().StartWith("POINT"));
    }

    #endregion

    #region Edge Cases Tests

    [Fact(DisplayName = "Same source and target EPSG returns input unchanged")]
    public async Task SameEpsgReturnsInputUnchanged()
    {
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9041, 52.3676]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            4326, // Same as source
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Be(input);
    }

    [Fact(DisplayName = "Empty input returns empty result")]
    public async Task EmptyInputReturnsEmptyResult()
    {
        var result = await CrsTransformerHelper.TransformAsync(
            "",
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeFalse();
        result.Data.Should().BeEmpty();
    }

    [Fact(DisplayName = "Whitespace input returns empty result")]
    public async Task WhitespaceInputReturnsEmptyResult()
    {
        var result = await CrsTransformerHelper.TransformAsync(
            "   ",
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeFalse();
        result.Data.Should().BeEmpty();
    }

    [Fact(DisplayName = "Invalid GeoJSON returns error")]
    public async Task InvalidGeoJsonReturnsError()
    {
        string input = "{\"invalid\": \"json\"}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeFalse();
    }

    [Fact(DisplayName = "Unsupported EPSG code returns error")]
    public async Task UnsupportedEpsgCodeReturnsError()
    {
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9, 52.3]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            99999, // Unsupported EPSG
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeFalse();
        result.Data.Should().Contain("not supported");
    }

    #endregion

    #region Indentation Tests

    [Fact(DisplayName = "GeoJSON output respects two spaces indentation")]
    public async Task GeoJsonOutputRespectsTwoSpacesIndentation()
    {
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9, 52.3]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("  \"type\"");
    }

    [Fact(DisplayName = "GeoJSON output respects four spaces indentation")]
    public async Task GeoJsonOutputRespectsFourSpacesIndentation()
    {
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9, 52.3]}";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.FourSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().Contain("    \"type\"");
    }

    #endregion

    #region Round-trip Tests

    [Fact(DisplayName = "Round-trip transformation returns similar coordinates")]
    public async Task RoundTripTransformationReturnsSimilarCoordinates()
    {
        string input = "{\"type\": \"Point\", \"coordinates\": [4.9041, 52.3676]}";

        // Transform WGS84 -> Web Mercator
        var toMercator = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.GeoJson,
            4326,
            3857,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        toMercator.HasSucceeded.Should().BeTrue();

        // Transform Web Mercator -> WGS84
        var backToWgs84 = await CrsTransformerHelper.TransformAsync(
            toMercator.Data!,
            CrsInputFormat.GeoJson,
            3857,
            4326,
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        backToWgs84.HasSucceeded.Should().BeTrue();
        // Should contain coordinates close to original (4.9041, 52.3676)
        backToWgs84.Data.Should().MatchRegex(@"4\.90\d+");
        backToWgs84.Data.Should().MatchRegex(@"52\.36\d+");
    }

    #endregion

    #region Additional EPSG Transformation Tests

    [Fact(DisplayName = "Transform from WGS84 to NAD83 UTM zone 18N")]
    public async Task TransformWgs84ToNad83Utm18N()
    {
        // New York City area: -74.0, 40.7 (WGS84)
        string input = "POINT (-74.0 40.7)";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            26918, // NAD83 / UTM zone 18N
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().StartWith("POINT");
    }

    [Fact(DisplayName = "Transform from WGS84 to New Zealand Transverse Mercator")]
    public async Task TransformWgs84ToNztm()
    {
        // Wellington: 174.78, -41.29 (WGS84)
        string input = "POINT (174.78 -41.29)";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            2193, // NZGD2000 / New Zealand Transverse Mercator
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().StartWith("POINT");
    }

    [Fact(DisplayName = "Transform from WGS84 to Swedish grid (SWEREF99 TM)")]
    public async Task TransformWgs84ToSweref99Tm()
    {
        // Stockholm: 18.07, 59.33 (WGS84)
        string input = "POINT (18.07 59.33)";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            3006, // SWEREF99 TM
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().StartWith("POINT");
    }

    [Fact(DisplayName = "Transform from WGS84 to World Mercator")]
    public async Task TransformWgs84ToWorldMercator()
    {
        // London: -0.12, 51.51 (WGS84)
        string input = "POINT (-0.12 51.51)";

        var result = await CrsTransformerHelper.TransformAsync(
            input,
            CrsInputFormat.Wkt,
            4326,
            3395, // WGS 84 / World Mercator
            Indentation.TwoSpaces,
            _logger,
            CancellationToken.None);

        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().StartWith("POINT");
    }

    [Fact(DisplayName = "Transform covers all UTM zones from Northern hemisphere")]
    public async Task TransformCoversAllUtmZonesNorth()
    {
        // Test a few UTM zones from Northern hemisphere
        var utmZones = new[] { 32601, 32610, 32620, 32630, 32640, 32650, 32660 };
        string input = "POINT (0 45)";

        foreach (var zone in utmZones)
        {
            var result = await CrsTransformerHelper.TransformAsync(
                input,
                CrsInputFormat.Wkt,
                4326,
                zone,
                Indentation.TwoSpaces,
                _logger,
                CancellationToken.None);

            result.HasSucceeded.Should().BeTrue($"UTM zone {zone} should be supported");
        }
    }

    [Fact(DisplayName = "Transform covers all UTM zones from Southern hemisphere")]
    public async Task TransformCoversAllUtmZonesSouth()
    {
        // Test a few UTM zones from Southern hemisphere
        var utmZones = new[] { 32701, 32710, 32720, 32730, 32740, 32750, 32760 };
        string input = "POINT (0 -45)";

        foreach (var zone in utmZones)
        {
            var result = await CrsTransformerHelper.TransformAsync(
                input,
                CrsInputFormat.Wkt,
                4326,
                zone,
                Indentation.TwoSpaces,
                _logger,
                CancellationToken.None);

            result.HasSucceeded.Should().BeTrue($"UTM zone {zone} should be supported");
        }
    }

    #endregion
}
