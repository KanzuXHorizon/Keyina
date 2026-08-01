using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed class FirstRunForm : Form
{
    private enum FirstRunTypographyRole
    {
        Display,
        SectionTitle,
        Body,
        SecondaryBody,
        Caption,
    }

    private readonly Action<string> openSection;
    private readonly Action complete;
    private readonly FluentThemePalette palette = FluentTheme.Current;
    private bool completed;

    public FirstRunForm(Action<string> openSection, Action complete)
    {
        this.openSection = openSection ?? throw new ArgumentNullException(nameof(openSection));
        this.complete = complete ?? throw new ArgumentNullException(nameof(complete));

        Text = "Bắt đầu với Keyina";
        AccessibleName = "Thiết lập ban đầu Keyina";
        AccessibleDescription = "Xác nhận bộ gõ và thiết lập các tính năng không bắt buộc.";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(760, 650);
        MinimumSize = new Size(720, 610);
        Font = FluentTypography.Create(FluentTypography.BodySize);

        var shell = new TableLayoutPanel
        {
            Name = "firstRunShell",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(FluentSpacing.Page, FluentSpacing.Section, FluentSpacing.Page, FluentSpacing.Section),
            Margin = Padding.Empty,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 154F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(shell);

        var title = CreateLabel("firstRunTitle", "Thiết lập Keyina trong vài phút", FirstRunTypographyRole.Display);
        shell.Controls.Add(title, 0, 0);

        var subtitle = CreateLabel(
            "firstRunSubtitle",
            "Bộ gõ hoạt động ngay. Hãy xác nhận bước đầu tiên; giọng nói và dịch nhanh là tùy chọn.",
            FirstRunTypographyRole.SecondaryBody);
        subtitle.ForeColor = palette.TextSecondary;
        shell.Controls.Add(subtitle, 0, 1);

        shell.Controls.Add(CreateTypingChecklistItem(), 0, 2);
        shell.Controls.Add(CreateChecklistItem(
            "firstRunSpeech",
            "\uE720",
            "Nhập bằng giọng nói",
            "Tùy chọn",
            "Thêm khóa Speechmatics để đọc văn bản vào ứng dụng đang focus.",
            "speech"), 0, 3);
        shell.Controls.Add(CreateChecklistItem(
            "firstRunTranslation",
            "\uE8C1",
            "Dịch nhanh",
            "Tùy chọn",
            "Thêm khóa DeepL và chọn ngôn ngữ đích cho văn bản đang chọn.",
            "translation"), 0, 4);

        var actions = new FlowLayoutPanel
        {
            Name = "firstRunActions",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, FluentSpacing.Section, 0, 0),
            Margin = Padding.Empty,
        };
        var completeButton = CreateButton("completeFirstRun", "Hoàn tất thiết lập", FluentButtonKind.Primary, 154);
        completeButton.Height = FluentControlMetrics.ProminentHeight;
        completeButton.Click += (_, _) => CompleteOnce();
        var skipButton = CreateButton("skipFirstRun", "Bỏ qua lúc này", FluentButtonKind.Subtle, 136);
        skipButton.Margin = new Padding(0, 0, FluentSpacing.Compact, 0);
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

    private FluentCard CreateTypingChecklistItem()
    {
        var card = CreateChecklistCard("firstRunTypingCard");
        var layout = CreateChecklistLayout();
        card.Controls.Add(layout);

        AddChecklistIdentity(layout, "firstRunTyping", "\uE765", "Xác nhận bộ gõ", "Bắt buộc", "Gõ thử Telex bên dưới. Văn bản chỉ ở trong ô kiểm tra này.");

        var input = new TextBox
        {
            Name = "firstRunTypingInput",
            AccessibleName = "Ô gõ thử tiếng Việt",
            AccessibleDescription = "Gõ thử ví dụ tieengs Vieetj để xác nhận bộ gõ hoạt động.",
            PlaceholderText = "Gõ thử: tieengs Vieetj",
            Dock = DockStyle.Fill,
            Height = FluentControlMetrics.DefaultHeight,
            Margin = new Padding(0, FluentSpacing.Compact, FluentSpacing.Standard, 0),
        };
        layout.Controls.Add(input, 1, 2);
        var state = CreateLabel("firstRunTypingState", "Chưa xác nhận", FirstRunTypographyRole.Caption);
        state.ForeColor = palette.Warning;
        state.TextAlign = ContentAlignment.MiddleRight;
        input.TextChanged += (_, _) =>
        {
            var ready = !string.IsNullOrWhiteSpace(input.Text);
            state.Text = ready ? "Đã gõ thử" : "Chưa xác nhận";
            state.ForeColor = ready ? palette.Success : palette.Warning;
            state.AccessibleDescription = ready ? "Bước kiểm tra bộ gõ đã hoàn thành." : "Bước kiểm tra bộ gõ chưa hoàn thành.";
        };
        layout.Controls.Add(state, 2, 2);
        return card;
    }

    private FluentCard CreateChecklistItem(string name, string glyph, string title, string badge, string description, string section)
    {
        var card = CreateChecklistCard(name + "Card");
        var layout = CreateChecklistLayout();
        card.Controls.Add(layout);
        AddChecklistIdentity(layout, name, glyph, title, badge, description);
        var button = CreateButton(name, "Mở thiết lập", FluentButtonKind.Secondary, 116);
        button.AccessibleName = $"Mở thiết lập {title}";
        button.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        button.Click += (_, _) => openSection(section);
        layout.SetRowSpan(button, 2);
        layout.Controls.Add(button, 2, 0);
        return card;
    }

    private FluentCard CreateChecklistCard(string name) => new()
    {
        Name = name,
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 0, 0, FluentSpacing.Standard),
        Padding = new Padding(FluentSpacing.Standard),
        Palette = palette,
    };

    private static TableLayoutPanel CreateChecklistLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        return layout;
    }

    private void AddChecklistIdentity(TableLayoutPanel layout, string name, string glyph, string title, string badge, string description)
    {
        var icon = CreateLabel(name + "Icon", glyph, FirstRunTypographyRole.SectionTitle);
        icon.Font = new Font("Segoe Fluent Icons", 15F, FontStyle.Regular);
        icon.TextAlign = ContentAlignment.MiddleCenter;
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);

        var heading = CreateLabel(name + "Title", title, FirstRunTypographyRole.SectionTitle);
        heading.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(heading, 1, 0);

        var badgeLabel = CreateLabel(name + "Badge", badge, FirstRunTypographyRole.Caption);
        badgeLabel.ForeColor = badge == "Bắt buộc" ? palette.Accent : palette.TextSecondary;
        badgeLabel.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(badgeLabel, 2, 0);

        var body = CreateLabel(name + "Description", description, FirstRunTypographyRole.SecondaryBody);
        body.ForeColor = palette.TextSecondary;
        body.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(body, 1, 1);
    }

    private void CompleteOnce()
    {
        if (completed) return;
        completed = true;
        complete();
        DialogResult = DialogResult.OK;
        if (Visible) Close();
    }

    private FluentButton CreateButton(string name, string text, FluentButtonKind kind, int width) => new()
    {
        Name = name,
        Text = text,
        AccessibleName = text,
        Kind = kind,
        Palette = palette,
        Width = width,
        Height = FluentControlMetrics.DefaultHeight,
    };

    private static Label CreateLabel(string name, string text, FirstRunTypographyRole role) => new()
    {
        Name = name,
        Text = text,
        AccessibleName = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        UseMnemonic = false,
        Font = role switch
        {
            FirstRunTypographyRole.Display => FluentTypography.Create(FluentTypography.DisplaySize, FontStyle.Bold),
            FirstRunTypographyRole.SectionTitle => FluentTypography.Create(FluentTypography.SectionTitleSize, FontStyle.Bold),
            FirstRunTypographyRole.Caption => FluentTypography.Create(FluentTypography.CaptionSize),
            _ => FluentTypography.Create(FluentTypography.BodySize),
        },
        BackColor = Color.Transparent,
    };

    private void ApplyPaletteRecursive(Control root)
    {
        root.BackColor = root is FluentCard ? Color.Transparent : palette.Window;
        root.ForeColor = palette.TextPrimary;
        foreach (Control child in root.Controls) ApplyPaletteRecursive(child);
    }
}
