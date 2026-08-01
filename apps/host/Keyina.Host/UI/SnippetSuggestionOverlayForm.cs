using System.Runtime.InteropServices;
using Keyina.Host.Core.Snippets;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed partial class SnippetSuggestionOverlayForm : Form
{
    public const int MaximumVisibleSuggestions = 6;

    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private readonly FlowLayoutPanel rows;
    private readonly Label prefixLabel;
    private readonly FluentThemePalette palette = FluentTheme.Current;

    public SnippetSuggestionOverlayForm()
    {
        Text = "Gợi ý gõ tắt";
        AccessibleName = "Gợi ý gõ tắt Keyina";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 260);
        BackColor = palette.Window;
        Padding = new Padding(1);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = palette.Surface,
            Padding = new Padding(12),
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        prefixLabel = new Label
        {
            Name = "snippetSuggestionPrefix",
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = palette.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        shell.Controls.Add(prefixLabel, 0, 0);
        rows = new FlowLayoutPanel
        {
            Name = "snippetSuggestionRows",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = Padding.Empty,
        };
        shell.Controls.Add(rows, 0, 1);
    }

    public bool UsesNoActivateStyle => (CreateParams.ExStyle & WsExNoActivate) != 0;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTransparent;
            return parameters;
        }
    }

    public void Present(string prefix, IReadOnlyList<SnippetDefinition> suggestions)
    {
        if (suggestions.Count == 0)
        {
            HideOverlay();
            return;
        }

        var normalizedPrefix = prefix?.Trim() ?? string.Empty;
        prefixLabel.Text = $"Gõ tắt khớp với {normalizedPrefix}";
        prefixLabel.AccessibleDescription = $"Có {suggestions.Count} gợi ý cho tiền tố {normalizedPrefix}.";
        rows.SuspendLayout();
        rows.Controls.Clear();
        var visibleSuggestions = suggestions.Take(MaximumVisibleSuggestions).ToArray();
        for (var index = 0; index < visibleSuggestions.Length; index++)
        {
            var suggestion = visibleSuggestions[index];
            var selected = index == 0;
            var row = new TableLayoutPanel
            {
                Name = $"snippetSuggestionRow{index}",
                AccessibleName = $"Gợi ý {index + 1}: {suggestion.Trigger}",
                AccessibleDescription = selected
                    ? "Gợi ý đang được chọn. Nhấn Tab để chèn."
                    : "Gợi ý gõ tắt.",
                Width = 372,
                Height = 38,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4),
                BackColor = selected ? palette.SurfacePressed : palette.SurfaceSecondary,
                Padding = new Padding(8, 0, 8, 0),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label
            {
                Name = $"snippetSuggestionTrigger{index}",
                AccessibleName = $"Phần khớp {suggestion.Trigger}",
                Text = suggestion.Trigger,
                Dock = DockStyle.Fill,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = palette.Accent,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);
            row.Controls.Add(new Label
            {
                Name = $"snippetSuggestionExpansion{index}",
                AccessibleName = "Nội dung mở rộng",
                Text = suggestion.Command == SnippetCommand.None
                    ? suggestion.Expansion
                    : DescribeCommand(suggestion.Command),
                Dock = DockStyle.Fill,
                ForeColor = palette.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            }, 1, 0);
            rows.Controls.Add(row);
        }
        rows.ResumeLayout();

        var desiredHeight = 62 + visibleSuggestions.Length * 42;
        Height = Math.Clamp(desiredHeight, 104, 314);
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(
            Math.Min(Width, Math.Max(320, screen.Width - 32)),
            Math.Min(Height, Math.Max(104, screen.Height - 32)));
        Location = new Point(
            Math.Max(screen.Left + 8, screen.Right - Width - 20),
            Math.Max(screen.Top + 8, screen.Bottom - Height - 20));
        if (!Visible)
        {
            Show();
        }
        NativeMethods.SetWindowPos(
            Handle,
            new nint(-1),
            Left,
            Top,
            Width,
            Height,
            0x0010 | 0x0004);
    }

    public void HideOverlay()
    {
        if (Visible)
        {
            Hide();
        }
    }

    private static string DescribeCommand(SnippetCommand command) => command switch
    {
        SnippetCommand.ToggleVietnamese => "Bật hoặc tắt bộ gõ tiếng Việt",
        SnippetCommand.ToggleDictation => "Bắt đầu hoặc dừng nhập giọng nói",
        SnippetCommand.ExternalOutput => "Chạy chương trình và chèn stdout",
        _ => "Lệnh Keyina",
    };

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(
            nint window,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
