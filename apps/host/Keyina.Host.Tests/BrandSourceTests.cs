using System.Globalization;
using System.Xml.Linq;

namespace Keyina.Host.Tests;

internal static class BrandSourceTests
{
    private static readonly string[] ExpectedAssets =
    [
        "keyina-mark.svg",
        "keyina-lockup.svg",
        "keyina-tray-active.svg",
        "keyina-tray-inactive.svg",
        "keyina-tray-listening.svg",
    ];

    [KeyinaTest("brand SVG sources are safe accessible and vector only")]
    private static void SvgSourcesAreSafeAndAccessible()
    {
        foreach (var name in ExpectedAssets)
        {
            var path = Path.Combine(RepositoryPaths.Root, "brand", name);
            AssertEx.True(File.Exists(path), $"Missing SVG source: brand/{name}");

            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            AssertEx.NotNull(root, $"SVG has no root element: {name}");
            AssertEx.Equal("svg", root!.Name.LocalName, $"Unexpected root element in {name}");
            AssertEx.True(!string.IsNullOrWhiteSpace(root.Attribute("viewBox")?.Value),
                $"SVG is missing viewBox: {name}");

            var title = root.Elements().FirstOrDefault(element => element.Name.LocalName == "title");
            AssertEx.True(!string.IsNullOrWhiteSpace(title?.Value),
                $"SVG is missing an accessible title: {name}");

            foreach (var element in root.Descendants())
            {
                AssertEx.False(element.Name.LocalName is "image" or "script" or "filter",
                    $"Forbidden <{element.Name.LocalName}> in {name}");
                foreach (var attribute in element.Attributes())
                {
                    var value = attribute.Value;
                    AssertEx.False(
                        value.Contains("http:", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("https:", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("data:", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("javascript:", StringComparison.OrdinalIgnoreCase),
                        $"External or embedded resource in {name}: {value}");
                }
            }
        }
    }

    [KeyinaTest("tray SVG sources are gradient free and respect the small icon safe area")]
    private static void TraySourcesRespectSafeArea()
    {
        foreach (var name in ExpectedAssets.Where(name => name.Contains("tray", StringComparison.Ordinal)))
        {
            var path = Path.Combine(RepositoryPaths.Root, "brand", name);
            AssertEx.True(File.Exists(path), $"Missing tray SVG source: {name}");
            var document = XDocument.Load(path);
            var root = document.Root!;
            AssertEx.Equal("0 0 16 16", root.Attribute("viewBox")?.Value);
            AssertEx.False(root.Descendants().Any(element => element.Name.LocalName == "linearGradient"),
                $"Tray icon must not use gradients: {name}");

            foreach (var element in root.Descendants().Where(IsGeometryElement))
            {
                ValidateGeometryBounds(element, name);
            }
        }
    }

    private static bool IsGeometryElement(XElement element) =>
        element.Name.LocalName is "line" or "circle" or "rect" or "polyline";

    private static void ValidateGeometryBounds(XElement element, string assetName)
    {
        foreach (var attributeName in new[] { "x", "y", "x1", "x2", "y1", "y2", "cx", "cy" })
        {
            var value = element.Attribute(attributeName)?.Value;
            if (value is not null)
            {
                var number = double.Parse(value, CultureInfo.InvariantCulture);
                AssertEx.True(number >= 3 && number <= 13,
                    $"{assetName} {element.Name.LocalName}.{attributeName}={number} violates 2 px safe area.");
            }
        }

        if (element.Name.LocalName == "circle")
        {
            var cx = Parse(element, "cx");
            var cy = Parse(element, "cy");
            var radius = Parse(element, "r");
            AssertEx.True(cx - radius >= 2 && cx + radius <= 14 &&
                          cy - radius >= 2 && cy + radius <= 14,
                $"Circle in {assetName} violates 2 px safe area.");
        }

        if (element.Name.LocalName == "rect")
        {
            var x = Parse(element, "x");
            var y = Parse(element, "y");
            var width = Parse(element, "width");
            var height = Parse(element, "height");
            AssertEx.True(x >= 2 && y >= 2 && x + width <= 14 && y + height <= 14,
                $"Rectangle in {assetName} violates 2 px safe area.");
        }

        if (element.Name.LocalName == "polyline")
        {
            var values = element.Attribute("points")!.Value
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();
            AssertEx.True(values.Length % 2 == 0, $"Invalid polyline in {assetName}.");
            AssertEx.True(values.All(value => value >= 2 && value <= 14),
                $"Polyline in {assetName} violates 2 px safe area.");
        }
    }

    private static double Parse(XElement element, string attributeName) =>
        double.Parse(element.Attribute(attributeName)!.Value, CultureInfo.InvariantCulture);
}
