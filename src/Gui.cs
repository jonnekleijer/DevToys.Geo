using DevToys.Api;
using System.ComponentModel.Composition;
using static DevToys.Api.GUI;

namespace DevToys.Geo;

[Export(typeof(IGuiTool))]
[Name("DevToys.Geo")]                                                         // A unique, internal name of the tool.
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",                                       // This font is available by default in DevToys
    IconGlyph = '\uEEF2',                                                     // An icon that represents a pizza
    GroupName = PredefinedCommonToolGroupNames.Converters,                    // The group in which the tool will appear in the side bar.
    ResourceManagerAssemblyIdentifier = nameof(DevToysGeoResourceAssemblyIdentifier), // The Resource Assembly Identifier to use
    ResourceManagerBaseName = "DevToys.Geo.DevToysGeo",                      // The full name (including namespace) of the resource file containing our localized texts
    ShortDisplayTitleResourceName = nameof(DevToysGeo.ShortDisplayTitle),    // The name of the resource to use for the short display title
    LongDisplayTitleResourceName = nameof(DevToysGeo.LongDisplayTitle),
    DescriptionResourceName = nameof(DevToysGeo.Description),
    AccessibleNameResourceName = nameof(DevToysGeo.AccessibleName))]
internal sealed class Gui : IGuiTool
{
    public UIToolView View => new(Label().Style(UILabelStyle.BodyStrong).Text(DevToysGeo.HelloWorldLabel));

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        throw new NotImplementedException();
    }
}