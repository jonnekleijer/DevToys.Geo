using DevToys.Api;
using System.ComponentModel.Composition;

namespace DevToys.Geo;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(DevToysGeoResourceAssemblyIdentifier))]
internal sealed class DevToysGeoResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
    {
        return new ValueTask<FontDefinition[]>([]);
    }
}
