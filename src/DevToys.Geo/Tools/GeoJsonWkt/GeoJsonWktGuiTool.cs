using DevToys.Api;
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
    ResourceManagerBaseName = "DevToys.Geo.Tools.GeoJsonWkt.GeoJsonWkt",
    ShortDisplayTitleResourceName = nameof(GeoJsonWkt.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(GeoJsonWkt.LongDisplayTitle),
    DescriptionResourceName = nameof(GeoJsonWkt.Description),
    AccessibleNameResourceName = nameof(GeoJsonWkt.AccessibleName))]
internal sealed class GeoJsonWktGuiTool : IGuiTool
{
    public UIToolView View => new(Label().Style(UILabelStyle.BodyStrong).Text(GeoJsonWkt.HelloWorldLabel));

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        throw new NotImplementedException();
    }
}