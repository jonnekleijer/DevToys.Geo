using DevToys.Api;
using System.ComponentModel.Composition;

namespace DevToys.Geo;

[Export(typeof(GuiToolGroup))]
[Name("Geo")]
[Order(After = PredefinedCommonToolGroupNames.Converters)]
internal class GeoToolGroup : GuiToolGroup
{
    [ImportingConstructor]
    internal GeoToolGroup()
    {
        IconFontName = "FluentSystemIcons";
        IconGlyph = '\uEEF2';
        DisplayTitle = DevToysGeo.GroupDisplayTitle;
        AccessibleName = DevToysGeo.GroupAccessibleName;
    }
}