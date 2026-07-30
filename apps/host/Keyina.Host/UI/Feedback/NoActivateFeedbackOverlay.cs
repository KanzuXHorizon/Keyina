using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Keyina.Host.Core.Feedback;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI.Feedback;

public sealed class NoActivateFeedbackOverlay : Form, IFeedbackOverlay
{
    private const int WindowMessageNonClientHitTest = 0x0084;
    private const int HitTransparent = -1;
    private const int ExtendedToolWindow = 0x00000080;
    private const int ExtendedTransparent = 0x00000020;
    private const int ExtendedLayered = 0x00080000;
    private const int ExtendedNoActivate = 0x08000000;
    private const int ClassDropShadow = 0x00020000;
    private const int ShowNoActivate = 4;
    private const uint SetWindowNoActivate = 0x0010;
    private const uint SetWindowShow = 0x0040;
    private static readonly IntPtr TopMostWindow = new(-1);
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);

    private readonly System.Windows.Forms.Timer animationTimer = new()
    {
        Interval = 16,
    };
    private FeedbackEvent? currentEvent;
    private FluentThemePalette palette = FluentTheme.Current;
    private long presentedTimestamp;
    private bool resourcesReleased;

    public NoActivateFeedbackOverlay()
    {
        Name = "feedbackOverlay";
        Text = string.Empty;
        AccessibleName = "Phản hồi Keyina";
        AccessibleRole = AccessibleRole.Alert;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        DoubleBuffered = true;
        Font = new Font(
            "Segoe UI Variable Text",
            10.5F,
            FontStyle.Bold,
            GraphicsUnit.Point);
        Size = new Size(280, 58);
        BackColor = palette.Surface;
        Opacity = 0;
        animationTimer.Tick += AnimationTimerTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= ClassDropShadow;
            parameters.ExStyle |=
                ExtendedToolWindow |
                ExtendedTransparent |
                ExtendedLayered |
                ExtendedNoActivate;
            return parameters;
        }
    }

    public void Present(FeedbackEvent feedbackEvent)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(feedbackEvent);
        if (string.IsNullOrWhiteSpace(feedbackEvent.Message))
        {
            throw new ArgumentException("Feedback message must not be empty.", nameof(feedbackEvent));
        }
        if (feedbackEvent.Duration <= TimeSpan.Zero &&
            feedbackEvent.Duration != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(feedbackEvent),
                "Feedback duration must be positive or infinite.");
        }

        currentEvent = feedbackEvent;
        palette = FluentTheme.Current;
        BackColor = palette.Surface;
        AccessibleDescription = feedbackEvent.Message;
        UpdateOverlaySize(feedbackEvent.Message);
        var bounds = CalculateBounds();
        Bounds = bounds;
        UpdateRoundedRegion();
        presentedTimestamp = Stopwatch.GetTimestamp();

        var animate = ShouldAnimate();
        Opacity = animate ? 0.01 : 1;
        _ = Handle;
        _ = SetWindowPos(
            Handle,
            TopMostWindow,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SetWindowNoActivate | SetWindowShow);
        _ = ShowWindow(Handle, ShowNoActivate);
        Invalidate();

        animationTimer.Stop();
        animationTimer.Start();
    }

    public void HideFeedback()
    {
        animationTimer.Stop();
        currentEvent = null;
        Opacity = 0;
        if (IsHandleCreated)
        {
            _ = ShowWindow(Handle, command: 0);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WindowMessageNonClientHitTest)
        {
            m.Result = new IntPtr(HitTransparent);
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(palette.Surface);

        var contentBounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var surface = FluentDrawing.CreateRoundedRectangle(
            contentBounds,
            Scale(15));
        using var surfaceBrush = new SolidBrush(palette.Surface);
        e.Graphics.FillPath(surfaceBrush, surface);
        if (palette.Mode == FluentThemeMode.HighContrast)
        {
            using var borderPen = new Pen(palette.BorderStrong, Scale(1));
            e.Graphics.DrawPath(borderPen, surface);
        }

        if (currentEvent is null)
        {
            return;
        }

        var tone = ResolveTone(currentEvent.Tone);
        var iconBounds = new Rectangle(
            Scale(16),
            (Height - Scale(28)) / 2,
            Scale(28),
            Scale(28));
        using var iconBackground = new SolidBrush(
            Color.FromArgb(palette.IsDark ? 58 : 30, tone));
        e.Graphics.FillEllipse(iconBackground, iconBounds);
        using var glyphFont = new Font(
            "Segoe Fluent Icons",
            11F,
            FontStyle.Regular,
            GraphicsUnit.Point);
        TextRenderer.DrawText(
            e.Graphics,
            ResolveGlyph(currentEvent.Kind),
            glyphFont,
            iconBounds,
            tone,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix);

        var textBounds = new Rectangle(
            iconBounds.Right + Scale(12),
            0,
            Width - iconBounds.Right - Scale(28),
            Height);
        TextRenderer.DrawText(
            e.Graphics,
            currentEvent.Message,
            Font,
            textBounds,
            palette.TextPrimary,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesReleased)
        {
            resourcesReleased = true;
            animationTimer.Stop();
            animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AnimationTimerTick(object? sender, EventArgs eventArgs)
    {
        if (currentEvent is null)
        {
            animationTimer.Stop();
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(presentedTimestamp);
        if (!ShouldAnimate())
        {
            Opacity = 1;
            if (currentEvent.Duration != Timeout.InfiniteTimeSpan &&
                elapsed >= currentEvent.Duration)
            {
                HideFeedback();
            }
            return;
        }

        if (elapsed < EntranceDuration)
        {
            Opacity = EaseOut((double)elapsed.Ticks / EntranceDuration.Ticks);
            return;
        }

        if (currentEvent.Duration == Timeout.InfiniteTimeSpan ||
            elapsed <= currentEvent.Duration)
        {
            Opacity = 1;
            return;
        }

        var exitElapsed = elapsed - currentEvent.Duration;
        if (exitElapsed >= ExitDuration)
        {
            HideFeedback();
            return;
        }

        Opacity = Math.Clamp(
            1d - ((double)exitElapsed.Ticks / ExitDuration.Ticks),
            0.01,
            1d);
    }

    private void UpdateOverlaySize(string message)
    {
        var measured = TextRenderer.MeasureText(
            message,
            Font,
            new Size(420, Scale(30)),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Width = Math.Clamp(measured.Width + Scale(84), Scale(220), Scale(440));
        Height = Scale(58);
    }

    private Rectangle CalculateBounds()
    {
        var foreground = GetForegroundWindow();
        var screen = foreground == IntPtr.Zero
            ? Screen.PrimaryScreen
            : Screen.FromHandle(foreground);
        screen ??= Screen.PrimaryScreen;
        var area = screen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        return new Rectangle(
            area.Left + ((area.Width - Width) / 2),
            area.Bottom - Height - Scale(56),
            Width,
            Height);
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }
        using var path = FluentDrawing.CreateRoundedRectangle(
            new Rectangle(0, 0, Width, Height),
            Scale(15));
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private Color ResolveTone(FeedbackTone tone) => tone switch
    {
        FeedbackTone.Accent => palette.Accent,
        FeedbackTone.Success => palette.Success,
        FeedbackTone.Warning => palette.Warning,
        FeedbackTone.Error => palette.Error,
        _ => palette.TextSecondary,
    };

    private static string ResolveGlyph(FeedbackEventKind kind) => kind switch
    {
        FeedbackEventKind.VietnameseEnabled => "\uE73E",
        FeedbackEventKind.VietnameseDisabled => "\uE711",
        FeedbackEventKind.DictationConnecting => "\uE895",
        FeedbackEventKind.DictationListening => "\uE720",
        FeedbackEventKind.DictationFinalizing => "\uE823",
        FeedbackEventKind.DictationInserted => "\uE73E",
        FeedbackEventKind.DictationCancelled => "\uE711",
        FeedbackEventKind.Error => "\uEA39",
        FeedbackEventKind.Preview => "\uE7F4",
        _ => "\uE946",
    };

    private bool ShouldAnimate() =>
        palette.Mode != FluentThemeMode.HighContrast &&
        SystemInformation.IsMenuAnimationEnabled;

    private int Scale(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96F));

    private static double EaseOut(double progress)
    {
        var clamped = Math.Clamp(progress, 0d, 1d);
        return 1d - Math.Pow(1d - clamped, 3d);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
