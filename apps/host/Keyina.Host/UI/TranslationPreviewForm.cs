using Keyina.Host.Translation;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed class TranslationPreviewForm : Form
{
    private readonly TranslationPreview preview;
    private readonly Action<TranslationPreview> replace;
    private readonly Action<string> copy;
    private readonly Action cancel;
    private readonly FluentThemePalette palette = FluentTheme.Current;
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
        AccessibleName = "Overlay bản dịch Keyina";
        AccessibleDescription =
            "Hiển thị bản dịch mà không thay đổi văn bản đang chọn. Có thể sao chép hoặc đóng overlay.";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(420, 260);
        Size = new Size(560, 360);
        FormBorderStyle = FormBorderStyle.FixedSingle;
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
            RowCount = 4,
            Padding = new Padding(26, 22, 26, 20),
            Margin = Padding.Empty,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        Controls.Add(shell);

        var title = new Label
        {
            Name = "translationPreviewTitle",
            Text = "Bản dịch",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Variable Display", 20F, FontStyle.Bold),
            UseMnemonic = false,
        };
        shell.Controls.Add(title, 0, 0);

        var metadata = new Label
        {
            Name = "translationPreviewMetadata",
            Text = $"{preview.Provider} · nguồn {preview.DetectedSourceLanguage} · tự hủy sau thời gian ngắn",
            AccessibleName = "Thông tin provider bản dịch",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
        };
        shell.Controls.Add(metadata, 0, 1);

        var comparison = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        comparison.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        comparison.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.Controls.Add(comparison, 0, 2);

        comparison.Controls.Add(CreateHeading("translationPreviewTranslatedTitle", "Nội dung đã dịch"), 0, 0);
        comparison.Controls.Add(CreatePreviewTextBox(
            "translationPreviewTranslated",
            "Bản dịch",
            preview.TranslatedText), 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 9, 0, 0),
            Margin = Padding.Empty,
        };
        var copyButton = CreateButton(
            "copyTranslationPreview",
            "Sao chép",
            FluentButtonKind.Primary,
            112);
        copyButton.AccessibleDescription = "Sao chép bản dịch nhưng không thay văn bản gốc.";
        copyButton.Margin = new Padding(0, 0, 8, 0);
        copyButton.Click += (_, _) => copy(preview.TranslatedText);
        var cancelButton = CreateButton(
            "cancelTranslationPreview",
            "Hủy",
            FluentButtonKind.Subtle,
            96);
        cancelButton.AccessibleDescription = "Đóng bản xem trước mà không chèn nội dung.";
        cancelButton.Margin = new Padding(0, 0, 8, 0);
        cancelButton.Click += (_, _) => CompleteOnce(cancel);
        actions.Controls.Add(copyButton);
        actions.Controls.Add(cancelButton);
        shell.Controls.Add(actions, 0, 3);

        AcceptButton = copyButton;
        CancelButton = cancelButton;
        FormClosing += (_, _) =>
        {
            if (!completed)
            {
                completed = true;
                cancel();
            }
        };
        ApplyPaletteRecursive(this);
        Shown += (_, _) => PositionNearWorkingArea();
    }

    private void PositionNearWorkingArea()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            Math.Max(area.Left, area.Right - Width - 20),
            Math.Max(area.Top, area.Bottom - Height - 20));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FluentWindow.Apply(this, palette);
    }

    private Label CreateHeading(string name, string text) => new()
    {
        Name = name,
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
        UseMnemonic = false,
        Padding = new Padding(4, 0, 4, 0),
    };

    private TextBox CreatePreviewTextBox(
        string name,
        string accessibleName,
        string text) => new()
    {
        Name = name,
        AccessibleName = accessibleName,
        AccessibleDescription = "Nội dung chỉ đọc; Keyina không ghi nội dung này vào log.",
        Text = text,
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        Dock = DockStyle.Fill,
        Margin = new Padding(4, 2, 8, 4),
        Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
        BorderStyle = BorderStyle.FixedSingle,
    };

    private FluentButton CreateButton(
        string name,
        string text,
        FluentButtonKind kind,
        int width) => new()
    {
        Name = name,
        Text = text,
        AccessibleName = text,
        Kind = kind,
        Palette = palette,
        Width = width,
        Height = 36,
    };

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
        else if (control is TextBox textBox)
        {
            textBox.BackColor = palette.Surface;
            textBox.ForeColor = palette.TextPrimary;
        }
        else if (control is Label label)
        {
            label.BackColor = Color.Transparent;
            label.ForeColor = palette.TextPrimary;
        }
        foreach (Control child in control.Controls)
        {
            ApplyPaletteRecursive(child);
        }
    }
}
