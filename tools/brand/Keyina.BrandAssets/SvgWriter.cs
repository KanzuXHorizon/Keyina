using System.Globalization;
using System.Text;
using System.Xml;

namespace Keyina.BrandAssets;

internal static class SvgWriter
{
    public static void GenerateAll(string repositoryRoot)
    {
        var brandDirectory = Path.Combine(repositoryRoot, "brand");
        Directory.CreateDirectory(brandDirectory);

        foreach (var asset in BrandGeometry.CreateAll())
        {
            var output = Path.Combine(brandDirectory, asset.FileName);
            AtomicWrite(output, Render(asset));
        }
    }

    private static byte[] Render(BrandAssetDefinition asset)
    {
        var builder = new StringBuilder(8_192);
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using (var writer = XmlWriter.Create(builder, settings))
        {
            writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
            writer.WriteAttributeString("viewBox", $"0 0 {Number(asset.Width)} {Number(asset.Height)}");
            writer.WriteAttributeString("role", "img");
            writer.WriteAttributeString("aria-labelledby", "title");
            writer.WriteAttributeString("shape-rendering", "geometricPrecision");
            writer.WriteStartElement("title");
            writer.WriteAttributeString("id", "title");
            writer.WriteString(asset.Title);
            writer.WriteEndElement();

            if (asset.Gradients.Count > 0)
            {
                writer.WriteStartElement("defs");
                foreach (var gradient in asset.Gradients)
                {
                    writer.WriteStartElement("linearGradient");
                    writer.WriteAttributeString("id", gradient.Id);
                    writer.WriteAttributeString("x1", Percentage(gradient.X1));
                    writer.WriteAttributeString("y1", Percentage(gradient.Y1));
                    writer.WriteAttributeString("x2", Percentage(gradient.X2));
                    writer.WriteAttributeString("y2", Percentage(gradient.Y2));
                    foreach (var stop in gradient.Stops)
                    {
                        writer.WriteStartElement("stop");
                        writer.WriteAttributeString("offset", Percentage(stop.Offset));
                        writer.WriteAttributeString("stop-color", stop.Color);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }

            foreach (var primitive in asset.Primitives)
            {
                WritePrimitive(writer, primitive);
            }
            writer.WriteEndElement();
        }

        return new UTF8Encoding(false).GetBytes(builder.Append('\n').ToString());
    }

    private static void WritePrimitive(XmlWriter writer, BrandPrimitive primitive)
    {
        switch (primitive)
        {
            case RoundedRectanglePrimitive rectangle:
                writer.WriteStartElement("rect");
                writer.WriteAttributeString("x", Number(rectangle.X));
                writer.WriteAttributeString("y", Number(rectangle.Y));
                writer.WriteAttributeString("width", Number(rectangle.Width));
                writer.WriteAttributeString("height", Number(rectangle.Height));
                writer.WriteAttributeString("rx", Number(rectangle.Radius));
                WritePaint(writer, rectangle.Fill, rectangle.Stroke, rectangle.StrokeWidth);
                writer.WriteEndElement();
                break;
            case LinePrimitive line:
                writer.WriteStartElement("line");
                writer.WriteAttributeString("x1", Number(line.X1));
                writer.WriteAttributeString("y1", Number(line.Y1));
                writer.WriteAttributeString("x2", Number(line.X2));
                writer.WriteAttributeString("y2", Number(line.Y2));
                WritePaint(writer, "none", line.Stroke, line.StrokeWidth);
                writer.WriteAttributeString("stroke-linecap", "round");
                writer.WriteEndElement();
                break;
            case CirclePrimitive circle:
                writer.WriteStartElement("circle");
                writer.WriteAttributeString("cx", Number(circle.CenterX));
                writer.WriteAttributeString("cy", Number(circle.CenterY));
                writer.WriteAttributeString("r", Number(circle.Radius));
                WritePaint(writer, circle.Fill, circle.Stroke, circle.StrokeWidth);
                writer.WriteEndElement();
                break;
            case PolylinePrimitive polyline:
                writer.WriteStartElement(polyline.Closed ? "polygon" : "polyline");
                writer.WriteAttributeString(
                    "points",
                    string.Join(" ", polyline.Points.Select(point =>
                        $"{Number(point.X)},{Number(point.Y)}")));
                WritePaint(writer, polyline.Fill, polyline.Stroke, polyline.StrokeWidth);
                writer.WriteAttributeString("stroke-linecap", "round");
                writer.WriteAttributeString("stroke-linejoin", "round");
                writer.WriteEndElement();
                break;
            default:
                throw new NotSupportedException($"Unsupported brand primitive: {primitive.GetType().Name}");
        }
    }

    private static void WritePaint(XmlWriter writer, string fill, string stroke, double strokeWidth)
    {
        writer.WriteAttributeString("fill", fill);
        writer.WriteAttributeString("stroke", stroke);
        if (strokeWidth > 0)
        {
            writer.WriteAttributeString("stroke-width", Number(strokeWidth));
        }
    }

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Percentage(double value) =>
        (value * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    private static void AtomicWrite(string destination, ReadOnlySpan<byte> content)
    {
        var temporary = destination + ".tmp";
        File.WriteAllBytes(temporary, content.ToArray());
        File.Move(temporary, destination, true);
    }
}
