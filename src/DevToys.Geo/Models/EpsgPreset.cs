namespace DevToys.Geo.Models;

/// <summary>
/// Represents a commonly used EPSG coordinate reference system.
/// </summary>
internal readonly record struct EpsgPreset(int Code, string Name, string Description)
{
    public override string ToString() => $"EPSG:{Code} - {Name}";
}
