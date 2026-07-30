using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed class FirstRunForm : Form
{
    private readonly Action<string> openSection;
    private readonly Action complete;
    private readonly FluentThemePalette palette = FluentTheme.Current;
    private bool completed;

    public FirstRunForm(
        Action<string> openSection,
        Action complete)
    {
        this.openSection = openSection ?? throw new ArgumentNullException(nameof(openSection));
        this.complete = complete ?? throw new ArgumentNullException(nameof(complete));

        Text = "Bắt đầu với Keyina";
        AccessibleName = "Thiết lập ban đầu Keyina";
        AccessibleDescription =
            "Các bước này không bắt buộc. Bộ gõ tiếng Việt có thể dùng ngay và các tính năng tùy chọn có thể thiết lập sau.";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(720, 570);
        MinimumSize = new Size(680, 540);
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(30, 26, 30, 24),
            Margin = Padding.Empty,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(shell);

        var title = CreateLabel(
            "firstRunTitle",
            "Keyina đã sẵn sàng để gõ tiếng Việt",
            22F,
            FontStyle.Bold);
        shell.Controls.Add(title, 0, 0);

        var subtitle = CreateLabel(
            "firstRunSubtitle",
            "Ba bước dưới đây là tùy chọn. Bạn có thể bỏ qua và quay lại bất kỳ lúc nào trong Cài đặt.",
            10F,
            FontStyle.Regular);
        subtitle.ForeColor = palette.TextSecondary;
        shell.Controls.Add(subtitle, 0, 1);

        shell.Controls.Add(CreateSetupCard(
            "firstRunTyping",
            "\uE765",
            "Kiểm tra bộ gõ",
            "Gõ thử Telex vào ô thật để xác nhận hook, focus và đường chèn ký tự đang hoạt động.",
            "typing"), 0, 2);
        shell.Controls.Add(CreateSetupCard(
            "firstRunSpeech",
            "\uE720",
            "Thiết lập nhập giọng nói",
            "Thêm khóa Speechmatics nếu cần đọc tiếng Việt vào ứng dụng đang focus.",
            "speech"), 0, 3);
        shell.Controls.Add(CreateSetupCard(
            "firstRunTranslation",
            "\uE8C1",
            "Thiết lập dịch nhanh",
            "Thêm khóa DeepL, chọn ngôn ngữ đích và dùng phím tắt trên văn bản đang chọn.",
            "translation"), 0, 4);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            Margin = Padding.Empty,
        };
        var completeButton = CreateButton(
            "completeFirstRun",
            "Hoàn tất",
            FluentButtonKind.Primary,
            118);
        completeButton.AccessibleDescription =
            "Đánh dấu thiết lập ban đầu đã hoàn tất.";
        completeButton.Click += (_, _) => CompleteOnce();
        var skipButton = CreateButton(
            "skipFirstRun",
            "Bỏ qua lúc này",
            FluentButtonKind.Secondary,
            136);
        skipButton.AccessibleDescription =
            "Đóng hướng dẫn và sử dụng bộ gõ ngay; có thể thiết lập tính năng tùy chọn sau.";
        skipButton.Margin = new Padding(0, 0, 8, 0);
        skipButton.Click += (_, _) => CompleteOnce();
        actions.Controls.Add(completeButton);
        actions.Controls.Add(skipButton);
        shell.Controls.Add(actions, 0, 5);

        AcceptButton = completeButton;
        ApplyPaletteRecursive(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FluentWindow.Apply(this, palette);
    }

    private FluentCard CreateSetupCard(
        string name,
        string glyph,
        string title,
        string description,
        string section)
    {
        var card = new FluentCard
        {
            Name = name + "Card",
            Dock = DockStyle.Fill,
            Height = 102,
            Margin = new Padding(0, 0, 0, 10),
            Palette = palette,
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);

        var icon = CreateLabel(name + "Icon", glyph, 15F, FontStyle.Regular);
        icon.Font = new Font("Segoe Fluent Icons", 15F, FontStyle.Regular);
        icon.TextAlign = ContentAlignment.MiddleCenter;
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);

        var titleLabel = CreateLabel(name + "Title", title, 10.5F, FontStyle.Bold);
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(titleLabel, 1, 0);
        var descriptionLabel = CreateLabel(
            name + "Description",
            description,
            9.5F,
            FontStyle.Regular);
        descriptionLabel.ForeColor = palette.TextSecondary;
        descriptionLabel.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(descriptionLabel, 1, 1);

        var button = CreateButton(
            name,
            "Mở thiết lập",
            FluentButtonKind.Secondary,
            108);
        button.AccessibleName = $"Mở thiết lập {title}";
        button.Anchor = AnchorStyles.None;
        button.Click += (_, _) => openSection(section);
        layout.SetRowSpan(button, 2);
        layout.Controls.Add(button, 2, 0);
        return card;
    }

    private void CompleteOnce()
    {
        if (completed)
        {
            return;
        }
        completed = true;
        complete();
        DialogResult = DialogResult.OK;
        if (Visible)
        {
            Close();
        }
    }

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

    private Label CreateLabel(
        string name,
        string text,
        float size,
        FontStyle style) => new()
    {
        Name = name,
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        Font = new Font(Font.FontFamily, size, style),
        UseMnemonic = false,
        BackColor = Color.Transparent,
        ForeColor = palette.TextPrimary,
    };

    private void ApplyPaletteRecursive(Control control)
    {
        control.BackColor = control == this
            ? palette.Window
            : Color.Transparent;
        if (control is Label label && label.ForeColor == SystemColors.ControlText)
        {
            label.ForeColor = palette.TextPrimary;
        }
        foreach (Control child in control.Controls)
        {
            ApplyPaletteRecursive(child);
        }
    }
}
