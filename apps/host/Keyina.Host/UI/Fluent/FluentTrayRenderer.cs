using System.Drawing.Drawing2D;

#pragma warning disable CA1725

namespace Keyina.Host.UI.Fluent;

public sealed class FluentTrayRenderer : ToolStripProfessionalRenderer
{
    private readonly FluentThemePalette palette;

    public FluentTrayRenderer(FluentThemePalette palette)
        : base(new FluentTrayColorTable(palette))
    {
        this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(Point.Empty, eventArgs.ToolStrip.Size);
        bounds.Width -= 1;
        bounds.Height -= 1;
        using var path = FluentDrawing.CreateRoundedRectangle(bounds, Scale(eventArgs.ToolStrip, 8));
        using var brush = new SolidBrush(palette.Surface);
        eventArgs.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(Point.Empty, eventArgs.ToolStrip.Size);
        bounds.Width -= 1;
        bounds.Height -= 1;
        using var path = FluentDrawing.CreateRoundedRectangle(bounds, Scale(eventArgs.ToolStrip, 8));
        using var pen = new Pen(palette.BorderStrong);
        eventArgs.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs eventArgs)
    {
        if (!eventArgs.Item.Selected || !eventArgs.Item.Enabled)
        {
            return;
        }

        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(
            Scale(eventArgs.ToolStrip, 4),
            Scale(eventArgs.ToolStrip, 2),
            Math.Max(1, eventArgs.Item.Width - Scale(eventArgs.ToolStrip, 8)),
            Math.Max(1, eventArgs.Item.Height - Scale(eventArgs.ToolStrip, 4)));
        using var path = FluentDrawing.CreateRoundedRectangle(bounds, Scale(eventArgs.ToolStrip, 5));
        using var brush = new SolidBrush(palette.SurfaceHover);
        eventArgs.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs eventArgs)
    {
        var y = eventArgs.Item.ContentRectangle.Top + eventArgs.Item.ContentRectangle.Height / 2;
        var toolStripWidth = eventArgs.ToolStrip?.Width ?? eventArgs.Item.Width;
        using var pen = new Pen(palette.Border);
        eventArgs.Graphics.DrawLine(
            pen,
            Scale(eventArgs.ToolStrip, 12),
            y,
            Math.Max(Scale(eventArgs.ToolStrip, 12), toolStripWidth - Scale(eventArgs.ToolStrip, 12)),
            y);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs eventArgs)
    {
        if (!eventArgs.Item.Enabled)
        {
            eventArgs.TextColor = palette.TextTertiary;
        }
        else if (string.Equals(eventArgs.Item.Tag as string, "danger", StringComparison.Ordinal))
        {
            eventArgs.TextColor = palette.Error;
        }
        else if (string.Equals(eventArgs.Item.Tag as string, "header", StringComparison.Ordinal))
        {
            eventArgs.TextColor = palette.TextPrimary;
        }
        else
        {
            eventArgs.TextColor = palette.TextPrimary;
        }

        base.OnRenderItemText(eventArgs);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Scale(eventArgs.ToolStrip, 16);
        var bounds = new Rectangle(
            eventArgs.ImageRectangle.Left + (eventArgs.ImageRectangle.Width - size) / 2,
            eventArgs.ImageRectangle.Top + (eventArgs.ImageRectangle.Height - size) / 2,
            size,
            size);
        using (var brush = new SolidBrush(palette.Accent))
        {
            eventArgs.Graphics.FillEllipse(brush, bounds);
        }

        using var pen = new Pen(Color.White, Math.Max(1F, Scale(eventArgs.ToolStrip, 1.5F)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        var x = bounds.Left;
        var y = bounds.Top;
        eventArgs.Graphics.DrawLines(
            pen,
            [
                new PointF(x + size * 0.27F, y + size * 0.52F),
                new PointF(x + size * 0.44F, y + size * 0.68F),
                new PointF(x + size * 0.75F, y + size * 0.34F),
            ]);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs eventArgs)
    {
        eventArgs.ArrowColor = eventArgs.Item?.Enabled != false
            ? palette.TextSecondary
            : palette.TextTertiary;
        base.OnRenderArrow(eventArgs);
    }

    private static int Scale(ToolStrip? toolStrip, int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * (toolStrip?.DeviceDpi ?? 96) / 96F));

    private static float Scale(ToolStrip? toolStrip, float logicalPixels) =>
        Math.Max(1F, logicalPixels * (toolStrip?.DeviceDpi ?? 96) / 96F);

    private sealed class FluentTrayColorTable : ProfessionalColorTable
    {
        private readonly FluentThemePalette palette;

        public FluentTrayColorTable(FluentThemePalette palette)
        {
            this.palette = palette;
            UseSystemColors = palette.Mode == FluentThemeMode.HighContrast;
        }

        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color ImageMarginGradientBegin => palette.Surface;
        public override Color ImageMarginGradientMiddle => palette.Surface;
        public override Color ImageMarginGradientEnd => palette.Surface;
        public override Color MenuItemBorder => palette.SurfaceHover;
        public override Color MenuItemSelected => palette.SurfaceHover;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Border;
    }
}

#pragma warning restore CA1725

public static class FluentTrayMenu
{
    public static void Apply(ContextMenuStrip menu, FluentThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(palette);

        menu.Renderer = new FluentTrayRenderer(palette);
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.TextPrimary;
        menu.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        menu.Padding = new Padding(8, 8, 8, 8);
        menu.ImageScalingSize = new Size(18, 18);
        menu.ShowImageMargin = true;
        menu.ShowCheckMargin = false;
        menu.DropShadowEnabled = true;
        menu.MinimumSize = new Size(286, 0);
        foreach (ToolStripItem item in menu.Items)
        {
            ApplyItemMetrics(item, palette);
        }

        ApplyRoundedRegion(menu);
    }

    public static void ApplyRoundedRegion(ContextMenuStrip menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (menu.Width <= 0 || menu.Height <= 0)
        {
            return;
        }

        using var path = FluentDrawing.CreateRoundedRectangle(
            new Rectangle(0, 0, menu.Width, menu.Height),
            Math.Max(6, (int)Math.Round(8 * menu.DeviceDpi / 96F)));
        var previous = menu.Region;
        menu.Region = new Region(path);
        previous?.Dispose();
    }

    public static Bitmap CreateGlyph(
        string glyph,
        FluentThemePalette palette,
        FluentTone tone = FluentTone.Neutral)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glyph);
        ArgumentNullException.ThrowIfNull(palette);
        var bitmap = new Bitmap(20, 20);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe Fluent Icons", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        var color = tone == FluentTone.Neutral
            ? palette.TextSecondary
            : FluentTheme.ToneColor(palette, tone);
        TextRenderer.DrawText(
            graphics,
            glyph,
            font,
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            color,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);
        return bitmap;
    }

    private static void ApplyItemMetrics(ToolStripItem item, FluentThemePalette palette)
    {
        item.AutoSize = true;
        item.Padding = item is ToolStripSeparator
            ? new Padding(0, 3, 0, 3)
            : new Padding(4, 5, 8, 5);
        item.ForeColor = palette.TextPrimary;
        if (item is ToolStripMenuItem menuItem)
        {
            menuItem.ShortcutKeyDisplayString ??= string.Empty;
        }
    }
}
