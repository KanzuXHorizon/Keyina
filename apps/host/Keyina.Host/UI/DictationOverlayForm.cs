using System.Runtime.InteropServices;
using Keyina.Host.Core.Speech;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed class DictationOverlayForm : Form
{
    public const int MaximumVisibleTranscriptCharacters = 180;

    private const int ExtendedToolWindow = 0x00000080;
    private const int ExtendedTransparent = 0x00000020;
    private const int ExtendedNoActivate = 0x08000000;
    private const uint SetWindowNoActivate = 0x0010;
    private const uint SetWindowShow = 0x0040;
    private static readonly IntPtr TopMostWindow = new(-1);

    private readonly FluentThemePalette palette = FluentTheme.Current;
    private readonly Label stateIndicator;
    private readonly Label statusLabel;
    private readonly Label shortcutLabel;
    private readonly Label transcriptLabel;
    private bool resourcesReleased;

    public DictationOverlayForm()
    {
        Name = "dictationOverlay";
        Text = string.Empty;
        AccessibleName = "Bản ghi giọng nói trực tiếp";
        AccessibleRole = AccessibleRole.StatusBar;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        DoubleBuffered = true;
        ClientSize = new Size(560, 156);
        MinimumSize = ClientSize;
        MaximumSize = ClientSize;
        BackColor = palette.Surface;
        Font = new Font(
            "Segoe UI Variable Text",
            10F,
            FontStyle.Regular,
            GraphicsUnit.Point);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 15, 18, 15),
            Margin = Padding.Empty,
            BackColor = palette.Surface,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        Controls.Add(layout);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = palette.Surface,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);

        stateIndicator = new Label
        {
            Name = "dictationOverlayStateIndicator",
            AccessibleName = "Trạng thái nhập giọng nói",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe Fluent Icons", 12F, FontStyle.Regular),
            ForeColor = palette.Accent,
            BackColor = palette.Surface,
            UseMnemonic = false,
        };
        header.Controls.Add(stateIndicator, 0, 0);

        statusLabel = new Label
        {
            Name = "dictationOverlayStatus",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            ForeColor = palette.Accent,
            BackColor = palette.Surface,
            UseMnemonic = false,
        };
        header.Controls.Add(statusLabel, 1, 0);

        shortcutLabel = new Label
        {
            Name = "dictationOverlayShortcut",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular),
            ForeColor = palette.TextSecondary,
            BackColor = palette.Surface,
            UseMnemonic = false,
            Margin = Padding.Empty,
        };
        header.Controls.Add(shortcutLabel, 2, 0);

        transcriptLabel = new Label
        {
            Name = "dictationOverlayTranscript",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Regular),
            ForeColor = palette.TextPrimary,
            BackColor = palette.Surface,
            UseMnemonic = false,
            Padding = new Padding(0, 8, 0, 0),
        };
        layout.Controls.Add(transcriptLabel, 0, 1);

        var hint = new Label
        {
            Name = "dictationOverlayHint",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Esc · Hủy  ·  Keyina không ghi nội dung vào log",
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular),
            ForeColor = palette.TextTertiary,
            BackColor = palette.Surface,
            UseMnemonic = false,
        };
        layout.Controls.Add(hint, 0, 2);
    }

    public bool UsesNoActivateWindowStyle =>
        (CreateParams.ExStyle & ExtendedNoActivate) != 0;

    public bool UsesClickThroughWindowStyle =>
        (CreateParams.ExStyle & ExtendedTransparent) != 0;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |=
                ExtendedToolWindow |
                ExtendedTransparent |
                ExtendedNoActivate;
            return parameters;
        }
    }

    public void Present(DictationState state)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(state);

        switch (state.Status)
        {
            case DictationStatus.Connecting:
                stateIndicator.Text = "\uE895";
                stateIndicator.ForeColor = palette.Accent;
                statusLabel.Text = "Đang kết nối";
                shortcutLabel.Text = "Esc · Hủy";
                transcriptLabel.Text = "Chuẩn bị microphone…";
                break;
            case DictationStatus.Listening:
                stateIndicator.Text = "\uE720";
                stateIndicator.ForeColor = palette.Success;
                statusLabel.Text = "Đang nghe";
                shortcutLabel.Text = "Ctrl + Alt + V · Hoàn tất";
                transcriptLabel.Text = string.IsNullOrWhiteSpace(state.DisplayText)
                    ? "Hãy bắt đầu nói…"
                    : CreateVisibleTranscript(state.DisplayText);
                break;
            case DictationStatus.Finalizing:
                stateIndicator.Text = "\uE895";
                stateIndicator.ForeColor = palette.Warning;
                statusLabel.Text = "Đang hoàn tất";
                shortcutLabel.Text = string.Empty;
                transcriptLabel.Text = string.IsNullOrWhiteSpace(state.DisplayText)
                    ? "Đang chờ Speechmatics gửi phần cuối…"
                    : CreateVisibleTranscript(state.DisplayText);
                break;
            case DictationStatus.Error:
                stateIndicator.Text = "\uEA39";
                stateIndicator.ForeColor = palette.Error;
                statusLabel.Text = "Không thể nhập giọng nói";
                shortcutLabel.Text = "Esc · Đóng";
                transcriptLabel.Text = string.IsNullOrWhiteSpace(state.ErrorCode)
                    ? "Đã xảy ra lỗi. Mở Cài đặt để kiểm tra microphone và khóa API."
                    : $"Lỗi: {state.ErrorCode}";
                break;
            case DictationStatus.Idle:
            case DictationStatus.Inserted:
            case DictationStatus.Cancelled:
                HideOverlay();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        AccessibleDescription = $"{statusLabel.Text}. {transcriptLabel.Text}";
        PositionNearWorkingArea();
        ShowNoActivate();
    }

    public void HideOverlay()
    {
        transcriptLabel.Text = string.Empty;
        stateIndicator.Text = string.Empty;
        statusLabel.Text = string.Empty;
        shortcutLabel.Text = string.Empty;
        AccessibleDescription = string.Empty;
        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(palette.BorderStrong);
        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        e.Graphics.DrawRectangle(border, bounds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesReleased)
        {
            resourcesReleased = true;
            stateIndicator.Dispose();
            statusLabel.Dispose();
            shortcutLabel.Dispose();
            transcriptLabel.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string CreateVisibleTranscript(string transcript)
    {
        var normalized = transcript.Trim();
        if (normalized.Length <= MaximumVisibleTranscriptCharacters)
        {
            return normalized;
        }

        var start = normalized.Length - MaximumVisibleTranscriptCharacters;
        var wordBoundary = normalized.IndexOf(' ', start);
        if (wordBoundary >= start && wordBoundary < normalized.Length - 1)
        {
            start = wordBoundary + 1;
        }
        return "… " + normalized[start..];
    }

    private void ShowNoActivate()
    {
        _ = Handle;
        if (!Visible)
        {
            Show();
        }
        _ = SetWindowPos(
            Handle,
            TopMostWindow,
            Left,
            Top,
            Width,
            Height,
            SetWindowNoActivate | SetWindowShow);
        Invalidate();
    }

    private void PositionNearWorkingArea()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            Math.Max(area.Left + 12, area.Right - Width - 24),
            Math.Max(area.Top + 12, area.Bottom - Height - 24));
    }

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
