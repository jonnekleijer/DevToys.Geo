using DevToys.Api;
using System.ComponentModel.Composition;
using static DevToys.Api.GUI;

namespace MyExtension;

[Export(typeof(IGuiTool))]
[Name("MyExtension")]                                                         // A unique, internal name of the tool.
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",                                       // This font is available by default in DevToys
    IconGlyph = '\uE670',                                                     // An icon that represents a pizza
    GroupName = PredefinedCommonToolGroupNames.Converters,                    // The group in which the tool will appear in the side bar.
    ResourceManagerAssemblyIdentifier = nameof(MyResourceAssemblyIdentifier), // The Resource Assembly Identifier to use
    ResourceManagerBaseName = "DevToys.Geo.MyExtension",                      // The full name (including namespace) of the resource file containing our localized texts
    ShortDisplayTitleResourceName = nameof(MyExtension.ShortDisplayTitle),    // The name of the resource to use for the short display title
    LongDisplayTitleResourceName = nameof(MyExtension.LongDisplayTitle),
    DescriptionResourceName = nameof(MyExtension.Description),
    AccessibleNameResourceName = nameof(MyExtension.AccessibleName))]
internal sealed class MyExtensionGui : IGuiTool
{
    public UIToolView View => new(Label().Style(UILabelStyle.BodyStrong).Text(MyExtension.HelloWorldLabel));

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        throw new NotImplementedException();
    }
}