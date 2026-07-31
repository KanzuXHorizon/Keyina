using Keyina.Host.Core.Configuration;
using Keyina.Host.Runtime;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed class SnippetEditorDialog : Form
{
    private readonly TextBox trigger;
    private readonly ComboBox kind;
    private readonly Panel textPanel;
    private readonly Panel commandPanel;
    private readonly Label validation;
    private readonly FluentButton save;
    private readonly IReadOnlyCollection<string> existingTriggers;
    private readonly string? originalTrigger;

    public SnippetEditorDialog(
        SnippetConfiguration? initial,
        IReadOnlyCollection<string> existingTriggers)
    {
        this.existingTriggers = existingTriggers ?? throw new ArgumentNullException(nameof(existingTriggers));
        originalTrigger = initial?.Trigger;
        Text = initial is null ? "Thêm gõ tắt" : "Sửa gõ tắt";
        AccessibleName = Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(720, 650);
        ClientSize = new Size(760, 700);
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI Variable Text", 9.5F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(24),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        root.Controls.Add(CreateFieldLabel("Trigger — bắt đầu bằng ;k"), 0, 0);
        trigger = new TextBox
        {
            Name = "snippetTrigger",
            Dock = DockStyle.Fill,
            Text = initial?.Trigger ?? ";k",
            AccessibleName = "Trigger gõ tắt",
        };
        root.Controls.Add(trigger, 0, 1);

        root.Controls.Add(CreateFieldLabel("Loại gõ tắt"), 0, 2);
        kind = new ComboBox
        {
            Name = "snippetKind",
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Loại gõ tắt",
        };
        kind.Items.AddRange(["Văn bản và biến động", "Đầu ra chương trình hoặc PowerShell"]);
        kind.SelectedIndex = initial?.Execution is null ? 0 : 1;
        kind.SelectedIndexChanged += (_, _) => UpdateKindVisibility();
        root.Controls.Add(kind, 0, 3);

        var contentHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        textPanel = CreateTextPanel(initial);
        commandPanel = CreateCommandPanel(initial);
        contentHost.Controls.Add(commandPanel);
        contentHost.Controls.Add(textPanel);
        root.Controls.Add(contentHost, 0, 4);

        validation = new Label
        {
            Name = "snippetValidation",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Current.Error,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        root.Controls.Add(validation, 0, 5);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        save = CreateActionButton("saveSnippet", "Lưu", FluentButtonKind.Primary, 104);
        var cancel = CreateActionButton("cancelSnippet", "Hủy", FluentButtonKind.Secondary, 104);
        cancel.DialogResult = DialogResult.Cancel;
        save.Click += (_, _) => SaveAndClose();
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 6);
        AcceptButton = save;
        CancelButton = cancel;
        UpdateKindVisibility();
    }

    public SnippetConfiguration? Result { get; private set; }

    private TextBox expansion = null!;
    private CheckBox caseSensitive = null!;
    private CheckBox preserveDelimiter = null!;
    private TextBox executablePath = null!;
    private TextBox arguments = null!;
    private TextBox workingDirectory = null!;
    private NumericUpDown timeout = null!;
    private TextBox previewOutput = null!;

    private Panel CreateTextPanel(SnippetConfiguration? initial)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.Controls.Add(layout);

        layout.Controls.Add(CreateFieldLabel("Nội dung mở rộng"), 0, 0);
        expansion = new TextBox
        {
            Name = "snippetExpansion",
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Text = initial?.Execution is null ? initial?.Expansion ?? string.Empty : string.Empty,
            AccessibleName = "Nội dung mở rộng",
        };
        layout.Controls.Add(expansion, 0, 1);

        var variables = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        foreach (var variable in new[] { "${date}", "${time}", "${datetime}" })
        {
            var button = CreateActionButton("insert" + variable, variable, FluentButtonKind.Secondary, 116);
            button.Click += (_, _) => expansion.SelectedText = variable;
            variables.Controls.Add(button);
        }
        layout.Controls.Add(variables, 0, 2);

        caseSensitive = new CheckBox
        {
            Name = "snippetCaseSensitive",
            Text = "Phân biệt chữ hoa và chữ thường",
            Checked = initial?.CaseSensitive ?? false,
            Dock = DockStyle.Fill,
        };
        preserveDelimiter = new CheckBox
        {
            Name = "snippetPreserveDelimiter",
            Text = "Giữ phím Space sau khi mở rộng",
            Checked = initial?.Execution is null && (initial?.PreserveDelimiter ?? false),
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(caseSensitive, 0, 3);
        layout.Controls.Add(preserveDelimiter, 0, 4);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Biến được tính ngay lúc chèn. Trường mật khẩu và ứng dụng bị loại trừ luôn được bỏ qua.",
            ForeColor = FluentTheme.Current.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 5);
        return panel;
    }

    private Panel CreateCommandPanel(SnippetConfiguration? initial)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 10,
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(layout);

        layout.Controls.Add(CreateFieldLabel("Đường dẫn chương trình (.exe)"), 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 3);
        executablePath = new TextBox
        {
            Name = "snippetExecutablePath",
            Dock = DockStyle.Fill,
            Text = initial?.Execution?.ExecutablePath ?? string.Empty,
            AccessibleName = "Đường dẫn chương trình",
        };
        layout.Controls.Add(executablePath, 0, 1);
        var browseExe = CreateActionButton("browseSnippetExecutable", "Duyệt file", FluentButtonKind.Secondary, 104);
        browseExe.Click += (_, _) => BrowseExecutable();
        layout.Controls.Add(browseExe, 1, 1);
        var powershell = CreateActionButton("usePowerShell", "PowerShell", FluentButtonKind.Secondary, 104);
        powershell.Click += (_, _) => UsePowerShellPreset();
        layout.Controls.Add(powershell, 2, 1);

        layout.Controls.Add(CreateFieldLabel("Đối số dòng lệnh"), 0, 2);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 2)!, 3);
        arguments = new TextBox
        {
            Name = "snippetArguments",
            Dock = DockStyle.Fill,
            Text = initial?.Execution?.Arguments ?? string.Empty,
            AccessibleName = "Đối số dòng lệnh",
        };
        layout.Controls.Add(arguments, 0, 3);
        layout.SetColumnSpan(arguments, 3);

        layout.Controls.Add(CreateFieldLabel("Thư mục làm việc — có thể để trống"), 0, 4);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 4)!, 3);
        workingDirectory = new TextBox
        {
            Name = "snippetWorkingDirectory",
            Dock = DockStyle.Fill,
            Text = initial?.Execution?.WorkingDirectory ?? string.Empty,
            AccessibleName = "Thư mục làm việc",
        };
        layout.Controls.Add(workingDirectory, 0, 5);
        layout.SetColumnSpan(workingDirectory, 2);
        var browseFolder = CreateActionButton("browseSnippetWorkingDirectory", "Duyệt thư mục", FluentButtonKind.Secondary, 104);
        browseFolder.Click += (_, _) => BrowseWorkingDirectory();
        layout.Controls.Add(browseFolder, 2, 5);

        layout.Controls.Add(CreateFieldLabel("Thời gian chờ tối đa"), 0, 6);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 6)!, 3);
        timeout = new NumericUpDown
        {
            Name = "snippetTimeout",
            Dock = DockStyle.Left,
            Width = 180,
            Minimum = SnippetExecutionConfiguration.MinimumTimeoutMilliseconds,
            Maximum = SnippetExecutionConfiguration.MaximumTimeoutMilliseconds,
            Increment = 250,
            Value = initial?.Execution?.TimeoutMilliseconds ?? 3_000,
            ThousandsSeparator = true,
        };
        layout.Controls.Add(timeout, 0, 7);
        var timeoutUnit = new Label
        {
            Dock = DockStyle.Fill,
            Text = "ms",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(timeoutUnit, 1, 7);

        var preview = CreateActionButton("previewSnippetCommand", "Chạy thử", FluentButtonKind.Primary, 104);
        preview.Click += async (_, _) => await PreviewCommandAsync(preview).ConfigureAwait(true);
        layout.Controls.Add(preview, 2, 7);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Chỉ stdout được chèn. Không chạy quyền quản trị, không mở cửa sổ console và tự hủy khi quá thời gian.",
            ForeColor = FluentTheme.Current.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 8);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 8)!, 3);
        previewOutput = new TextBox
        {
            Name = "snippetCommandPreview",
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Kết quả chạy thử sẽ xuất hiện ở đây.",
        };
        layout.Controls.Add(previewOutput, 0, 9);
        layout.SetColumnSpan(previewOutput, 3);
        return panel;
    }

    private void UpdateKindVisibility()
    {
        var isCommand = kind.SelectedIndex == 1;
        commandPanel.Visible = isCommand;
        textPanel.Visible = !isCommand;
        commandPanel.BringToFront();
        textPanel.BringToFront();
        validation.Text = string.Empty;
    }

    private void SaveAndClose()
    {
        var normalizedTrigger = trigger.Text.Trim();
        if (!normalizedTrigger.StartsWith(";k", StringComparison.OrdinalIgnoreCase) ||
            normalizedTrigger.Length < 3 ||
            normalizedTrigger.Any(char.IsWhiteSpace))
        {
            SetValidation("Trigger phải bắt đầu bằng ;k và không chứa khoảng trắng.", trigger);
            return;
        }
        if (!string.Equals(normalizedTrigger, originalTrigger, StringComparison.OrdinalIgnoreCase) &&
            existingTriggers.Contains(normalizedTrigger, StringComparer.OrdinalIgnoreCase))
        {
            SetValidation("Trigger này đã tồn tại.", trigger);
            return;
        }

        SnippetExecutionConfiguration? execution = null;
        var normalizedExpansion = expansion.Text.Trim();
        if (kind.SelectedIndex == 0)
        {
            if (normalizedExpansion.Length == 0)
            {
                SetValidation("Nội dung mở rộng không được để trống.", expansion);
                return;
            }
        }
        else
        {
            execution = CreateExecutionConfiguration();
            if (execution is null)
            {
                return;
            }
            normalizedExpansion = string.Empty;
        }

        Result = new SnippetConfiguration(
            normalizedTrigger,
            normalizedExpansion,
            caseSensitive.Checked,
            kind.SelectedIndex == 0 && preserveDelimiter.Checked,
            " ",
            [],
            [],
            execution);
        DialogResult = DialogResult.OK;
        Close();
    }

    private SnippetExecutionConfiguration? CreateExecutionConfiguration()
    {
        var candidate = new SnippetExecutionConfiguration(
            executablePath.Text.Trim(),
            arguments.Text,
            workingDirectory.Text.Trim(),
            checked((int)timeout.Value));
        try
        {
            candidate.Validate();
        }
        catch (ArgumentException exception)
        {
            SetValidation(LocalizeExecutionError(exception), executablePath);
            return null;
        }
        return candidate;
    }

    private async Task PreviewCommandAsync(Control previewButton)
    {
        var execution = CreateExecutionConfiguration();
        if (execution is null)
        {
            return;
        }
        previewButton.Enabled = false;
        previewOutput.Text = "Đang chạy thử…";
        try
        {
            var result = await new SnippetCommandOutputRunner()
                .CaptureAsync(execution, CancellationToken.None)
                .ConfigureAwait(true);
            previewOutput.Text = result.Success
                ? result.Output
                : LocalizeRunnerCode(result.Code);
        }
        catch (Exception)
        {
            previewOutput.Text = "Không thể chạy thử chương trình.";
        }
        finally
        {
            previewButton.Enabled = true;
        }
    }

    private void BrowseExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn chương trình thực thi",
            Filter = "Ứng dụng Windows (*.exe)|*.exe|Tất cả tệp (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            executablePath.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(workingDirectory.Text))
            {
                workingDirectory.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }

    private void BrowseWorkingDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục làm việc cho lệnh",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            workingDirectory.Text = dialog.SelectedPath;
        }
    }

    private void UsePowerShellPreset()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        executablePath.Text = Path.Combine(
            systemRoot,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        arguments.Text = "-NoLogo -NoProfile -NonInteractive -Command \"Get-Date -Format 'yyyy-MM-dd HH:mm:ss'\"";
        workingDirectory.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        timeout.Value = 3_000;
        previewOutput.Text = "Mẫu PowerShell đã được điền. Nhấn Chạy thử để kiểm tra.";
    }

    private void SetValidation(string message, Control focus)
    {
        validation.Text = message;
        focus.Focus();
    }

    private static string LocalizeExecutionError(ArgumentException exception)
    {
        if (exception.ParamName == nameof(SnippetExecutionConfiguration.ExecutablePath))
        {
            return "Chọn đường dẫn tuyệt đối đến một tệp .exe hợp lệ.";
        }
        if (exception.ParamName == nameof(SnippetExecutionConfiguration.WorkingDirectory))
        {
            return "Thư mục làm việc phải là đường dẫn tuyệt đối hoặc để trống.";
        }
        return "Cấu hình chương trình chưa hợp lệ.";
    }

    private static string LocalizeRunnerCode(string code) => code switch
    {
        "snippet_executable_missing" => "Không tìm thấy chương trình tại đường dẫn đã chọn.",
        "snippet_working_directory_missing" => "Không tìm thấy thư mục làm việc.",
        "snippet_process_start_failed" => "Windows không thể khởi chạy chương trình.",
        "snippet_process_timeout" => "Lệnh chạy quá thời gian cho phép và đã bị dừng.",
        "snippet_process_failed" => "Chương trình kết thúc với mã lỗi.",
        "snippet_output_empty" => "Chương trình không trả về stdout.",
        "snippet_output_too_large" => "Đầu ra vượt giới hạn 16 KiB.",
        _ => "Không thể lấy đầu ra chương trình.",
    };

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = FluentTheme.Current.TextPrimary,
    };

    private static FluentButton CreateActionButton(
        string name,
        string text,
        FluentButtonKind kind,
        int width) => new()
    {
        Name = name,
        Text = text,
        Kind = kind,
        Palette = FluentTheme.Current,
        Width = width,
        Height = 34,
    };
}
