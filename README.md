# DevToys Geo

A set of Geo Extensions for [DevToys](https://devtoys.app/).
Currently, the following tools are available:
* **GeoJSON <> WKT**: Convert GeoJSON to WKT and vice versa.
* **Coordinate Converter**: Convert coordinates between Decimal Degrees (DD), Degrees Minutes Seconds (DMS), and Degrees Decimal Minutes (DDM) formats.
* **CRS Transformer**: Transform geometries between 8,000+ coordinate reference systems (EPSG codes). Supports GeoJSON and WKT input formats with searchable EPSG selection.

## License

This extension is licensed under the MIT License - see the [LICENSE](https://github.com/jonnekleijer/DevToys.Geo/blob/main/LICENSE) file for details.

## Installation

1. Download the `DevToys.Geo` NuGet package from [NuGet.org](https://www.nuget.org/packages/jonnekleijer/DevToys.Geo/).
2. Install the extension using one of these methods:
   - **Via DevToys UI**: Open DevToys, go to `Manage extensions`, click on `Install an extension` and select the downloaded NuGet package.
   - **Manual installation**: Extract the `.nupkg` file (it's a ZIP archive) to the plugins folder:
     - Windows: `%LocalAppData%/DevToys/Plugins/`
     - macOS: `~/Library/com.devtoys/Plugins/`
     - Linux: `~/.local/share/devtoys/Plugins/`