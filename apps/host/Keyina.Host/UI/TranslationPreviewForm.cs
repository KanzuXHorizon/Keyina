using Keyina.Host.Translation;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed class TranslationPreviewForm : Form
{
    private const float MinimumReaderZoom = 0.8F;
    private const float MaximumReaderZoom = 1.8F;
    private const float ReaderZoomStep = 0.1F;

    private readonly TranslationPreview preview;
    private readonly Action<TranslationPreview> replace;
    private readonly Action<string> copy;
    private readonly Action cancel;
    private readonly FluentThemePalette palette = FluentTheme.Current;
    private readonly RichTextBox translatedReader;
    private readonly Label metadataLabel;
    private readonly FluentButton replaceButton;
    private readonly System.Windows.Forms.Timer previewTimer = new()
    {
        Interval = 1_000,
    };
    private bool completed;

    public TranslationPreviewForm(
        TranslationPreview preview,
        Action<TranslationPreview> replace,
        Action<string> copy,
        Action cancel)
    {
        this.preview = preview ?? throw new ArgumentNullException(nameof(preview));
        this.replace = replace ?? throw new ArgumentNullException(nameof(replace));
        this.copy = copy ?? throw new ArgumentNullException(nameof(copy));
        this.cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));

        Text = "Bản dịch";
        AccessibleName = "Trình đọc bản dịch Keyina";
        AccessibleDescription =
            "Hiển thị bản dịch nhưng không thay đổi văn bản đang chọn cho đến khi bạn chọn Thay thế. Có thể sao chép hoặc đóng.";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(480, 320);
        Size = CalculateInitialSize(preview.TranslatedText);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 18, 24, 18),
            Margin = Padding.Empty,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        Controls.Add(shell);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148F));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        shell.Controls.Add(header, 0, 0);

        var title = new Label
        {
            Name = "translationPreviewTitle",
            Text = "Bản dịch",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Variable Display", 19F, FontStyle.Bold),
            UseMnemonic = false,
        };
        header.Controls.Add(title, 0, 0);

        metadataLabel = new Label
        {
            Name = "translationPreviewMetadata",
            AccessibleName = "Thông tin bản dịch",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
            ForeColor = palette.TextSecondary,
        };
        header.Controls.Add(metadataLabel, 0, 1);

        var zoomActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        header.Controls.Add(zoomActions, 1, 0);
        header.SetRowSpan(zoomActions, 2);

        var closeButton = CreateButton(
            "closeTranslationPreview",
            "×",
            FluentButtonKind.Subtle,
            40,
            height: 32);
        closeButton.AccessibleName = "Đóng bản dịch";
        closeButton.AccessibleDescription = "Đóng trình đọc mà không thay văn bản gốc.";
        closeButton.Margin = Padding.Empty;
        closeButton.Click += (_, _) => CompleteOnce(cancel);

        var increaseZoomButton = CreateButton(
            "increaseTranslationZoom",
            "A+",
            FluentButtonKind.Subtle,
            44,
            height: 32);
        increaseZoomButton.AccessibleDescription = "Tăng cỡ chữ bản dịch.";
        increaseZoomButton.Margin = Padding.Empty;
        var decreaseZoomButton = CreateButton(
            "decreaseTranslationZoom",
            "A−",
            FluentButtonKind.Subtle,
            44,
            height: 32);
        decreaseZoomButton.AccessibleDescription = "Giảm cỡ chữ bản dịch.";
        decreaseZoomButton.Margin = new Padding(0, 0, 6, 0);
        zoomActions.Controls.Add(closeButton);
        zoomActions.Controls.Add(increaseZoomButton);
        zoomActions.Controls.Add(decreaseZoomButton);

        var readerCard = new FluentCard
        {
            Name = "translationPreviewReaderCard",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(18, 16, 12, 16),
            Palette = palette,
        };
        shell.Controls.Add(readerCard, 0, 1);

        translatedReader = new RichTextBox
        {
            Name = "translationPreviewTranslated",
            AccessibleName = "Bản dịch",
            AccessibleDescription =
                "Nội dung chỉ đọc, có thể chọn và sao chép. Keyina không ghi nội dung này vào log.",
            Text = preview.TranslatedText,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Regular),
            BorderStyle = BorderStyle.None,
            BackColor = palette.Surface,
            ForeColor = palette.TextPrimary,
            DetectUrls = false,
            HideSelection = false,
            ShortcutsEnabled = true,
        };
        translatedReader.SelectionStart = 0;
        translatedReader.SelectionLength = 0;
        readerCard.Controls.Add(translatedReader);

        increaseZoomButton.Click += (_, _) => AdjustReaderZoom(ReaderZoomStep);
        decreaseZoomButton.Click += (_, _) => AdjustReaderZoom(-ReaderZoomStep);
        translatedReader.MouseWheel += (_, eventArgs) =>
        {
            if ((ModifierKeys & Keys.Control) == 0)
            {
                return;
            }

            AdjustReaderZoom(eventArgs.Delta > 0 ? ReaderZoomStep : -ReaderZoomStep);
            if (eventArgs is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 11, 0, 0),
            Margin = Padding.Empty,
        };
        var copyButton = CreateButton(
            "copyTranslationPreview",
            "Sao chép",
            FluentButtonKind.Primary,
            112);
        copyButton.AccessibleDescription = "Sao chép toàn bộ bản dịch nhưng không thay văn bản gốc.";
        copyButton.Margin = Padding.Empty;
        copyButton.Click += (_, _) => copy(preview.TranslatedText);

        replaceButton = CreateButton(
            "replaceTranslationPreview",
            "Thay thế",
            FluentButtonKind.Secondary,
            112);
        replaceButton.AccessibleDescription =
            "Thay văn bản đã chọn bằng bản dịch khi vị trí nhập vẫn còn hợp lệ.";
        replaceButton.Margin = new Padding(0, 0, 8, 0);
        replaceButton.Click += (_, _) => CompleteOnce(() => replace(preview));

        var cancelButton = CreateButton(
            "cancelTranslationPreview",
            "Đóng",
            FluentButtonKind.Subtle,
            96);
        cancelButton.AccessibleDescription = "Đóng trình đọc mà không thay văn bản gốc.";
        cancelButton.Margin = new Padding(0, 0, 8, 0);
        cancelButton.Click += (_, _) => CompleteOnce(cancel);

        actions.Controls.Add(copyButton);
        actions.Controls.Add(replaceButton);
        actions.Controls.Add(cancelButton);
        shell.Controls.Add(actions, 0, 2);

        AcceptButton = copyButton;
        CancelButton = cancelButton;
        KeyDown += HandleShortcutKeyDown;
        FormClosing += (_, _) =>
        {
            previewTimer.Stop();
            if (!completed)
            {
                completed = true;
                cancel();
            }
        };
        previewTimer.Tick += (_, _) => UpdatePreviewLifetime();
        UpdatePreviewLifetime();
        previewTimer.Start();

        ApplyPaletteRecursive(this);
        Shown += (_, _) =>
        {
            PositionNearWorkingArea();
            translatedReader.SelectionStart = 0;
            translatedReader.SelectionLength = 0;
            translatedReader.ScrollToCaret();
            copyButton.Select();
        };
    }

    private static Size CalculateInitialSize(string text)
    {
        var normalizedLength = Math.Max(1, text?.Length ?? 0);
        var explicitLines = 1 + (text?.Count(character => character == '\n') ?? 0);
        var width = normalizedLength switch
        {
            > 1_800 => 760,
            > 700 => 700,
            _ => 600,
        };
        var charactersPerLine = width >= 700 ? 78 : 66;
        var estimatedLines = explicitLines +
            (int)Math.Ceiling(normalizedLength / (double)charactersPerLine);
        var height = Math.Clamp(250 + estimatedLines * 20, 340, 620);
        return new Size(width, height);
    }

    private void PositionNearWorkingArea()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(
            Math.Min(Width, Math.Max(MinimumSize.Width, area.Width - 40)),
            Math.Min(Height, Math.Max(MinimumSize.Height, area.Height - 40)));
        Location = new Point(
            Math.Max(area.Left, area.Right - Width - 20),
            Math.Max(area.Top, area.Bottom - Height - 20));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FluentWindow.Apply(this, palette);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            previewTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private FluentButton CreateButton(
        string name,
        string text,
        FluentButtonKind kind,
        int width,
        int height = 36) => new()
    {
        Name = name,
        Text = text,
        AccessibleName = text,
        Kind = kind,
        Palette = palette,
        Width = width,
        Height = height,
    };

    private void HandleShortcutKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            eventArgs.SuppressKeyPress = true;
            CompleteOnce(cancel);
            return;
        }

        if (!eventArgs.Control)
        {
            return;
        }

        if (eventArgs.KeyCode == Keys.Enter && replaceButton.Enabled)
        {
            eventArgs.SuppressKeyPress = true;
            CompleteOnce(() => replace(preview));
            return;
        }

        if (eventArgs.KeyCode is Keys.Add or Keys.Oemplus)
        {
            eventArgs.SuppressKeyPress = true;
            AdjustReaderZoom(ReaderZoomStep);
        }
        else if (eventArgs.KeyCode is Keys.Subtract or Keys.OemMinus)
        {
            eventArgs.SuppressKeyPress = true;
            AdjustReaderZoom(-ReaderZoomStep);
        }
        else if (eventArgs.KeyCode == Keys.D0)
        {
            eventArgs.SuppressKeyPress = true;
            translatedReader.ZoomFactor = 1F;
        }
    }

    private void AdjustReaderZoom(float delta)
    {
        translatedReader.ZoomFactor = Math.Clamp(
            translatedReader.ZoomFactor + delta,
            MinimumReaderZoom,
            MaximumReaderZoom);
    }

    private void UpdatePreviewLifetime()
    {
        var remaining = preview.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            replaceButton.Enabled = false;
            metadataLabel.Text =
                $"{preview.Provider} · phát hiện {preview.DetectedSourceLanguage} · đã hết hạn thay thế";
            previewTimer.Stop();
            return;
        }

        var remainingMinutes = Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes));
        metadataLabel.Text =
            $"{preview.Provider} · phát hiện {preview.DetectedSourceLanguage} · còn {remainingMinutes} phút";
    }

    private void CompleteOnce(Action action)
    {
        if (completed)
        {
            return;
        }
        completed = true;
        action();
        if (!IsDisposed)
        {
            Close();
        }
    }

    private void ApplyPaletteRecursive(Control control)
    {
        if (control == this)
        {
            control.BackColor = palette.Window;
            control.ForeColor = palette.TextPrimary;
        }
        else if (control is FluentCard card)
        {
            card.Palette = palette;
        }
        else if (control is RichTextBox richTextBox)
        {
            richTextBox.BackColor = palette.Surface;
            richTextBox.ForeColor = palette.TextPrimary;
        }
        else if (control is Label label)
        {
            label.BackColor = Color.Transparent;
            if (label == metadataLabel)
            {
                label.ForeColor = palette.TextSecondary;
            }
            else
            {
                label.ForeColor = palette.TextPrimary;
            }
        }
        foreach (Control child in control.Controls)
        {
            ApplyPaletteRecursive(child);
        }
    }
}
