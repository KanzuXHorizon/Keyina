using System.ComponentModel;

namespace Keyina.Host.UI.Fluent;

public static class FluentSpacing
{
    public const int Micro = 4;
    public const int Compact = 8;
    public const int Standard = 12;
    public const int Control = 16;
    public const int Section = 24;
    public const int Page = 32;
}

public static class FluentControlMetrics
{
    public const int CompactHeight = 32;
    public const int DefaultHeight = 36;
    public const int ProminentHeight = 40;
}

public static class FluentTypography
{
    public const float DisplaySize = 22F;
    public const float PageTitleSize = 18F;
    public const float SectionTitleSize = 11F;
    public const float BodySize = 9.5F;
    public const float CaptionSize = 8.5F;

    public static Font Create(float size, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI Variable Text", size, style, GraphicsUnit.Point);
}

public enum FluentInlineMessageKind
{
    Information,
    Success,
    Warning,
    Error,
}

public sealed class FluentSectionHeader : TableLayoutPanel
{
    private readonly Label titleLabel;
    private readonly Label descriptionLabel;

    public FluentSectionHeader()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        RowCount = 2;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, FluentSpacing.Standard);
        Padding = Padding.Empty;

        titleLabel = new Label
        {
            Name = "sectionHeaderTitle",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = FluentTypography.Create(FluentTypography.SectionTitleSize, FontStyle.Bold),
            Margin = Padding.Empty,
        };
        descriptionLabel = new Label
        {
            Name = "sectionHeaderDescription",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = FluentTypography.Create(FluentTypography.BodySize),
            Margin = new Padding(0, FluentSpacing.Micro, 0, 0),
        };

        Controls.Add(titleLabel, 0, 0);
        Controls.Add(descriptionLabel, 0, 1);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => titleLabel.Text;
        set
        {
            titleLabel.Text = value;
            AccessibleName = value;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Description
    {
        get => descriptionLabel.Text;
        set
        {
            descriptionLabel.Text = value;
            descriptionLabel.Visible = !string.IsNullOrWhiteSpace(value);
            AccessibleDescription = value;
        }
    }

    public void ApplyPalette(FluentThemePalette palette)
    {
        titleLabel.ForeColor = palette.TextPrimary;
        descriptionLabel.ForeColor = palette.TextSecondary;
        BackColor = Color.Transparent;
    }
}

public sealed class FluentSettingRow : TableLayoutPanel
{
    private readonly Label titleLabel;
    private readonly Label descriptionLabel;
    private Control? action;

    public FluentSettingRow()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        RowCount = 2;
        Dock = DockStyle.Top;
        Padding = new Padding(FluentSpacing.Control);
        Margin = new Padding(0, 0, 0, FluentSpacing.Compact);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        titleLabel = new Label
        {
            Name = "settingRowTitle",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = FluentTypography.Create(FluentTypography.BodySize, FontStyle.Bold),
            Margin = Padding.Empty,
        };
        descriptionLabel = new Label
        {
            Name = "settingRowDescription",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = FluentTypography.Create(FluentTypography.BodySize),
            Margin = new Padding(0, FluentSpacing.Micro, FluentSpacing.Standard, 0),
        };
        SetColumnSpan(descriptionLabel, 1);
        Controls.Add(titleLabel, 0, 0);
        Controls.Add(descriptionLabel, 0, 1);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => titleLabel.Text;
        set
        {
            titleLabel.Text = value;
            AccessibleName = value;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Description
    {
        get => descriptionLabel.Text;
        set
        {
            descriptionLabel.Text = value;
            descriptionLabel.Visible = !string.IsNullOrWhiteSpace(value);
            AccessibleDescription = value;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? Action
    {
        get => action;
        set
        {
            if (ReferenceEquals(action, value))
            {
                return;
            }
            if (action is not null)
            {
                Controls.Remove(action);
            }
            action = value;
            if (action is null)
            {
                return;
            }
            action.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            action.Margin = new Padding(FluentSpacing.Standard, 0, 0, 0);
            SetRowSpan(action, 2);
            Controls.Add(action, 1, 0);
        }
    }

    public void ApplyPalette(FluentThemePalette palette)
    {
        BackColor = palette.Surface;
        titleLabel.ForeColor = palette.TextPrimary;
        descriptionLabel.ForeColor = palette.TextSecondary;
    }
}

public sealed class FluentInlineMessage : Label
{
    private FluentThemePalette palette = FluentTheme.Current;
    private FluentInlineMessageKind kind;

    public FluentInlineMessage()
    {
        AutoSize = true;
        MaximumSize = new Size(760, 0);
        Padding = new Padding(FluentSpacing.Standard, FluentSpacing.Compact,
            FluentSpacing.Standard, FluentSpacing.Compact);
        Margin = new Padding(0, FluentSpacing.Compact, 0, 0);
        Font = FluentTypography.Create(FluentTypography.BodySize);
        AccessibleRole = AccessibleRole.StaticText;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentThemePalette Palette
    {
        get => palette;
        set
        {
            palette = value ?? throw new ArgumentNullException(nameof(value));
            ApplyColors();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentInlineMessageKind Kind
    {
        get => kind;
        set
        {
            kind = value;
            ApplyColors();
        }
    }

    public void SetMessage(string message, FluentInlineMessageKind messageKind)
    {
        Kind = messageKind;
        Text = message;
        AccessibleName = Prefix(messageKind) + ": " + message;
        Visible = !string.IsNullOrWhiteSpace(message);
    }

    private void ApplyColors()
    {
        ForeColor = kind switch
        {
            FluentInlineMessageKind.Success => palette.Success,
            FluentInlineMessageKind.Warning => palette.Warning,
            FluentInlineMessageKind.Error => palette.Error,
            _ => palette.TextSecondary,
        };
        BackColor = palette.SurfaceSecondary;
    }

    private static string Prefix(FluentInlineMessageKind messageKind) => messageKind switch
    {
        FluentInlineMessageKind.Success => "Thành công",
        FluentInlineMessageKind.Warning => "Cảnh báo",
        FluentInlineMessageKind.Error => "Lỗi",
        _ => "Thông tin",
    };
}
