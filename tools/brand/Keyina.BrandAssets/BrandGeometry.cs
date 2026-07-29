namespace Keyina.BrandAssets;

internal static class BrandGeometry
{
    private const string Blue = "#1677FF";
    private const string Violet = "#6D28D9";
    private const string Red = "#FF3D5A";
    private const string Ink = "#111827";
    private const string Paper = "#F8FAFC";
    private const string Muted = "#9CA3AF";
    private const string None = "none";
    private const string MainGradient = "brandGradient";

    public static IReadOnlyList<BrandAssetDefinition> CreateAll() =>
    [
        CreateMark(),
        CreateLockup(),
        CreateTray("keyina-tray-active.svg", "Keyina tray icon — Vietnamese input enabled", Paper),
        CreateTray("keyina-tray-inactive.svg", "Keyina tray icon — Vietnamese input disabled", Muted, muted: true),
        CreateTray("keyina-tray-listening.svg", "Keyina tray icon — speech dictation listening", Red, listening: true),
    ];

    private static BrandAssetDefinition CreateMark()
    {
        var primitives = new List<BrandPrimitive>();
        AddMark(primitives, 0, 0, 1);
        return new BrandAssetDefinition(
            "keyina-mark.svg",
            "Keyina application mark",
            256,
            256,
            CreateGradients(),
            primitives);
    }

    private static BrandAssetDefinition CreateLockup()
    {
        var primitives = new List<BrandPrimitive>();
        AddMark(primitives, 0, 0, 1);
        AddWordmark(primitives);
        return new BrandAssetDefinition(
            "keyina-lockup.svg",
            "Keyina logo",
            840,
            256,
            CreateGradients(),
            primitives);
    }

    private static BrandAssetDefinition CreateTray(
        string fileName,
        string title,
        string foreground,
        bool muted = false,
        bool listening = false)
    {
        var primitives = new List<BrandPrimitive>();
        const string underlay = Ink;
        AddTrayGlyph(primitives, underlay, 2.2, muted, listening);
        AddTrayGlyph(primitives, foreground, 1.15, muted, listening);
        return new BrandAssetDefinition(fileName, title, 16, 16, [], primitives);
    }

    private static IReadOnlyList<GradientDefinition> CreateGradients() =>
    [
        new GradientDefinition(
            MainGradient,
            0,
            0,
            1,
            1,
            [
                new GradientStop(0, Blue),
                new GradientStop(0.52, Violet),
                new GradientStop(1, Red),
            ]),
    ];

    private static void AddMark(List<BrandPrimitive> primitives, double offsetX, double offsetY, double scale)
    {
        double X(double value) => offsetX + (value * scale);
        double Y(double value) => offsetY + (value * scale);
        double S(double value) => value * scale;

        primitives.Add(new RoundedRectanglePrimitive(
            X(8), Y(8), S(240), S(240), S(52),
            $"url(#{MainGradient})", None, 0));
        primitives.Add(new RoundedRectanglePrimitive(
            X(48), Y(70), S(160), S(116), S(44),
            None, Paper, S(14)));
        primitives.Add(new PolylinePrimitive(
            [new PointD(X(72), Y(178)), new PointD(X(72), Y(216)), new PointD(X(108), Y(184))],
            None,
            Paper,
            S(14),
            false));

        var waveform = new[]
        {
            (76d, 122d, 76d, 140d),
            (94d, 108d, 94d, 154d),
            (112d, 92d, 112d, 170d),
            (130d, 80d, 130d, 182d),
            (148d, 96d, 148d, 166d),
            (166d, 108d, 166d, 154d),
            (184d, 120d, 184d, 142d),
        };
        foreach (var (x1, y1, x2, y2) in waveform)
        {
            primitives.Add(new LinePrimitive(X(x1), Y(y1), X(x2), Y(y2), Paper, S(11)));
        }
        primitives.Add(new LinePrimitive(X(60), Y(132), X(68), Y(132), Paper, S(8)));
        primitives.Add(new LinePrimitive(X(192), Y(132), X(200), Y(132), Paper, S(8)));
        primitives.Add(new LinePrimitive(X(136), Y(42), X(154), Y(22), Paper, S(13)));
        primitives.Add(new CirclePrimitive(X(128), Y(52), S(7), Paper, None, 0));
    }

    private static void AddWordmark(List<BrandPrimitive> primitives)
    {
        const double stroke = 22;
        const string color = Ink;

        AddOpenPath(primitives, color, stroke,
            new PointD(320, 70), new PointD(320, 184));
        AddOpenPath(primitives, color, stroke,
            new PointD(320, 128), new PointD(374, 70));
        AddOpenPath(primitives, color, stroke,
            new PointD(320, 128), new PointD(378, 184));

        AddOpenPath(primitives, color, stroke,
            new PointD(410, 132), new PointD(486, 132), new PointD(478, 105),
            new PointD(458, 89), new PointD(432, 89), new PointD(411, 104),
            new PointD(402, 130), new PointD(409, 156), new PointD(430, 174),
            new PointD(458, 174), new PointD(482, 158));

        AddOpenPath(primitives, color, stroke,
            new PointD(512, 94), new PointD(542, 151), new PointD(572, 94));
        AddOpenPath(primitives, color, stroke,
            new PointD(542, 151), new PointD(525, 190));

        AddOpenPath(primitives, color, stroke,
            new PointD(606, 108), new PointD(606, 174));
        primitives.Add(new CirclePrimitive(606, 82, 9, color, None, 0));

        AddOpenPath(primitives, color, stroke,
            new PointD(646, 174), new PointD(646, 104), new PointD(673, 104),
            new PointD(700, 126), new PointD(700, 174));

        primitives.Add(new CirclePrimitive(752, 139, 35, None, color, stroke));
        AddOpenPath(primitives, color, stroke,
            new PointD(787, 105), new PointD(787, 174));
    }

    private static void AddOpenPath(
        List<BrandPrimitive> primitives,
        string stroke,
        double strokeWidth,
        params PointD[] points)
    {
        primitives.Add(new PolylinePrimitive(points, None, stroke, strokeWidth, false));
    }

    private static void AddTrayGlyph(
        List<BrandPrimitive> primitives,
        string color,
        double strokeWidth,
        bool muted,
        bool listening)
    {
        primitives.Add(new RoundedRectanglePrimitive(
            3, 4.5, 10, 7.5, 3,
            None, color, strokeWidth));
        primitives.Add(new PolylinePrimitive(
            [new PointD(4.5, 11), new PointD(4.5, 13), new PointD(6.5, 11.8)],
            None,
            color,
            strokeWidth,
            false));

        var bars = listening
            ? new[] { (6d, 7d, 6d, 10d), (8d, 5.5d, 8d, 11d), (10d, 6.5d, 10d, 10.5d) }
            : new[] { (6d, 7.5d, 6d, 9.5d), (8d, 6d, 8d, 11d), (10d, 7d, 10d, 10d) };
        foreach (var (x1, y1, x2, y2) in bars)
        {
            primitives.Add(new LinePrimitive(x1, y1, x2, y2, color, strokeWidth));
        }

        primitives.Add(new LinePrimitive(8.7, 3.5, 9.7, 3, color, strokeWidth));
        primitives.Add(new CirclePrimitive(8, 3.5, 0.45, color, None, 0));

        if (muted)
        {
            primitives.Add(new LinePrimitive(4, 4, 12, 12, color, strokeWidth));
        }
        if (listening)
        {
            primitives.Add(new CirclePrimitive(12.5, 4, 0.5, color, None, 0));
        }
    }
}

internal sealed record BrandAssetDefinition(
    string FileName,
    string Title,
    double Width,
    double Height,
    IReadOnlyList<GradientDefinition> Gradients,
    IReadOnlyList<BrandPrimitive> Primitives);

internal sealed record GradientDefinition(
    string Id,
    double X1,
    double Y1,
    double X2,
    double Y2,
    IReadOnlyList<GradientStop> Stops);

internal sealed record GradientStop(double Offset, string Color);

internal readonly record struct PointD(double X, double Y);

internal abstract record BrandPrimitive;

internal sealed record RoundedRectanglePrimitive(
    double X,
    double Y,
    double Width,
    double Height,
    double Radius,
    string Fill,
    string Stroke,
    double StrokeWidth) : BrandPrimitive;

internal sealed record LinePrimitive(
    double X1,
    double Y1,
    double X2,
    double Y2,
    string Stroke,
    double StrokeWidth) : BrandPrimitive;

internal sealed record CirclePrimitive(
    double CenterX,
    double CenterY,
    double Radius,
    string Fill,
    string Stroke,
    double StrokeWidth) : BrandPrimitive;

internal sealed record PolylinePrimitive(
    IReadOnlyList<PointD> Points,
    string Fill,
    string Stroke,
    double StrokeWidth,
    bool Closed) : BrandPrimitive;
