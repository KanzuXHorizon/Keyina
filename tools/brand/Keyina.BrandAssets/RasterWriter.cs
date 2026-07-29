using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace Keyina.BrandAssets;

internal static class RasterWriter
{
    private const int Supersample = 4;
    private static readonly int[] AppSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512];
    private static readonly int[] IconFrameSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public static void GenerateAll(string repositoryRoot)
    {
        var generatedRoot = Path.Combine(repositoryRoot, "brand", "generated");
        if (Directory.Exists(generatedRoot))
        {
            Directory.Delete(generatedRoot, recursive: true);
        }
        Directory.CreateDirectory(generatedRoot);

        var definitions = BrandGeometry.CreateAll()
            .ToDictionary(asset => asset.FileName, StringComparer.Ordinal);
        var manifest = new List<GeneratedAsset>();

        var mark = definitions["keyina-mark.svg"];
        var appFrames = new List<IconFrame>();
        foreach (var size in AppSizes)
        {
            var bytes = RenderPng(mark, size, size);
            var relativePath = $"brand/generated/app/keyina-app-{size}.png";
            var fullPath = Write(repositoryRoot, relativePath, bytes);
            manifest.Add(AssetManifest.Create(
                repositoryRoot,
                fullPath,
                "app-icon",
                "png",
                size,
                size,
                "brand/keyina-mark.svg"));
            if (IconFrameSizes.Contains(size))
            {
                appFrames.Add(new IconFrame(size, bytes));
            }
        }
        WriteIco(
            repositoryRoot,
            "brand/generated/keyina.ico",
            "app-icon",
            "brand/keyina-mark.svg",
            appFrames,
            manifest);

        var lockup = definitions["keyina-lockup.svg"];
        var lockupBytes = RenderPng(lockup, 1680, 512);
        var lockupPath = Write(
            repositoryRoot,
            "brand/generated/lockup/keyina-lockup-1680x512.png",
            lockupBytes);
        manifest.Add(AssetManifest.Create(
            repositoryRoot,
            lockupPath,
            "lockup",
            "png",
            1680,
            512,
            "brand/keyina-lockup.svg"));

        foreach (var state in new[] { "active", "inactive", "listening" })
        {
            var source = $"brand/keyina-tray-{state}.svg";
            var definition = definitions[$"keyina-tray-{state}.svg"];
            var frames = new List<IconFrame>();
            foreach (var size in IconFrameSizes)
            {
                var bytes = RenderPng(definition, size, size);
                frames.Add(new IconFrame(size, bytes));
                var relativePath = $"brand/generated/tray/keyina-tray-{state}-{size}.png";
                var fullPath = Write(repositoryRoot, relativePath, bytes);
                manifest.Add(AssetManifest.Create(
                    repositoryRoot,
                    fullPath,
                    $"tray-{state}",
                    "png",
                    size,
                    size,
                    source));
            }

            WriteIco(
                repositoryRoot,
                $"brand/generated/keyina-tray-{state}.ico",
                $"tray-{state}",
                source,
                frames,
                manifest);
        }

        AssetManifest.Write(repositoryRoot, manifest);
    }

    private static void WriteIco(
        string repositoryRoot,
        string relativePath,
        string role,
        string source,
        IReadOnlyList<IconFrame> frames,
        List<GeneratedAsset> manifest)
    {
        var fullPath = Write(repositoryRoot, relativePath, IcoWriter.Create(frames));
        manifest.Add(AssetManifest.Create(
            repositoryRoot,
            fullPath,
            role,
            "ico",
            256,
            256,
            source,
            frames.Select(frame => frame.Size).ToArray()));
    }

    private static byte[] RenderPng(BrandAssetDefinition asset, int width, int height)
    {
        var highWidth = checked(width * Supersample);
        var highHeight = checked(height * Supersample);
        using var highResolution = new Bitmap(highWidth, highHeight, PixelFormat.Format32bppPArgb);
        highResolution.SetResolution(96 * Supersample, 96 * Supersample);
        using (var graphics = Graphics.FromImage(highResolution))
        {
            ConfigureGraphics(graphics);
            graphics.Clear(Color.Transparent);
            graphics.ScaleTransform(
                (float)(highWidth / asset.Width),
                (float)(highHeight / asset.Height));
            foreach (var primitive in asset.Primitives)
            {
                DrawPrimitive(graphics, asset, primitive);
            }
        }

        using var final = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        final.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(final))
        {
            ConfigureGraphics(graphics);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(
                highResolution,
                new Rectangle(0, 0, width, height),
                0,
                0,
                highWidth,
                highHeight,
                GraphicsUnit.Pixel);
        }

        using var stream = new MemoryStream();
        final.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private static void DrawPrimitive(
        Graphics graphics,
        BrandAssetDefinition asset,
        BrandPrimitive primitive)
    {
        switch (primitive)
        {
            case RoundedRectanglePrimitive rectangle:
                using (var path = CreateRoundedRectangle(rectangle))
                {
                    using var fill = CreateBrush(
                        asset,
                        rectangle.Fill,
                        new RectangleF(
                            (float)rectangle.X,
                            (float)rectangle.Y,
                            (float)rectangle.Width,
                            (float)rectangle.Height));
                    if (fill is not null)
                    {
                        graphics.FillPath(fill, path);
                    }
                    using var pen = CreatePen(rectangle.Stroke, rectangle.StrokeWidth);
                    if (pen is not null)
                    {
                        graphics.DrawPath(pen, path);
                    }
                }
                break;
            case LinePrimitive line:
                using (var pen = CreatePen(line.Stroke, line.StrokeWidth))
                {
                    if (pen is not null)
                    {
                        graphics.DrawLine(
                            pen,
                            (float)line.X1,
                            (float)line.Y1,
                            (float)line.X2,
                            (float)line.Y2);
                    }
                }
                break;
            case CirclePrimitive circle:
                var bounds = new RectangleF(
                    (float)(circle.CenterX - circle.Radius),
                    (float)(circle.CenterY - circle.Radius),
                    (float)(circle.Radius * 2),
                    (float)(circle.Radius * 2));
                using (var fill = CreateBrush(asset, circle.Fill, bounds))
                {
                    if (fill is not null)
                    {
                        graphics.FillEllipse(fill, bounds);
                    }
                }
                using (var pen = CreatePen(circle.Stroke, circle.StrokeWidth))
                {
                    if (pen is not null)
                    {
                        graphics.DrawEllipse(pen, bounds);
                    }
                }
                break;
            case PolylinePrimitive polyline:
                var points = polyline.Points
                    .Select(point => new PointF((float)point.X, (float)point.Y))
                    .ToArray();
                if (points.Length < 2)
                {
                    throw new InvalidOperationException("Polyline requires at least two points.");
                }
                if (polyline.Closed)
                {
                    using var fill = CreateBrush(
                        asset,
                        polyline.Fill,
                        Bounds(points));
                    if (fill is not null)
                    {
                        graphics.FillPolygon(fill, points);
                    }
                    using var pen = CreatePen(polyline.Stroke, polyline.StrokeWidth);
                    if (pen is not null)
                    {
                        graphics.DrawPolygon(pen, points);
                    }
                }
                else
                {
                    using var pen = CreatePen(polyline.Stroke, polyline.StrokeWidth);
                    if (pen is not null)
                    {
                        graphics.DrawLines(pen, points);
                    }
                }
                break;
            default:
                throw new NotSupportedException($"Unsupported brand primitive: {primitive.GetType().Name}");
        }
    }

    private static GraphicsPath CreateRoundedRectangle(RoundedRectanglePrimitive rectangle)
    {
        var path = new GraphicsPath();
        var radius = Math.Min(
            rectangle.Radius,
            Math.Min(rectangle.Width, rectangle.Height) / 2);
        if (radius <= 0)
        {
            path.AddRectangle(new RectangleF(
                (float)rectangle.X,
                (float)rectangle.Y,
                (float)rectangle.Width,
                (float)rectangle.Height));
            return path;
        }

        var diameter = (float)(radius * 2);
        var x = (float)rectangle.X;
        var y = (float)rectangle.Y;
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Brush? CreateBrush(
        BrandAssetDefinition asset,
        string paint,
        RectangleF bounds)
    {
        if (paint == "none")
        {
            return null;
        }
        if (!paint.StartsWith("url(#", StringComparison.Ordinal) || !paint.EndsWith(')'))
        {
            return new SolidBrush(ParseColor(paint));
        }

        var identifier = paint[5..^1];
        var gradient = asset.Gradients.Single(item => item.Id == identifier);
        var start = new PointF(
            bounds.Left + ((float)gradient.X1 * bounds.Width),
            bounds.Top + ((float)gradient.Y1 * bounds.Height));
        var end = new PointF(
            bounds.Left + ((float)gradient.X2 * bounds.Width),
            bounds.Top + ((float)gradient.Y2 * bounds.Height));
        var brush = new LinearGradientBrush(start, end, Color.Black, Color.Black)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = gradient.Stops.Select(stop => ParseColor(stop.Color)).ToArray(),
                Positions = gradient.Stops.Select(stop => (float)stop.Offset).ToArray(),
            },
        };
        return brush;
    }

    private static Pen? CreatePen(string paint, double width)
    {
        if (paint == "none" || width <= 0)
        {
            return null;
        }
        return new Pen(ParseColor(paint), (float)width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
            Alignment = PenAlignment.Center,
        };
    }

    private static Color ParseColor(string value)
    {
        if (value.Length != 7 || value[0] != '#')
        {
            throw new InvalidOperationException($"Unsupported brand color: {value}");
        }
        return Color.FromArgb(
            255,
            byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static RectangleF Bounds(IReadOnlyList<PointF> points)
    {
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static string Write(string repositoryRoot, string relativePath, ReadOnlySpan<byte> bytes)
    {
        var fullPath = Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        File.WriteAllBytes(temporary, bytes.ToArray());
        File.Move(temporary, fullPath, true);
        return fullPath;
    }
}
