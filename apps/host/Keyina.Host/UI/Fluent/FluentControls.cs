using System.ComponentModel;
using System.Drawing.Drawing2D;

#pragma warning disable CA1725

namespace Keyina.Host.UI.Fluent;

public enum FluentButtonKind
{
    Primary,
    Secondary,
    Subtle,
    Danger,
}

public static class FluentMetrics
{
    public const int SurfaceCornerRadius = 0;

    public const int ControlCornerRadius = 0;
}

public sealed class FluentCard : Panel
{
    private FluentThemePalette palette = FluentTheme.Current;

    public FluentCard()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Padding = new Padding(20);
        Margin = new Padding(0, 0, 0, 12);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = FluentMetrics.SurfaceCornerRadius;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool UseSecondarySurface { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate(true);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(
            FluentDrawing.ResolveBackground(this, palette.Window));
        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = FluentDrawing.CreateRoundedRectangle(bounds, Scale(CornerRadius));
        using var brush = new SolidBrush(
            UseSecondarySurface ? palette.SurfaceSecondary : palette.Surface);
        eventArgs.Graphics.FillPath(brush, path);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = FluentDrawing.CreateRoundedRectangle(bounds, Scale(CornerRadius));
        using var pen = new Pen(palette.Border);
        eventArgs.Graphics.DrawPath(pen, path);
    }

    private int Scale(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96F));
}

public sealed class FluentToggle : CheckBox
{
    private FluentThemePalette palette = FluentTheme.Current;
    private bool hovered;

    public FluentToggle()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        Size = new Size(46, 26);
        Cursor = Cursors.Hand;
        Text = string.Empty;
        AccessibleRole = AccessibleRole.CheckButton;
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate();
        }
    }

    protected override void OnCheckedChanged(EventArgs eventArgs)
    {
        base.OnCheckedChanged(eventArgs);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(BackColor);

        var trackHeight = Scale(20);
        var trackWidth = Scale(40);
        var trackBounds = new Rectangle(
            (Width - trackWidth) / 2,
            (Height - trackHeight) / 2,
            trackWidth,
            trackHeight);
        var trackColor = Checked
            ? hovered ? palette.AccentHover : palette.Accent
            : hovered ? palette.BorderStrong : palette.SurfacePressed;
        if (!Enabled)
        {
            trackColor = Color.FromArgb(110, trackColor);
        }

        using (var path = FluentDrawing.CreateRoundedRectangle(trackBounds, trackHeight / 2))
        using (var brush = new SolidBrush(trackColor))
        {
            eventArgs.Graphics.FillPath(brush, path);
        }

        var thumbSize = Scale(14);
        var thumbInset = Scale(3);
        var thumbX = Checked
            ? trackBounds.Right - thumbInset - thumbSize
            : trackBounds.Left + thumbInset;
        var thumbBounds = new Rectangle(
            thumbX,
            trackBounds.Top + (trackHeight - thumbSize) / 2,
            thumbSize,
            thumbSize);
        using (var brush = new SolidBrush(Enabled ? Color.White : palette.TextTertiary))
        {
            eventArgs.Graphics.FillEllipse(brush, thumbBounds);
        }

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(trackBounds, Scale(3), Scale(3));
            using var pen = new Pen(palette.Focus) { DashStyle = DashStyle.Dot };
            eventArgs.Graphics.DrawRectangle(pen, focus);
        }
    }

    private int Scale(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96F));
}

public sealed class FluentNavigationButton : Button
{
    private FluentThemePalette palette = FluentTheme.Current;
    private bool hovered;
    private bool selected;

    public FluentNavigationButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        TextAlign = ContentAlignment.MiddleLeft;
        Cursor = Cursors.Hand;
        Height = 42;
        Margin = new Padding(0, 0, 0, 4);
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = "\uE80F";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(
            FluentDrawing.ResolveBackground(this, palette.Sidebar));

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (selected || hovered || Focused)
        {
            var fill = selected ? palette.SurfacePressed : palette.SurfaceHover;
            using var path = FluentDrawing.CreateRoundedRectangle(
                bounds,
                Scale(FluentMetrics.ControlCornerRadius));
            using var brush = new SolidBrush(fill);
            eventArgs.Graphics.FillPath(brush, path);
        }

        if (selected)
        {
            var indicator = new Rectangle(
                Scale(2),
                Scale(11),
                Scale(3),
                Math.Max(Scale(18), Height - Scale(22)));
            using var path = FluentDrawing.CreateRoundedRectangle(indicator, Scale(2));
            using var brush = new SolidBrush(palette.Accent);
            eventArgs.Graphics.FillPath(brush, path);
        }

        var iconBounds = new Rectangle(Scale(14), 0, Scale(24), Height);
        using (var iconFont = new Font("Segoe Fluent Icons", ScaleFont(12F), FontStyle.Regular))
        {
            TextRenderer.DrawText(
                eventArgs.Graphics,
                Glyph,
                iconFont,
                iconBounds,
                Enabled ? palette.TextSecondary : palette.TextTertiary,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        var textBounds = new Rectangle(Scale(48), 0, Math.Max(0, Width - Scale(58)), Height);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? palette.TextPrimary : palette.TextTertiary,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(bounds, -Scale(3), -Scale(3));
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focus, palette.Focus, Color.Transparent);
        }
    }

    private int Scale(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96F));

    private float ScaleFont(float points) => points * DeviceDpi / 96F;
}

public sealed class FluentButton : Button
{
    private FluentThemePalette palette = FluentTheme.Current;
    private bool hovered;
    private bool pressed;

    public FluentButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        AutoSize = false;
        Height = 36;
        Padding = new Padding(12, 0, 12, 0);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentButtonKind Kind { get; set; } = FluentButtonKind.Secondary;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        pressed = true;
        Invalidate();
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(
            FluentDrawing.ResolveBackground(this, palette.Surface));
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var (background, foreground, border) = ResolveColors();

        using (var path = FluentDrawing.CreateRoundedRectangle(
                   bounds,
                   Scale(FluentMetrics.ControlCornerRadius)))
        using (var brush = new SolidBrush(background))
        using (var pen = new Pen(border))
        {
            eventArgs.Graphics.FillPath(brush, path);
            eventArgs.Graphics.DrawPath(pen, path);
        }

        var text = string.IsNullOrEmpty(Glyph) ? Text : $"{Glyph}  {Text}";
        var font = string.IsNullOrEmpty(Glyph)
            ? Font
            : new Font("Segoe UI", Font.Size, Font.Style);
        try
        {
            TextRenderer.DrawText(
                eventArgs.Graphics,
                text,
                font,
                bounds,
                Enabled ? foreground : palette.TextTertiary,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
        finally
        {
            if (!ReferenceEquals(font, Font))
            {
                font.Dispose();
            }
        }

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(bounds, -Scale(3), -Scale(3));
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focus, palette.Focus, background);
        }
    }

    private (Color Background, Color Foreground, Color Border) ResolveColors()
    {
        if (!Enabled)
        {
            return (palette.SurfacePressed, palette.TextTertiary, palette.Border);
        }

        return Kind switch
        {
            FluentButtonKind.Primary => (
                pressed ? palette.AccentPressed : hovered ? palette.AccentHover : palette.Accent,
                palette.AccentText,
                pressed ? palette.AccentPressed : hovered ? palette.AccentHover : palette.Accent),
            FluentButtonKind.Danger => (
                pressed ? Color.FromArgb(38, palette.Error) : hovered ? Color.FromArgb(28, palette.Error) : palette.SurfaceSecondary,
                palette.Error,
                hovered ? palette.Error : palette.Border),
            FluentButtonKind.Subtle => (
                pressed ? palette.SurfacePressed : hovered ? palette.SurfaceHover : palette.Surface,
                palette.TextPrimary,
                pressed || hovered ? palette.Border : palette.Surface),
            _ => (
                pressed ? palette.SurfacePressed : hovered ? palette.SurfaceHover : palette.SurfaceSecondary,
                palette.TextPrimary,
                hovered ? palette.BorderStrong : palette.Border),
        };
    }

    private int Scale(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96F));
}

public sealed class FluentStatusBadge : Label
{
    private FluentThemePalette palette = FluentTheme.Current;

    public FluentStatusBadge()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        Height = 28;
        TextAlign = ContentAlignment.MiddleCenter;
        Padding = new Padding(10, 0, 10, 0);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentTone Tone { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(
            FluentDrawing.ResolveBackground(this, palette.Surface));
        var tone = FluentTheme.ToneColor(palette, Tone);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = FluentDrawing.CreateRoundedRectangle(
                   bounds,
                   Scale(FluentMetrics.ControlCornerRadius)))
        using (var brush = new SolidBrush(Color.FromArgb(palette.IsDark ? 38 : 24, tone)))
        using (var pen = new Pen(Color.FromArgb(palette.IsDark ? 96 : 64, tone)))
        {
            eventArgs.Graphics.FillPath(brush, path);
            eventArgs.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            bounds,
            tone,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    private int Scale(int logicalPixels) =>
        Math.Max(0, (int)Math.Round(logicalPixels * DeviceDpi / 96F));
}

#pragma warning restore CA1725

internal static class FluentDrawing
{
    public static Color ResolveBackground(Control control, Color fallback)
    {
        ArgumentNullException.ThrowIfNull(control);
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.BackColor.A == byte.MaxValue)
            {
                return parent.BackColor;
            }
        }
        return fallback;
    }

    public static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rectangle);
            return path;
        }

        var clampedRadius = Math.Min(
            radius,
            Math.Min(rectangle.Width, rectangle.Height) / 2);
        var diameter = clampedRadius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
