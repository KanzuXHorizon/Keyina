using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Keyina.Host.Core.Applications;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Translation;
using Keyina.Host.UI.Fluent;
using Keyina.Host.Windows.Typing;
using Microsoft.Win32;

#pragma warning disable CA1725

namespace Keyina.Host.UI;

public sealed partial class SettingsForm : Form
{
    private const string DeepLAuthenticationHelpUrl =
        "https://developers.deepl.com/docs/getting-started/auth";

    private static readonly Dictionary<string, (string Title, string Subtitle)> SectionCopy =
        new(StringComparer.Ordinal)
        {
            ["overview"] = ("Tổng quan", "Trạng thái bộ gõ và các hành động cần thiết."),
            ["typing"] = ("Bộ gõ", "Thiết lập cách Keyina xử lý tiếng Việt trong Windows."),
            ["speech"] = ("Nhập bằng giọng nói", "Đọc tiếng Việt vào ứng dụng đang được chọn."),
            ["translation"] = ("Dịch nhanh", "Dịch phần văn bản đang chọn mà không làm mất focus."),
            ["hotkeys"] = ("Phím tắt", "Các thao tác nhanh hoạt động trên toàn hệ thống."),
            ["applications"] = ("Ứng dụng", "Tùy chỉnh hành vi Keyina theo tên file thực thi."),
            ["snippets"] = ("Gõ tắt", "Mở rộng cụm từ và lệnh cục bộ, có kiểm soát."),
            ["diagnostics"] = ("Chẩn đoán", "Kiểm tra trạng thái; raw trace chỉ chạy trong ô sandbox đang focus."),
        };

    private readonly SettingsActions actions;
    private readonly Dictionary<string, Panel> pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FluentNavigationButton> navigationButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<HotkeyCommand, Label> hotkeyKeycaps = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly TableLayoutPanel shell;
    private readonly Panel sidebar;
    private readonly Panel contentPanel;
    private readonly Panel pageHost;
    private readonly Label sectionTitle;
    private readonly Label sectionSubtitle;
    private readonly Label systemThemeStatus;
    private readonly FluentStatusBadge statusMessage;
    private readonly FluentStatusBadge inputStatus;
    private readonly FluentStatusBadge speechStatus;
    private readonly FluentStatusBadge speechCredentialStatus;
    private readonly FluentStatusBadge translationCredentialStatus;
    private readonly FluentStatusBadge libreTranslateCredentialStatus;
    private readonly FluentStatusBadge translationHotkeyStatus;
    private readonly FluentStatusBadge ipcStatus;
    private readonly FluentStatusBadge hotkeyStatus;
    private readonly Label snippetCount;
    private readonly FluentToggle vietnameseToggle;
    private readonly FluentToggle speechToggle;
    private readonly FluentToggle translationToggle;
    private readonly FluentToggle translationPreviewToggle;
    private readonly FluentToggle libreTranslateToggle;
    private readonly FluentToggle allowLocalTranslationEndpointToggle;
    private readonly FluentToggle startupToggle;
    private readonly FluentToggle typingLatencyToggle;
    private readonly ListView typingLatencyTable;
    private readonly TextBox typingDiagnosticInput;
    private readonly Label typingDiagnosticStatus;
    private readonly ComboBox typingDiagnosticFilter;
    private readonly TextBox typingDiagnosticLog;
    private readonly System.Windows.Forms.Timer typingDiagnosticTimer;
    private readonly ComboBox feedbackMode;
    private readonly FluentButton previewFeedback;
    private readonly TextBox speechApiKey;
    private readonly FluentButton saveSpeechKey;
    private readonly FluentButton removeSpeechKey;
    private readonly ComboBox translationTargetLanguage;
    private readonly TextBox deepLApiKey;
    private readonly FluentButton saveDeepLKey;
    private readonly FluentButton removeDeepLKey;
    private readonly TextBox libreTranslateEndpoint;
    private readonly TextBox libreTranslateApiKey;
    private readonly FluentButton saveLibreTranslateKey;
    private readonly FluentButton removeLibreTranslateKey;
    private readonly Label diagnosticsResult;
    private readonly FluentButton setupTsfButton;
    private readonly FlowLayoutPanel snippetsList;
    private readonly TextBox snippetsSearch;
    private readonly ComboBox snippetsFilter;
    private readonly TextBox disableVietnameseApplications;
    private readonly TextBox disableSpeechApplications;
    private readonly TextBox disableTranslationApplications;
    private readonly TextBox suppressVisualFeedbackApplications;
    private readonly Label applicationRulesStatus;
    private FluentThemePalette palette = FluentTheme.Current;
    private SettingsSnapshot currentSnapshot;
    private bool applyingSnapshot;
    private bool applicationRulesDirty;
    private bool resourcesReleased;

    public SettingsForm(SettingsSnapshot snapshot, SettingsActions actions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        currentSnapshot = snapshot;

        Text = "Keyina";
        AccessibleName = "Cài đặt Keyina";
        AccessibleDescription =
            "Cài đặt bộ gõ tiếng Việt, nhập bằng giọng nói, dịch nhanh, phím tắt, gõ tắt và chẩn đoán.";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 620);
        Size = new Size(1100, 760);
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        statusMessage = CreateBadge("statusMessage", 142);
        inputStatus = CreateBadge("inputStatus", 104);
        speechStatus = CreateBadge("speechStatus", 104);
        speechCredentialStatus = CreateBadge("speechCredentialStatus", 118);
        translationCredentialStatus = CreateBadge("translationCredentialStatus", 118);
        libreTranslateCredentialStatus = CreateBadge(
            "libreTranslateCredentialStatus",
            118);
        translationHotkeyStatus = CreateBadge("translationHotkeyStatus", 126);
        ipcStatus = CreateBadge("ipcStatus", 150);
        hotkeyStatus = CreateBadge("hotkeyStatus", 120);
        snippetCount = CreateLabel("snippetCount", string.Empty, LabelRole.Secondary);

        vietnameseToggle = CreateToggle("vietnameseToggle", "Bật bộ gõ tiếng Việt");
        speechToggle = CreateToggle("speechToggle", "Bật nhập bằng giọng nói");
        translationToggle = CreateToggle("translationToggle", "Bật dịch nhanh văn bản đang chọn");
        translationPreviewToggle = CreateToggle(
            "translationPreviewToggle",
            "Xem trước bản dịch trước khi thay thế");
        libreTranslateToggle = CreateToggle(
            "libreTranslateToggle",
            "Dùng LibreTranslate khi DeepL không khả dụng");
        allowLocalTranslationEndpointToggle = CreateToggle(
            "allowLocalTranslationEndpointToggle",
            "Cho phép endpoint local hoặc mạng riêng");
        startupToggle = CreateToggle("startupToggle", "Khởi động Keyina cùng Windows");
        typingLatencyToggle = CreateToggle(
            "typingLatencyToggle",
            "Đo độ trễ từng công đoạn");
        typingLatencyTable = CreateTypingLatencyTable();
        typingDiagnosticInput = CreateTextBox(
            "typingDiagnosticInput",
            "Gõ ca sai dấu hoặc double phím tại đây",
            "Ô nhập sandbox chẩn đoán bộ gõ");
        typingDiagnosticInput.Multiline = true;
        typingDiagnosticInput.AcceptsReturn = true;
        typingDiagnosticInput.AcceptsTab = false;
        typingDiagnosticInput.ScrollBars = ScrollBars.Vertical;
        typingDiagnosticInput.MaxLength = 4096;
        typingDiagnosticInput.Font = new Font(Font.FontFamily, 11F, FontStyle.Regular);
        typingDiagnosticInput.AccessibleDescription =
            "Chỉ khi ô này có focus, Keyina mới ghi phím thô, quyết định engine và kết quả hiển thị.";
        typingDiagnosticStatus = CreateLabel(
            "typingDiagnosticStatus",
            "Tạm dừng — nhấp vào ô để bắt đầu ghi.",
            LabelRole.Caption);
        typingDiagnosticFilter = new ComboBox
        {
            Name = "typingDiagnosticFilter",
            AccessibleName = "Bộ lọc log chẩn đoán bộ gõ",
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular),
            Width = 148,
            Height = 34,
            IntegralHeight = false,
            DropDownHeight = 160,
            Margin = Padding.Empty,
        };
        typingDiagnosticFilter.Items.AddRange(
        [
            "Tất cả",
            "Phím vật lý",
            "Engine",
            "Kết quả",
            "Bất thường",
        ]);
        typingDiagnosticFilter.SelectedIndex = 0;
        typingDiagnosticLog = CreateTextBox(
            "typingDiagnosticLog",
            "Log sẽ xuất hiện khi bắt đầu gõ",
            "Log chi tiết chẩn đoán bộ gõ");
        typingDiagnosticLog.Multiline = true;
        typingDiagnosticLog.ReadOnly = true;
        typingDiagnosticLog.WordWrap = false;
        typingDiagnosticLog.ScrollBars = ScrollBars.Both;
        typingDiagnosticLog.TabStop = false;
        typingDiagnosticLog.Font = new Font("Cascadia Mono", 9F, FontStyle.Regular);
        typingDiagnosticTimer = new System.Windows.Forms.Timer
        {
            Interval = 120,
        };
        feedbackMode = CreateFeedbackModeSelector();
        previewFeedback = CreateButton(
            "previewFeedback",
            "Thử phản hồi",
            FluentButtonKind.Secondary,
            128);

        speechApiKey = CreateTextBox(
            "speechApiKey",
            "Dán khóa API Speechmatics",
            "Khóa API Speechmatics");
        speechApiKey.UseSystemPasswordChar = true;
        speechApiKey.MaxLength = 256;
        speechApiKey.AccessibleDescription =
            "Khóa được che khi nhập và chỉ lưu trong Windows Credential Manager.";
        saveSpeechKey = CreateButton(
            "saveSpeechKey",
            "Lưu khóa",
            FluentButtonKind.Primary,
            112);
        saveSpeechKey.Enabled = false;
        removeSpeechKey = CreateButton(
            "removeSpeechKey",
            "Xóa khóa",
            FluentButtonKind.Secondary,
            108);

        translationTargetLanguage = new ComboBox
        {
            Name = "translationTargetLanguage",
            AccessibleName = "Ngôn ngữ đích",
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            DisplayMember = nameof(TranslationLanguage.DisplayName),
            ValueMember = nameof(TranslationLanguage.Code),
            DataSource = TranslationLanguageCatalog.SupportedTargets.ToArray(),
            Height = 36,
            IntegralHeight = false,
            DropDownHeight = 280,
        };
        deepLApiKey = CreateTextBox(
            "deepLApiKey",
            "Dán khóa DeepL API Free",
            "Khóa API DeepL");
        deepLApiKey.UseSystemPasswordChar = true;
        deepLApiKey.MaxLength = 256;
        deepLApiKey.AccessibleDescription =
            "Khóa được che khi nhập và chỉ lưu trong Windows Credential Manager.";
        saveDeepLKey = CreateButton(
            "saveDeepLKey",
            "Lưu khóa",
            FluentButtonKind.Primary,
            112);
        saveDeepLKey.Enabled = false;
        removeDeepLKey = CreateButton(
            "removeDeepLKey",
            "Xóa khóa",
            FluentButtonKind.Secondary,
            108);
        libreTranslateEndpoint = CreateTextBox(
            "libreTranslateEndpoint",
            "https://translate.example",
            "Endpoint LibreTranslate");
        libreTranslateEndpoint.MaxLength =
            TranslationProviderPreferences.MaximumEndpointLength;
        libreTranslateEndpoint.AccessibleDescription =
            "Nhập URL server LibreTranslate do bạn tin cậy; Keyina không tự chọn public mirror.";
        libreTranslateApiKey = CreateTextBox(
            "libreTranslateApiKey",
            "Khóa API tùy chọn",
            "Khóa API LibreTranslate");
        libreTranslateApiKey.UseSystemPasswordChar = true;
        libreTranslateApiKey.MaxLength = 256;
        saveLibreTranslateKey = CreateButton(
            "saveLibreTranslateKey",
            "Lưu khóa",
            FluentButtonKind.Primary,
            112);
        saveLibreTranslateKey.Enabled = false;
        removeLibreTranslateKey = CreateButton(
            "removeLibreTranslateKey",
            "Xóa khóa",
            FluentButtonKind.Secondary,
            108);

        diagnosticsResult = CreateLabel(
            "diagnosticsResult",
            "Chưa chạy kiểm tra. Keyina chỉ đọc trạng thái hệ thống cục bộ.",
            LabelRole.Secondary);
        diagnosticsResult.AutoEllipsis = true;

        setupTsfButton = CreateButton(
            "setupTsf",
            "Mở kiểm tra gõ",
            FluentButtonKind.Primary,
            168);
        snippetsList = CreateVerticalStack("snippetsList");
        snippetsList.AutoScroll = true;
        snippetsList.Dock = DockStyle.Fill;
        snippetsSearch = CreateTextBox(
            "snippetsSearch",
            "Tìm theo từ kích hoạt hoặc nội dung",
            "Tìm gõ tắt");
        snippetsFilter = new ComboBox
        {
            Name = "snippetsFilter",
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Lọc gõ tắt",
            Width = 164,
        };
        snippetsFilter.Items.AddRange(["Tất cả", "Có sẵn", "Tùy chỉnh", "Đầu ra lệnh"]);
        snippetsFilter.SelectedIndex = 0;
        disableVietnameseApplications = CreateApplicationListTextBox(
            "disableVietnameseApplications",
            "Ứng dụng không dùng bộ gõ tiếng Việt");
        disableSpeechApplications = CreateApplicationListTextBox(
            "disableSpeechApplications",
            "Ứng dụng không dùng nhập giọng nói");
        disableTranslationApplications = CreateApplicationListTextBox(
            "disableTranslationApplications",
            "Ứng dụng không dùng dịch nhanh");
        suppressVisualFeedbackApplications = CreateApplicationListTextBox(
            "suppressVisualFeedbackApplications",
            "Ứng dụng chỉ dùng phản hồi âm thanh");
        applicationRulesStatus = CreateLabel(
            "applicationRulesStatus",
            "Mỗi dòng là một tên file .exe, ví dụ game.exe.",
            LabelRole.Tertiary);

        shell = new TableLayoutPanel
        {
            Name = "settingsShell",
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228F));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(shell);

        sidebar = CreateSidebar();
        shell.Controls.Add(sidebar, 0, 0);

        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(30, 22, 30, 26),
        };
        shell.Controls.Add(contentPanel, 1, 0);

        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        contentPanel.Controls.Add(contentLayout);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        contentLayout.Controls.Add(header, 0, 0);

        sectionTitle = CreateLabel("sectionTitle", "Tổng quan", LabelRole.Title);
        sectionTitle.Dock = DockStyle.Fill;
        sectionTitle.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(sectionTitle, 0, 0);

        sectionSubtitle = CreateLabel(
            "sectionSubtitle",
            SectionCopy["overview"].Subtitle,
            LabelRole.Secondary);
        sectionSubtitle.Dock = DockStyle.Fill;
        sectionSubtitle.TextAlign = ContentAlignment.TopLeft;
        header.Controls.Add(sectionSubtitle, 0, 1);

        systemThemeStatus = CreateLabel(
            "systemThemeStatus",
            FluentTheme.SystemThemeDescription,
            LabelRole.Tertiary);
        systemThemeStatus.AutoSize = true;
        systemThemeStatus.TextAlign = ContentAlignment.MiddleRight;
        systemThemeStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        systemThemeStatus.Margin = new Padding(12, 8, 0, 0);
        systemThemeStatus.AccessibleName = "Giao diện hiện tại";
        header.SetRowSpan(systemThemeStatus, 2);
        header.Controls.Add(systemThemeStatus, 1, 0);

        pageHost = new Panel
        {
            Name = "pageHost",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        contentLayout.Controls.Add(pageHost, 0, 1);

        pages.Add("overview", CreateOverviewPage());
        pages.Add("typing", CreateTypingPage());
        pages.Add("speech", CreateSpeechPage());
        pages.Add("translation", CreateTranslationPage());
        pages.Add("hotkeys", CreateHotkeysPage());
        pages.Add("applications", CreateApplicationsPage());
        pages.Add("snippets", CreateSnippetsPage());
        pages.Add("diagnostics", CreateDiagnosticsPage());
        foreach (var page in pages.Values)
        {
            pageHost.Controls.Add(page);
        }

        vietnameseToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                actions.SetVietnameseEnabled(vietnameseToggle.Checked);
            }
        };
        speechToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                actions.SetSpeechEnabled(speechToggle.Checked);
            }
        };
        translationToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                actions.SetTranslationEnabled(translationToggle.Checked);
            }
        };
        translationTargetLanguage.SelectedValueChanged += (_, _) =>
        {
            if (!applyingSnapshot &&
                translationTargetLanguage.SelectedValue is string targetLanguage)
            {
                actions.SetTranslationTargetLanguage(targetLanguage);
            }
        };
        translationPreviewToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                actions.SetTranslationPreviewEnabled(translationPreviewToggle.Checked);
            }
        };
        startupToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                actions.SetStartupEnabled(startupToggle.Checked);
            }
        };
        typingLatencyToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                actions.SetTypingLatencyEnabled(typingLatencyToggle.Checked);
            }
        };
        feedbackMode.SelectedIndexChanged += (_, _) =>
        {
            if (!applyingSnapshot && feedbackMode.SelectedIndex >= 0)
            {
                actions.SetFeedbackMode(FeedbackModeFromIndex(feedbackMode.SelectedIndex));
            }
        };
        previewFeedback.Click += (_, _) => actions.PreviewFeedback();
        speechApiKey.TextChanged += (_, _) => saveSpeechKey.Enabled =
            !string.IsNullOrWhiteSpace(speechApiKey.Text);
        speechApiKey.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter || !saveSpeechKey.Enabled)
            {
                return;
            }

            SaveSpeechCredential();
            eventArgs.SuppressKeyPress = true;
            eventArgs.Handled = true;
        };
        saveSpeechKey.Click += (_, _) => SaveSpeechCredential();
        removeSpeechKey.Click += (_, _) => actions.DeleteSpeechApiKey();
        deepLApiKey.TextChanged += (_, _) => saveDeepLKey.Enabled =
            !string.IsNullOrWhiteSpace(deepLApiKey.Text);
        deepLApiKey.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter || !saveDeepLKey.Enabled)
            {
                return;
            }

            SaveDeepLCredential();
            eventArgs.SuppressKeyPress = true;
            eventArgs.Handled = true;
        };
        saveDeepLKey.Click += (_, _) => SaveDeepLCredential();
        removeDeepLKey.Click += (_, _) => actions.DeleteDeepLApiKey();
        libreTranslateToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                SaveTranslationProviderPreferences();
            }
        };
        allowLocalTranslationEndpointToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                SaveTranslationProviderPreferences();
            }
        };
        libreTranslateEndpoint.Leave += (_, _) => SaveTranslationProviderPreferences();
        libreTranslateApiKey.TextChanged += (_, _) => saveLibreTranslateKey.Enabled =
            !string.IsNullOrWhiteSpace(libreTranslateApiKey.Text);
        libreTranslateApiKey.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter || !saveLibreTranslateKey.Enabled)
            {
                return;
            }

            SaveLibreTranslateCredential();
            eventArgs.SuppressKeyPress = true;
            eventArgs.Handled = true;
        };
        saveLibreTranslateKey.Click += (_, _) => SaveLibreTranslateCredential();
        removeLibreTranslateKey.Click += (_, _) => actions.DeleteLibreTranslateApiKey();
        snippetsSearch.TextChanged += (_, _) => FilterSnippets(snippetsSearch.Text);
        snippetsFilter.SelectedIndexChanged += (_, _) => FilterSnippets(snippetsSearch.Text);
        foreach (var textBox in GetApplicationRuleTextBoxes())
        {
            textBox.TextChanged += (_, _) =>
            {
                if (!applyingSnapshot)
                {
                    applicationRulesDirty = true;
                    applicationRulesStatus.Text = "Có thay đổi chưa lưu.";
                    applicationRulesStatus.ForeColor = palette.Warning;
                }
            };
        }
        setupTsfButton.Click += SetupTsfButtonClick;
        typingDiagnosticInput.Enter += (_, _) => StartTypingDiagnosticCapture();
        typingDiagnosticInput.Leave += (_, _) => PauseTypingDiagnosticCapture();
        typingDiagnosticInput.KeyDown += (_, eventArgs) =>
            RecordTypingDiagnosticControlEvent($"WinForms.KeyDown:{eventArgs.KeyCode}");
        typingDiagnosticInput.KeyPress += (_, eventArgs) =>
            RecordTypingDiagnosticControlEvent(
                $"WinForms.KeyPress:{FormatDiagnosticCharacter(eventArgs.KeyChar)}");
        typingDiagnosticInput.KeyUp += (_, eventArgs) =>
            RecordTypingDiagnosticControlEvent($"WinForms.KeyUp:{eventArgs.KeyCode}");
        typingDiagnosticInput.TextChanged += (_, _) =>
            RecordTypingDiagnosticControlEvent("TextChanged");
        typingDiagnosticFilter.SelectedIndexChanged += (_, _) =>
            RefreshTypingDiagnosticLog();
        typingDiagnosticTimer.Tick += (_, _) => RefreshTypingDiagnosticLog();

        SystemEvents.UserPreferenceChanged += SystemEventsUserPreferenceChanged;
        Resize += (_, _) => UpdateResponsiveShell();

        ApplySnapshot(snapshot);
        ApplySystemTheme();
        ShowSection("overview");
    }

    public bool UsesBufferedRendering => DoubleBuffered;

    public void ApplySnapshot(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        currentSnapshot = snapshot;
        applyingSnapshot = true;
        try
        {
            vietnameseToggle.Checked = snapshot.VietnameseEnabled;
            speechToggle.Checked = snapshot.SpeechEnabled;
            translationToggle.Checked = snapshot.TranslationEnabled;
            translationPreviewToggle.Checked = snapshot.TranslationPreviewEnabled;
            libreTranslateToggle.Checked = snapshot.TranslationProviders.LibreTranslateEnabled;
            allowLocalTranslationEndpointToggle.Checked =
                snapshot.TranslationProviders.AllowLocalEndpoint;
            libreTranslateEndpoint.Text = snapshot.TranslationProviders.LibreTranslateEndpoint;
            translationTargetLanguage.SelectedValue = snapshot.TranslationTargetLanguage;
            startupToggle.Checked = snapshot.StartupEnabled;
            typingLatencyToggle.Checked = TypingLatencyProfiler.IsEnabled;
            feedbackMode.SelectedIndex = FeedbackModeToIndex(snapshot.FeedbackMode);
            if (!applicationRulesDirty)
            {
                UpdateApplicationRulesDisplay(snapshot.Applications);
            }

            var readinessText = snapshot.Listening
                ? "Đang nghe"
                : snapshot.Readiness switch
                {
                    KeyinaReadiness.Ready => "Sẵn sàng",
                    KeyinaReadiness.NeedsSetup => "Cần thiết lập",
                    KeyinaReadiness.NeedsAttention => "Cần xử lý",
                    KeyinaReadiness.Unavailable => "Không khả dụng",
                    _ => "Đang kiểm tra",
                };
            SetBadge(
                statusMessage,
                readinessText,
                snapshot.Listening
                    ? FluentTone.Warning
                    : snapshot.Readiness switch
                    {
                        KeyinaReadiness.Ready => FluentTone.Success,
                        KeyinaReadiness.NeedsSetup => FluentTone.Warning,
                        KeyinaReadiness.NeedsAttention => FluentTone.Warning,
                        KeyinaReadiness.Unavailable => FluentTone.Error,
                        _ => FluentTone.Neutral,
                    });

            SetBadge(
                inputStatus,
                !snapshot.TsfRegistered
                    ? "Chưa kết nối"
                    : snapshot.VietnameseEnabled ? "Đang bật" : "Đang tắt",
                !snapshot.TsfRegistered
                    ? FluentTone.Warning
                    : snapshot.VietnameseEnabled ? FluentTone.Success : FluentTone.Neutral);
            SetBadge(
                speechStatus,
                snapshot.Listening
                    ? "Đang nghe"
                    : snapshot.SpeechEnabled ? "Sẵn sàng" : "Đang tắt",
                snapshot.Listening
                    ? FluentTone.Warning
                    : snapshot.SpeechEnabled ? FluentTone.Success : FluentTone.Neutral);
            SetBadge(
                speechCredentialStatus,
                snapshot.SpeechCredentialConfigured ? "Đã cấu hình" : "Chưa cấu hình",
                snapshot.SpeechCredentialConfigured ? FluentTone.Success : FluentTone.Warning);
            SetBadge(
                translationCredentialStatus,
                snapshot.TranslationCredentialConfigured ? "Đã cấu hình" : "Chưa cấu hình",
                snapshot.TranslationCredentialConfigured ? FluentTone.Success : FluentTone.Warning);
            SetBadge(
                libreTranslateCredentialStatus,
                snapshot.LibreTranslateCredentialConfigured
                    ? "Đã cấu hình"
                    : "Không bắt buộc",
                snapshot.LibreTranslateCredentialConfigured
                    ? FluentTone.Success
                    : FluentTone.Neutral);
            var translationProviderAvailable =
                snapshot.TranslationCredentialConfigured ||
                snapshot.TranslationProviders.LibreTranslateEnabled;
            SetBadge(
                translationHotkeyStatus,
                !translationProviderAvailable
                    ? "Cần provider"
                    : !snapshot.TranslationEnabled
                        ? "Chưa bật"
                        : snapshot.TranslationHotkeyRegistered
                            ? "Đã đăng ký"
                            : "Đang xung đột",
                !translationProviderAvailable
                    ? FluentTone.Warning
                    : !snapshot.TranslationEnabled
                        ? FluentTone.Neutral
                        : snapshot.TranslationHotkeyRegistered
                            ? FluentTone.Success
                            : FluentTone.Warning);
            SetBadge(
                ipcStatus,
                LocalizeRuntimeStatus(snapshot.IpcStatus),
                snapshot.IpcStatus.Contains("connected", StringComparison.OrdinalIgnoreCase)
                    ? FluentTone.Success
                    : FluentTone.Warning);
            SetBadge(
                hotkeyStatus,
                LocalizeRuntimeStatus(snapshot.HotkeyStatus),
                snapshot.HotkeyStatus.Contains("registered", StringComparison.OrdinalIgnoreCase) ||
                snapshot.HotkeyStatus.Contains("active", StringComparison.OrdinalIgnoreCase)
                    ? FluentTone.Success
                    : FluentTone.Warning);

            snippetCount.Text = snapshot.CustomSnippetCount == 1
                ? "1 gõ tắt tùy chỉnh"
                : $"{snapshot.CustomSnippetCount} gõ tắt tùy chỉnh";
            AddSnippetRows();
            removeSpeechKey.Enabled = snapshot.SpeechCredentialConfigured;
            removeDeepLKey.Enabled = snapshot.TranslationCredentialConfigured;
            saveDeepLKey.Text = snapshot.TranslationCredentialConfigured
                ? "Cập nhật khóa"
                : "Lưu khóa";
            removeLibreTranslateKey.Enabled = snapshot.LibreTranslateCredentialConfigured;
            saveLibreTranslateKey.Text = snapshot.LibreTranslateCredentialConfigured
                ? "Cập nhật khóa"
                : "Lưu khóa";
            setupTsfButton.Text = snapshot.Readiness switch
            {
                KeyinaReadiness.Ready => "Kiểm tra bộ gõ",
                KeyinaReadiness.NeedsSetup or
                KeyinaReadiness.NeedsAttention or
                KeyinaReadiness.Unavailable => "Mở chẩn đoán",
                _ => "Kiểm tra lại",
            };
            setupTsfButton.Kind = snapshot.Readiness == KeyinaReadiness.Unavailable
                ? FluentButtonKind.Secondary
                : FluentButtonKind.Primary;
            UpdateHotkeyDisplay(snapshot.Hotkeys);
            setupTsfButton.Invalidate();
        }
        finally
        {
            applyingSnapshot = false;
        }
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        FluentWindow.Apply(this, palette);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesReleased)
        {
            resourcesReleased = true;
            SystemEvents.UserPreferenceChanged -= SystemEventsUserPreferenceChanged;
            typingDiagnosticTimer.Stop();
            typingDiagnosticTimer.Dispose();
            TypingDiagnosticTrace.ClearAndDisable();
            lifetime.Cancel();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }

    private Panel CreateSidebar()
    {
        var panel = new Panel
        {
            Name = "sidebar",
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 18, 14, 14),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        panel.Controls.Add(layout);

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        layout.Controls.Add(brand, 0, 0);

        var mark = new Label
        {
            Name = "brandMark",
            Dock = DockStyle.Fill,
            Text = "K",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Variable Display", 18F, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 10, 10),
            AccessibleName = "Biểu tượng Keyina",
        };
        mark.Paint += BrandMarkPaint;
        brand.SetRowSpan(mark, 2);
        brand.Controls.Add(mark, 0, 0);

        var product = CreateLabel("productName", "Keyina", LabelRole.Heading);
        product.Dock = DockStyle.Fill;
        product.TextAlign = ContentAlignment.BottomLeft;
        brand.Controls.Add(product, 1, 0);

        var productSubtitle = CreateLabel(
            "productSubtitle",
            "Bộ gõ tiếng Việt",
            LabelRole.Tertiary);
        productSubtitle.Dock = DockStyle.Fill;
        productSubtitle.TextAlign = ContentAlignment.TopLeft;
        brand.Controls.Add(productSubtitle, 1, 1);

        var navigation = new FlowLayoutPanel
        {
            Name = "navigation",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0, 8, 0, 0),
        };
        layout.Controls.Add(navigation, 0, 1);

        AddNavigation(navigation, "navOverview", "Tổng quan", "\uE80F", "overview");
        AddNavigation(navigation, "navTyping", "Bộ gõ", "\uE765", "typing");
        AddNavigation(navigation, "navSpeech", "Nhập bằng giọng nói", "\uE720", "speech");
        AddNavigation(navigation, "navTranslation", "Dịch nhanh", "\uE8C1", "translation");
        AddNavigation(navigation, "navHotkeys", "Phím tắt", "\uE92E", "hotkeys");
        AddNavigation(navigation, "navApplications", "Ứng dụng", "\uE7C5", "applications");
        AddNavigation(navigation, "navSnippets", "Gõ tắt", "\uE8A5", "snippets");
        AddNavigation(navigation, "navDiagnostics", "Chẩn đoán", "\uE9D9", "diagnostics");
        navigation.SizeChanged += (_, _) =>
        {
            foreach (Control child in navigation.Controls)
            {
                child.Width = Math.Max(120, navigation.ClientSize.Width - 2);
            }
        };

        var privacy = new TableLayoutPanel
        {
            Name = "privacySummary",
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(4, 0, 4, 0),
            Padding = new Padding(8, 4, 8, 4),
        };
        privacy.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26F));
        privacy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        privacy.RowStyles.Add(new RowStyle(SizeType.Absolute, 21F));
        privacy.RowStyles.Add(new RowStyle(SizeType.Absolute, 21F));
        layout.Controls.Add(privacy, 0, 2);

        var shield = CreateIconLabel("privacyIcon", "\uEA18", 14F);
        shield.Dock = DockStyle.Fill;
        privacy.SetRowSpan(shield, 2);
        privacy.Controls.Add(shield, 0, 0);
        var localFirst = CreateLabel("localFirst", "Ưu tiên xử lý cục bộ", LabelRole.Caption);
        localFirst.Dock = DockStyle.Fill;
        privacy.Controls.Add(localFirst, 1, 0);
        var localDetail = CreateLabel(
            "localDetail",
            "Gõ văn bản không dùng mạng",
            LabelRole.Tertiary);
        localDetail.Dock = DockStyle.Fill;
        privacy.Controls.Add(localDetail, 1, 1);

        var version = CreateLabel(
            "versionLabel",
            $"Phiên bản {currentSnapshot.Version}",
            LabelRole.Tertiary);
        version.Dock = DockStyle.Fill;
        version.TextAlign = ContentAlignment.MiddleLeft;
        version.Padding = new Padding(8, 0, 0, 0);
        layout.Controls.Add(version, 0, 3);

        return panel;
    }

    private void AddNavigation(
        FlowLayoutPanel navigation,
        string name,
        string text,
        string glyph,
        string pageKey)
    {
        var button = new FluentNavigationButton
        {
            Name = name,
            Text = text,
            Glyph = glyph,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Width = 196,
            AccessibleName = text,
            AccessibleDescription = $"Mở trang {text}",
        };
        button.Click += (_, _) => ShowSection(pageKey);
        navigation.Controls.Add(button);
        navigationButtons.Add(pageKey, button);
    }

    public void OpenSection(string pageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageKey);
        ShowSection(pageKey);
    }

    public void OpenCredentialSetup(CredentialSetupTarget target)
    {
        var (section, input) = target switch
        {
            CredentialSetupTarget.Speechmatics => ("speech", speechApiKey),
            CredentialSetupTarget.Translation => ("translation", deepLApiKey),
            CredentialSetupTarget.LibreTranslate =>
                ("translation", libreTranslateApiKey),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        ShowSection(section);
        input.Select();
        _ = input.Focus();
    }

    private void ShowSection(string pageKey)
    {
        if (!pages.TryGetValue(pageKey, out var selectedPage) ||
            !SectionCopy.TryGetValue(pageKey, out var copy))
        {
            throw new ArgumentOutOfRangeException(nameof(pageKey));
        }

        foreach (var page in pages.Values)
        {
            page.Visible = ReferenceEquals(page, selectedPage);
        }
        selectedPage.BringToFront();

        foreach (var (key, button) in navigationButtons)
        {
            button.Selected = string.Equals(key, pageKey, StringComparison.Ordinal);
        }

        sectionTitle.Text = copy.Title;
        sectionSubtitle.Text = copy.Subtitle;
        selectedPage.SelectNextControl(
            selectedPage,
            forward: true,
            tabStopOnly: true,
            nested: true,
            wrap: false);
    }

    private Panel CreateOverviewPage()
    {
        var page = CreatePage("overviewPage");
        var stack = CreateVerticalStack("overviewStack");
        page.Controls.Add(stack);

        var readiness = CreateCard("readinessCard", 150);
        var readinessLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        readinessLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        readinessLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        readinessLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        readinessLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        readinessLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        readinessLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        readiness.Controls.Add(readinessLayout);

        var readinessTitle = CreateLabel(
            "readinessTitle",
            "Trạng thái hệ thống",
            LabelRole.Heading);
        readinessTitle.Dock = DockStyle.Fill;
        readinessLayout.Controls.Add(readinessTitle, 0, 0);
        readinessLayout.SetColumnSpan(readinessTitle, 3);

        statusMessage.Anchor = AnchorStyles.Left;
        readinessLayout.Controls.Add(statusMessage, 0, 1);
        setupTsfButton.Anchor = AnchorStyles.Right;
        setupTsfButton.Margin = new Padding(12, 2, 0, 2);
        readinessLayout.Controls.Add(setupTsfButton, 2, 1);

        var readinessDetail = CreateLabel(
            "readinessDetail",
            "Keyina chỉ báo sẵn sàng khi host, bộ gõ, phím tắt và đường nhập đang hoạt động đúng.",
            LabelRole.Secondary);
        readinessDetail.Dock = DockStyle.Fill;
        readinessDetail.Padding = new Padding(0, 4, 0, 0);
        readinessLayout.Controls.Add(readinessDetail, 0, 2);
        readinessLayout.SetColumnSpan(readinessDetail, 3);
        stack.Controls.Add(readiness);

        var statusGrid = new TableLayoutPanel
        {
            Name = "overviewStatusGrid",
            Height = 248,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12),
            Padding = Padding.Empty,
        };
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        statusGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        statusGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        statusGrid.Controls.Add(CreateStatusCard(
            "overviewTyping",
            "\uE765",
            "Bộ gõ",
            inputStatus,
            "Telex · phím tắt Ctrl + Shift"), 0, 0);
        statusGrid.Controls.Add(CreateStatusCard(
            "overviewSpeech",
            "\uE720",
            "Giọng nói",
            speechStatus,
            "Speechmatics · tiếng Việt"), 1, 0);
        statusGrid.Controls.Add(CreateStatusCard(
            "overviewHotkeys",
            "\uE92E",
            "Phím tắt hệ thống",
            hotkeyStatus,
            "Hoạt động nền, không đổi focus"), 0, 1);
        statusGrid.Controls.Add(CreateStatusCard(
            "overviewFocusedApp",
            "\uE7C5",
            "Ứng dụng đang nhập",
            ipcStatus,
            "Chèn trực tiếp vào ô đang nhập"), 1, 1);
        stack.Controls.Add(statusGrid);

        var privacyCard = CreateCard("privacyCard", 120);
        var privacyLayout = CreateIconTextLayout(
            "\uEA18",
            "Riêng tư ngay từ thiết kế",
            "Bộ gõ hoạt động ngoại tuyến. Nhập bằng giọng nói là tùy chọn; khóa API chỉ được lưu trong Windows Credential Manager.");
        privacyCard.Controls.Add(privacyLayout);
        stack.Controls.Add(privacyCard);

        return page;
    }

    private Panel CreateTypingPage()
    {
        var page = CreatePage("typingPage");
        var stack = CreateVerticalStack("typingStack");
        page.Controls.Add(stack);

        stack.Controls.Add(CreateSettingRow(
            "typingEnabledRow",
            "\uE765",
            "Bộ gõ tiếng Việt",
            "Bật hoặc tắt mà không làm mất focus của ứng dụng hiện tại.",
            "Ctrl + Shift",
            vietnameseToggle));
        stack.Controls.Add(CreateSettingRow(
            "startupRow",
            "\uE7E8",
            "Khởi động cùng Windows",
            "Chạy host nhẹ cho tài khoản Windows hiện tại.",
            "Người dùng hiện tại",
            startupToggle));

        var testCard = CreateCard("typingTestCard", 248);
        var testLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        testLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        testCard.Controls.Add(testLayout);

        var testTitle = CreateLabel("typingTestTitle", "Thử gõ thật", LabelRole.Heading);
        testTitle.Dock = DockStyle.Fill;
        testLayout.Controls.Add(testTitle, 0, 0);
        var testPrompt = CreateLabel(
            "typingTestPrompt",
            "Nhấp vào ô và gõ:  tieengs Vieetj   →   tiếng Việt",
            LabelRole.Secondary);
        testPrompt.Dock = DockStyle.Fill;
        testPrompt.TextAlign = ContentAlignment.MiddleLeft;
        testLayout.Controls.Add(testPrompt, 0, 1);

        var typingTest = CreateTextBox(
            "typingTestInput",
            "Gõ câu Telex ở đây",
            "Ô kiểm tra gõ tiếng Việt");
        typingTest.Font = new Font(Font.FontFamily, 13F, FontStyle.Regular);
        typingTest.Margin = new Padding(0, 2, 0, 4);
        typingTest.Dock = DockStyle.Fill;
        testLayout.Controls.Add(CreateInputFrame(typingTest), 0, 2);

        var typingResult = CreateLabel(
            "typingTestResult",
            "Chưa kiểm tra",
            LabelRole.Caption);
        typingResult.Dock = DockStyle.Fill;
        typingResult.TextAlign = ContentAlignment.MiddleLeft;
        testLayout.Controls.Add(typingResult, 0, 3);

        var testNote = CreateLabel(
            "typingTestNote",
            "Bài kiểm tra dùng chính ô đang focus; không gọi engine trực tiếp để giả lập kết quả.",
            LabelRole.Tertiary);
        testNote.Dock = DockStyle.Fill;
        testNote.TextAlign = ContentAlignment.TopLeft;
        testLayout.Controls.Add(testNote, 0, 4);

        typingTest.TextChanged += (_, _) =>
        {
            var normalized = typingTest.Text.Trim();
            var passed = normalized.Contains("tiếng Việt", StringComparison.OrdinalIgnoreCase);
            actions.RecordTypingTest(passed);
            if (passed)
            {
                typingResult.Text = "Đạt — tiếng Việt đã đi qua đường nhập đang focus.";
                typingResult.ForeColor = palette.Success;
            }
            else if (normalized.Length == 0)
            {
                typingResult.Text = "Chưa kiểm tra";
                typingResult.ForeColor = palette.TextSecondary;
            }
            else
            {
                typingResult.Text = "Chưa đạt — kiểm tra bộ gõ đang bật rồi thử lại.";
                typingResult.ForeColor = palette.Warning;
            }
        };
        stack.Controls.Add(testCard);

        var guardCard = CreateCard("contextGuardCard", 126);
        guardCard.Controls.Add(CreateIconTextLayout(
            "\uE72E",
            "Context Guard",
            "Keyina ưu tiên nhập nguyên văn trong code, URL, email, lệnh, đường dẫn, identifier và trường nhập bảo mật."));
        stack.Controls.Add(guardCard);
        return page;
    }

    private Panel CreateSpeechPage()
    {
        var page = CreatePage("speechPage");
        var stack = CreateVerticalStack("speechStack");
        page.Controls.Add(stack);

        stack.Controls.Add(CreateSettingRow(
            "speechEnabledRow",
            "\uE720",
            "Nhập bằng giọng nói",
            "Chỉ chèn đoạn đã ổn định vào ứng dụng đang focus; lỗi giọng nói không ảnh hưởng bộ gõ.",
            "Ctrl + Alt + V",
            speechToggle));

        var credentialCard = CreateCard("speechCredentialCard", 238);
        var credentialLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        credentialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        credentialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credentialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        credentialCard.Controls.Add(credentialLayout);

        var credentialTitle = CreateLabel(
            "speechCredentialTitle",
            "Khóa dịch vụ Speechmatics",
            LabelRole.Heading);
        credentialTitle.Dock = DockStyle.Fill;
        credentialLayout.Controls.Add(credentialTitle, 0, 0);
        credentialLayout.SetColumnSpan(credentialTitle, 2);
        speechCredentialStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        credentialLayout.Controls.Add(speechCredentialStatus, 2, 0);

        var credentialHint = CreateLabel(
            "speechCredentialHint",
            "Khóa được mã hóa bởi Windows Credential Manager, không ghi vào settings.json.",
            LabelRole.Secondary);
        credentialHint.Dock = DockStyle.Fill;
        credentialLayout.Controls.Add(credentialHint, 0, 1);
        credentialLayout.SetColumnSpan(credentialHint, 3);

        var reveal = CreateButton(
            "toggleSpeechKeyVisibility",
            "Hiện",
            FluentButtonKind.Subtle,
            72);
        reveal.AccessibleName = "Hiện hoặc ẩn khóa API";
        reveal.Click += (_, _) =>
        {
            speechApiKey.UseSystemPasswordChar = !speechApiKey.UseSystemPasswordChar;
            reveal.Text = speechApiKey.UseSystemPasswordChar ? "Hiện" : "Ẩn";
        };
        var credentialInput = CreateInputFrame(speechApiKey, reveal);
        credentialInput.Dock = DockStyle.Fill;
        credentialInput.Margin = new Padding(0, 4, 0, 4);
        credentialLayout.Controls.Add(credentialInput, 0, 2);
        credentialLayout.SetColumnSpan(credentialInput, 3);

        var actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4),
        };
        saveSpeechKey.Margin = new Padding(0, 0, 8, 0);
        removeSpeechKey.Margin = Padding.Empty;
        actionsPanel.Controls.Add(saveSpeechKey);
        actionsPanel.Controls.Add(removeSpeechKey);
        credentialLayout.Controls.Add(actionsPanel, 0, 3);
        credentialLayout.SetColumnSpan(actionsPanel, 3);

        var credentialPrivacy = CreateLabel(
            "speechPrivacy",
            "Âm thanh chỉ được gửi khi bạn chủ động bắt đầu phiên đọc. Gõ tiếng Việt thông thường vẫn ngoại tuyến.",
            LabelRole.Tertiary);
        credentialPrivacy.Dock = DockStyle.Fill;
        credentialLayout.Controls.Add(credentialPrivacy, 0, 4);
        credentialLayout.SetColumnSpan(credentialPrivacy, 3);
        stack.Controls.Add(credentialCard);

        var providerCard = CreateCard("speechProviderCard", 118);
        providerCard.Controls.Add(CreateIconTextLayout(
            "\uE8D4",
            "Cấu hình hiện tại",
            "Tiếng Việt · đoạn tạm chỉ hiển thị trên lớp phủ · Escape để hủy phiên."));
        stack.Controls.Add(providerCard);
        return page;
    }

    private Panel CreateTranslationPage()
    {
        var page = CreatePage("translationPage");
        var stack = CreateVerticalStack("translationStack");
        page.Controls.Add(stack);

        stack.Controls.Add(CreateSettingRow(
            "translationEnabledRow",
            "\uE8C1",
            "Dịch văn bản đang chọn",
            "Chọn văn bản trong ứng dụng bất kỳ rồi dịch và thay thế ngay, không mở cửa sổ chiếm focus.",
            "Ctrl + Alt + T",
            translationToggle));

        var targetCard = CreateCard("translationTargetCard", 118);
        var targetLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        targetCard.Controls.Add(targetLayout);
        var targetTitle = CreateLabel(
            "translationTargetTitle",
            "Ngôn ngữ đích",
            LabelRole.Heading);
        targetTitle.Dock = DockStyle.Fill;
        targetLayout.Controls.Add(targetTitle, 0, 0);
        translationTargetLanguage.Dock = DockStyle.Fill;
        translationTargetLanguage.Margin = new Padding(8, 0, 0, 4);
        targetLayout.Controls.Add(translationTargetLanguage, 1, 0);
        var targetHint = CreateLabel(
            "translationTargetHint",
            "Keyina tự nhận diện ngôn ngữ nguồn. Đổi lựa chọn này không gửi nội dung ra mạng.",
            LabelRole.Secondary);
        targetHint.Dock = DockStyle.Fill;
        targetLayout.Controls.Add(targetHint, 0, 1);
        targetLayout.SetColumnSpan(targetHint, 2);
        stack.Controls.Add(targetCard);

        stack.Controls.Add(CreateSettingRow(
            "translationPreviewRow",
            "\uE890",
            "Xem trước trước khi thay thế",
            "So sánh văn bản gốc và bản dịch trong cửa sổ riêng; chỉ chèn khi bạn bấm Thay thế.",
            "Tùy chọn",
            translationPreviewToggle));

        var credentialCard = CreateCard("translationCredentialCard", 270);
        var credentialLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        credentialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        credentialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credentialLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        credentialLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        credentialCard.Controls.Add(credentialLayout);

        var credentialTitle = CreateLabel(
            "translationCredentialTitle",
            "Khóa DeepL API Free",
            LabelRole.Heading);
        credentialTitle.Dock = DockStyle.Fill;
        credentialLayout.Controls.Add(credentialTitle, 0, 0);
        credentialLayout.SetColumnSpan(credentialTitle, 2);
        translationCredentialStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        credentialLayout.Controls.Add(translationCredentialStatus, 2, 0);

        var credentialHint = CreateLabel(
            "translationCredentialHint",
            "Khóa API Free thường kết thúc bằng :fx. Keyina tự chọn endpoint phù hợp và chỉ lưu khóa trong Windows Credential Manager.",
            LabelRole.Secondary);
        credentialHint.Dock = DockStyle.Fill;
        credentialLayout.Controls.Add(credentialHint, 0, 1);
        credentialLayout.SetColumnSpan(credentialHint, 3);

        var reveal = CreateButton(
            "toggleDeepLKeyVisibility",
            "Hiện",
            FluentButtonKind.Subtle,
            72);
        reveal.AccessibleName = "Hiện hoặc ẩn khóa DeepL API";
        reveal.Click += (_, _) =>
        {
            deepLApiKey.UseSystemPasswordChar = !deepLApiKey.UseSystemPasswordChar;
            reveal.Text = deepLApiKey.UseSystemPasswordChar ? "Hiện" : "Ẩn";
        };
        var credentialInput = CreateInputFrame(deepLApiKey, reveal);
        credentialInput.Dock = DockStyle.Fill;
        credentialInput.Margin = new Padding(0, 4, 0, 4);
        credentialLayout.Controls.Add(credentialInput, 0, 2);
        credentialLayout.SetColumnSpan(credentialInput, 3);

        var actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4),
        };
        saveDeepLKey.Margin = new Padding(0, 0, 8, 0);
        removeDeepLKey.Margin = new Padding(0, 0, 8, 0);
        var openDeepLApiHelp = CreateButton(
            "openDeepLApiHelp",
            "Cách lấy khóa",
            FluentButtonKind.Subtle,
            128);
        openDeepLApiHelp.AccessibleDescription =
            "Mở tài liệu chính thức của DeepL về cách tìm khóa API.";
        openDeepLApiHelp.Click += (_, _) => OpenDeepLAuthenticationHelp();
        actionsPanel.Controls.Add(saveDeepLKey);
        actionsPanel.Controls.Add(removeDeepLKey);
        actionsPanel.Controls.Add(openDeepLApiHelp);
        credentialLayout.Controls.Add(actionsPanel, 0, 3);
        credentialLayout.SetColumnSpan(actionsPanel, 3);

        var privacyWarning = CreateLabel(
            "translationPrivacyWarning",
            "DeepL API Free nhận phần văn bản bạn chọn. Không dùng tính năng này cho dữ liệu cá nhân, bí mật hoặc nội dung nhạy cảm.",
            LabelRole.Tertiary);
        privacyWarning.Dock = DockStyle.Fill;
        credentialLayout.Controls.Add(privacyWarning, 0, 4);
        credentialLayout.SetColumnSpan(privacyWarning, 3);
        stack.Controls.Add(credentialCard);
        stack.Controls.Add(CreateLibreTranslateCard());

        var shortcutCard = CreateCard("translationShortcutCard", 112);
        var shortcutLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        shortcutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shortcutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shortcutLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        shortcutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shortcutCard.Controls.Add(shortcutLayout);
        var shortcutTitle = CreateLabel(
            "translationShortcutTitle",
            "Phím tắt Ctrl + Alt + T",
            LabelRole.Heading);
        shortcutTitle.Dock = DockStyle.Fill;
        shortcutLayout.Controls.Add(shortcutTitle, 0, 0);
        translationHotkeyStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        shortcutLayout.Controls.Add(translationHotkeyStatus, 1, 0);
        var shortcutHint = CreateLabel(
            "translationShortcutHint",
            "Nếu phím tắt bị ứng dụng khác chiếm, bộ gõ vẫn hoạt động và bạn vẫn có thể dịch từ menu khay hệ thống.",
            LabelRole.Secondary);
        shortcutHint.Dock = DockStyle.Fill;
        shortcutLayout.Controls.Add(shortcutHint, 0, 1);
        shortcutLayout.SetColumnSpan(shortcutHint, 2);
        stack.Controls.Add(shortcutCard);

        var behaviorCard = CreateCard("translationBehaviorCard", 126);
        behaviorCard.Controls.Add(CreateIconTextLayout(
            "\uE73E",
            "Giữ nguyên nội dung kỹ thuật và hoàn tác an toàn",
            "Code, URL, email, đường dẫn và placeholder được khóa bằng XML; Ctrl + Alt + Z hoàn tác một lần nếu focus và nội dung vẫn khớp."));
        stack.Controls.Add(behaviorCard);
        return page;
    }

    private Panel CreateHotkeysPage()
    {
        var page = CreatePage("hotkeysPage");
        var stack = CreateVerticalStack("hotkeysStack");
        page.Controls.Add(stack);

        stack.Controls.Add(CreateEditableShortcutRow(
            "hotkeyVietnamese",
            "\uE765",
            "Bật hoặc tắt bộ gõ tiếng Việt",
            "Tổ hợp chỉ gồm phím bổ trợ, không chặn thao tác trong ứng dụng hiện tại.",
            HotkeyCommand.ToggleVietnamese));
        stack.Controls.Add(CreateEditableShortcutRow(
            "hotkeyPushToTalk",
            "\uE720",
            "Giữ để nhập bằng giọng nói",
            "Bắt đầu khi nhấn và dừng ngay khi thả phím chính hoặc phím bổ trợ.",
            HotkeyCommand.PushToTalkPressed));
        stack.Controls.Add(CreateEditableShortcutRow(
            "hotkeyToggleDictation",
            "\uE8D4",
            "Bật hoặc tắt phiên nhập giọng nói",
            "Nhấn một lần để bắt đầu, nhấn lại để hoàn tất phiên đọc.",
            HotkeyCommand.ToggleDictation));
        stack.Controls.Add(CreateEditableShortcutRow(
            "hotkeyTranslation",
            "\uE8C1",
            "Dịch văn bản đang chọn",
            "Dịch sang ngôn ngữ đã cài đặt và giữ nguyên focus của ứng dụng.",
            HotkeyCommand.TranslateSelection));
        stack.Controls.Add(CreateEditableShortcutRow(
            "hotkeyUndoTranslation",
            "\uE7A7",
            "Hoàn tác bản dịch gần nhất",
            "Khôi phục văn bản gốc trong thời gian ngắn nếu focus và nội dung vẫn khớp.",
            HotkeyCommand.UndoTranslation));
        stack.Controls.Add(CreateEditableShortcutRow(
            "hotkeyCancel",
            "\uE711",
            "Hủy thao tác đang chạy",
            "Hủy phiên đọc hoặc yêu cầu dịch hiện tại mà không chèn nội dung dở dang.",
            HotkeyCommand.CancelDictation));

        var restoreCard = CreateCard("hotkeyRestoreCard", 88);
        var restoreLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        restoreLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        restoreLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        restoreLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        restoreLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        restoreCard.Controls.Add(restoreLayout);
        var restoreTitle = CreateLabel(
            "hotkeyRestoreTitle",
            "Khôi phục phím tắt mặc định",
            LabelRole.Heading);
        restoreTitle.Dock = DockStyle.Fill;
        restoreLayout.Controls.Add(restoreTitle, 0, 0);
        var resetAllHotkeys = CreateButton(
            "resetAllHotkeys",
            "Khôi phục tất cả",
            FluentButtonKind.Secondary,
            144);
        resetAllHotkeys.AccessibleDescription =
            "Đưa toàn bộ phím tắt về cấu hình mặc định của Keyina.";
        resetAllHotkeys.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        resetAllHotkeys.Click += (_, _) => actions.ResetAllHotkeys();
        restoreLayout.SetRowSpan(resetAllHotkeys, 2);
        restoreLayout.Controls.Add(resetAllHotkeys, 1, 0);
        var restoreDescription = CreateLabel(
            "hotkeyRestoreDescription",
            "Các thay đổi chỉ được lưu khi Windows đăng ký toàn bộ tổ hợp thành công.",
            LabelRole.Secondary);
        restoreDescription.Dock = DockStyle.Fill;
        restoreLayout.Controls.Add(restoreDescription, 0, 1);
        stack.Controls.Add(restoreCard);

        var feedbackCard = CreateCard("hotkeyFeedbackCard", 184);
        var feedbackLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        feedbackLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        feedbackLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        feedbackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        feedbackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        feedbackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        feedbackLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        feedbackCard.Controls.Add(feedbackLayout);

        var feedbackTitle = CreateLabel(
            "feedbackTitle",
            "Phản hồi khi dùng phím tắt",
            LabelRole.Heading);
        feedbackTitle.Dock = DockStyle.Fill;
        feedbackLayout.Controls.Add(feedbackTitle, 0, 0);
        previewFeedback.Height = 34;
        previewFeedback.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        feedbackLayout.Controls.Add(previewFeedback, 1, 0);

        var feedbackDescription = CreateLabel(
            "feedbackDescription",
            "Xác nhận lệnh bằng lớp phủ không chiếm focus và âm thanh ngắn.",
            LabelRole.Secondary);
        feedbackDescription.Dock = DockStyle.Fill;
        feedbackLayout.Controls.Add(feedbackDescription, 0, 1);
        feedbackLayout.SetColumnSpan(feedbackDescription, 2);

        var feedbackModeLabel = CreateLabel(
            "feedbackModeLabel",
            "Cách phản hồi",
            LabelRole.Caption);
        feedbackModeLabel.Dock = DockStyle.Fill;
        feedbackModeLabel.TextAlign = ContentAlignment.MiddleLeft;
        feedbackLayout.Controls.Add(feedbackModeLabel, 0, 2);
        feedbackMode.Anchor = AnchorStyles.Right;
        feedbackLayout.Controls.Add(feedbackMode, 1, 2);

        var feedbackNote = CreateLabel(
            "feedbackFullscreenNote",
            "Ở game hoặc ứng dụng toàn màn hình, chế độ Tự động chỉ phát âm thanh.",
            LabelRole.Tertiary);
        feedbackNote.Dock = DockStyle.Fill;
        feedbackLayout.Controls.Add(feedbackNote, 0, 3);
        feedbackLayout.SetColumnSpan(feedbackNote, 2);
        stack.Controls.Add(feedbackCard);

        var registration = CreateCard("hotkeyRegistrationCard", 112);
        var registrationLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4),
        };
        registrationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        registrationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        registrationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        registrationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        registration.Controls.Add(registrationLayout);
        var registrationTitle = CreateLabel(
            "hotkeyRegistrationTitle",
            "Trạng thái đăng ký",
            LabelRole.Heading);
        registrationTitle.Dock = DockStyle.Fill;
        registrationLayout.Controls.Add(registrationTitle, 0, 0);
        hotkeyStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        registrationLayout.Controls.Add(hotkeyStatus, 1, 0);
        var registrationText = CreateLabel(
            "hotkeyRegistrationText",
            "Các phím tắt được đăng ký toàn hệ thống và không chiếm focus của ứng dụng đang dùng.",
            LabelRole.Secondary);
        registrationText.Dock = DockStyle.Fill;
        registrationLayout.Controls.Add(registrationText, 0, 1);
        registrationLayout.SetColumnSpan(registrationText, 2);
        stack.Controls.Add(registration);
        return page;
    }

    private Panel CreateApplicationsPage()
    {
        var page = CreatePage("applicationsPage");
        var stack = CreateVerticalStack("applicationsStack");
        page.Controls.Add(stack);

        stack.Controls.Add(CreateApplicationRuleCard(
            "applicationTypingRule",
            "\uE765",
            "Tắt bộ gõ tiếng Việt",
            "Dùng cho game, terminal đặc biệt hoặc ứng dụng tự xử lý bàn phím.",
            disableVietnameseApplications));
        stack.Controls.Add(CreateApplicationRuleCard(
            "applicationSpeechRule",
            "\uE720",
            "Tắt nhập giọng nói",
            "Ngăn gửi âm thanh hoặc chèn transcript trong các ứng dụng đã chọn.",
            disableSpeechApplications));
        stack.Controls.Add(CreateApplicationRuleCard(
            "applicationTranslationRule",
            "\uE8C1",
            "Tắt dịch nhanh",
            "Ngăn văn bản được chọn gửi đến provider dịch trong các ứng dụng nhạy cảm.",
            disableTranslationApplications));
        stack.Controls.Add(CreateApplicationRuleCard(
            "applicationFeedbackRule",
            "\uE7F4",
            "Chỉ phát âm thanh phản hồi",
            "Ẩn overlay trong game hoặc ứng dụng toàn màn hình nhưng vẫn giữ âm báo ngắn.",
            suppressVisualFeedbackApplications));

        var saveCard = CreateCard("applicationRulesSaveCard", 104);
        var saveLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        saveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        saveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        saveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        saveLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        saveCard.Controls.Add(saveLayout);
        var saveTitle = CreateLabel(
            "applicationRulesTitle",
            "Áp dụng quy tắc ứng dụng",
            LabelRole.Heading);
        saveTitle.Dock = DockStyle.Fill;
        saveLayout.Controls.Add(saveTitle, 0, 0);
        var saveButton = CreateButton(
            "saveApplicationPreferences",
            "Lưu quy tắc",
            FluentButtonKind.Primary,
            128);
        saveButton.AccessibleDescription =
            "Kiểm tra toàn bộ tên file .exe rồi lưu atomically.";
        saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        saveButton.Click += (_, _) => SaveApplicationPreferences();
        saveLayout.SetRowSpan(saveButton, 2);
        saveLayout.Controls.Add(saveButton, 1, 0);
        applicationRulesStatus.Dock = DockStyle.Fill;
        saveLayout.Controls.Add(applicationRulesStatus, 0, 1);
        stack.Controls.Add(saveCard);
        return page;
    }

    private FluentCard CreateApplicationRuleCard(
        string name,
        string glyph,
        string title,
        string description,
        TextBox textBox)
    {
        var card = CreateCard(name, 160);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);

        var icon = CreateIconLabel(name + "Icon", glyph, 14F);
        icon.Dock = DockStyle.Fill;
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);
        var titleLabel = CreateLabel(name + "Title", title, LabelRole.Heading);
        titleLabel.Dock = DockStyle.Fill;
        layout.Controls.Add(titleLabel, 1, 0);
        var descriptionLabel = CreateLabel(
            name + "Description",
            description,
            LabelRole.Secondary);
        descriptionLabel.Dock = DockStyle.Fill;
        layout.Controls.Add(descriptionLabel, 1, 1);
        layout.SetColumnSpan(descriptionLabel, 2);

        var addCurrent = CreateButton(
            name + "AddCurrent",
            "Thêm app hiện tại",
            FluentButtonKind.Secondary,
            144);
        addCurrent.AccessibleDescription =
            $"Thêm tên file của ứng dụng đang focus vào danh sách {title}.";
        addCurrent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addCurrent.Click += (_, _) => AddForegroundApplication(textBox);
        layout.Controls.Add(addCurrent, 2, 0);

        var input = CreateInputFrame(textBox);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(input, 1, 2);
        layout.SetColumnSpan(input, 2);
        return card;
    }

    private Panel CreateSnippetsPage()
    {
        var page = CreatePage("snippetsPage");
        var stack = CreateVerticalStack("snippetsStack");
        page.Controls.Add(stack);

        var library = CreateCard("snippetsLibraryCard", 490);
        var libraryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        library.Controls.Add(libraryLayout);

        var libraryTitle = CreateLabel("snippetsTitle", "Thư viện gõ tắt", LabelRole.Heading);
        libraryTitle.Dock = DockStyle.Fill;
        libraryLayout.Controls.Add(libraryTitle, 0, 0);
        snippetCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        snippetCount.TextAlign = ContentAlignment.MiddleRight;
        libraryLayout.Controls.Add(snippetCount, 1, 0);

        var searchToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4),
        };
        searchToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176F));
        var searchFrame = CreateInputFrame(snippetsSearch);
        searchFrame.Dock = DockStyle.Fill;
        searchFrame.Margin = new Padding(0, 0, 8, 0);
        searchToolbar.Controls.Add(searchFrame, 0, 0);
        snippetsFilter.Dock = DockStyle.Fill;
        searchToolbar.Controls.Add(snippetsFilter, 1, 0);
        libraryLayout.Controls.Add(searchToolbar, 0, 1);
        libraryLayout.SetColumnSpan(searchToolbar, 2);

        AddSnippetRows();
        libraryLayout.Controls.Add(snippetsList, 0, 2);
        libraryLayout.SetColumnSpan(snippetsList, 2);

        var snippetFooter = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        snippetFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        snippetFooter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var snippetNote = CreateLabel(
            "snippetPrivacyNote",
            "Gõ trigger rồi nhấn Space. Mỗi snippet có thể giữ hoặc nuốt phím kích hoạt; trường bảo mật luôn được bỏ qua.",
            LabelRole.Tertiary);
        snippetNote.Dock = DockStyle.Fill;
        snippetNote.TextAlign = ContentAlignment.MiddleLeft;
        snippetFooter.Controls.Add(snippetNote, 0, 0);
        var addSnippet = CreateButton(
            "addSnippet",
            "Thêm gõ tắt",
            FluentButtonKind.Primary,
            132);
        addSnippet.Click += (_, _) => AddCustomSnippet();
        snippetFooter.Controls.Add(addSnippet, 1, 0);
        libraryLayout.Controls.Add(snippetFooter, 0, 3);
        libraryLayout.SetColumnSpan(snippetFooter, 2);

        stack.Controls.Add(library);
        return page;
    }

    private Panel CreateDiagnosticsPage()
    {
        var page = CreatePage("diagnosticsPage");
        var stack = CreateVerticalStack("diagnosticsStack");
        page.Controls.Add(stack);

        var checks = CreateCard("diagnosticChecksCard", 250);
        var checksLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        checksLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        checksLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        checksLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        checksLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        checksLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        checks.Controls.Add(checksLayout);
        var checksTitle = CreateLabel("diagnosticsTitle", "Kiểm tra cục bộ", LabelRole.Heading);
        checksTitle.Dock = DockStyle.Fill;
        checksLayout.Controls.Add(checksTitle, 0, 0);
        checksLayout.Controls.Add(CreateDiagnosticRow("\uE7BA", "Resident bộ gõ", hotkeyStatus), 0, 1);
        checksLayout.Controls.Add(CreateDiagnosticRow("\uE765", "Bộ gõ và snippet", inputStatus), 0, 2);
        checksLayout.Controls.Add(CreateDiagnosticRow("\uE8C8", "Kết nối native", ipcStatus), 0, 3);
        checksLayout.Controls.Add(CreateDiagnosticRow("\uE720", "Dịch vụ giọng nói", speechStatus), 0, 4);
        stack.Controls.Add(checks);

        var resultCard = CreateCard("diagnosticsResultCard", 188);
        var resultLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        resultCard.Controls.Add(resultLayout);
        var resultTitle = CreateLabel("diagnosticsResultTitle", "Kết quả gần nhất", LabelRole.Heading);
        resultTitle.Dock = DockStyle.Fill;
        resultLayout.Controls.Add(resultTitle, 0, 0);
        diagnosticsResult.Dock = DockStyle.Fill;
        diagnosticsResult.Padding = new Padding(0, 4, 0, 4);
        resultLayout.Controls.Add(diagnosticsResult, 0, 1);

        var actionBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };
        var run = CreateButton(
            "runDiagnostics",
            "Chạy kiểm tra",
            FluentButtonKind.Primary,
            132);
        var folder = CreateButton(
            "openConfigFolder",
            "Mở thư mục cấu hình",
            FluentButtonKind.Secondary,
            168);
        var copy = CreateButton(
            "copyDiagnostics",
            "Sao chép báo cáo",
            FluentButtonKind.Secondary,
            150);
        run.Margin = new Padding(0, 0, 8, 0);
        folder.Margin = new Padding(0, 0, 8, 0);
        copy.Margin = Padding.Empty;
        run.Click += async (_, _) => await RunDiagnosticsAsync(run).ConfigureAwait(true);
        folder.Click += (_, _) => actions.OpenConfigurationFolder();
        copy.Click += (_, _) => CopyDiagnostics();
        actionBar.Controls.Add(run);
        actionBar.Controls.Add(folder);
        actionBar.Controls.Add(copy);
        resultLayout.Controls.Add(actionBar, 0, 2);
        stack.Controls.Add(resultCard);

        var typingDiagnosticCard = CreateCard("typingDiagnosticCard", 560);
        var typingDiagnosticLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        typingDiagnosticLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        typingDiagnosticLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        typingDiagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        typingDiagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        typingDiagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
        typingDiagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        typingDiagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        typingDiagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        typingDiagnosticCard.Controls.Add(typingDiagnosticLayout);

        var typingDiagnosticTitle = CreateLabel(
            "typingDiagnosticTitle",
            "Sandbox debug bộ gõ",
            LabelRole.Heading);
        typingDiagnosticTitle.Dock = DockStyle.Fill;
        typingDiagnosticTitle.TextAlign = ContentAlignment.MiddleLeft;
        typingDiagnosticLayout.Controls.Add(typingDiagnosticTitle, 0, 0);
        typingDiagnosticStatus.AutoSize = true;
        typingDiagnosticStatus.Anchor = AnchorStyles.Right;
        typingDiagnosticStatus.Margin = new Padding(12, 8, 0, 0);
        typingDiagnosticLayout.Controls.Add(typingDiagnosticStatus, 1, 0);

        var typingDiagnosticPrivacy = CreateLabel(
            "typingDiagnosticPrivacy",
            "Chỉ ô này ghi phím thô và nội dung hiển thị. Rời focus sẽ tạm dừng; Keyina không ghi ở ứng dụng khác.",
            LabelRole.Secondary);
        typingDiagnosticPrivacy.Dock = DockStyle.Fill;
        typingDiagnosticPrivacy.TextAlign = ContentAlignment.MiddleLeft;
        typingDiagnosticPrivacy.AccessibleDescription =
            "Raw trace is active only for this focused diagnostic input and remains in memory until cleared or the window closes.";
        typingDiagnosticLayout.Controls.Add(typingDiagnosticPrivacy, 0, 1);
        typingDiagnosticLayout.SetColumnSpan(typingDiagnosticPrivacy, 2);

        var typingDiagnosticInputFrame = CreateInputFrame(typingDiagnosticInput);
        typingDiagnosticInputFrame.Height = 96;
        typingDiagnosticInputFrame.Dock = DockStyle.Fill;
        typingDiagnosticInputFrame.Padding = new Padding(12, 10, 12, 10);
        typingDiagnosticLayout.Controls.Add(typingDiagnosticInputFrame, 0, 2);
        typingDiagnosticLayout.SetColumnSpan(typingDiagnosticInputFrame, 2);

        var typingDiagnosticLogTitle = CreateLabel(
            "typingDiagnosticLogTitle",
            "Dòng sự kiện",
            LabelRole.Caption);
        typingDiagnosticLogTitle.Dock = DockStyle.Fill;
        typingDiagnosticLogTitle.TextAlign = ContentAlignment.MiddleLeft;
        typingDiagnosticLayout.Controls.Add(typingDiagnosticLogTitle, 0, 3);
        typingDiagnosticFilter.Anchor = AnchorStyles.Right;
        typingDiagnosticFilter.Margin = new Padding(12, 4, 0, 4);
        typingDiagnosticLayout.Controls.Add(typingDiagnosticFilter, 1, 3);

        var typingDiagnosticLogFrame = CreateInputFrame(typingDiagnosticLog);
        typingDiagnosticLogFrame.Dock = DockStyle.Fill;
        typingDiagnosticLogFrame.Padding = new Padding(12, 10, 12, 10);
        typingDiagnosticLayout.Controls.Add(typingDiagnosticLogFrame, 0, 4);
        typingDiagnosticLayout.SetColumnSpan(typingDiagnosticLogFrame, 2);

        var typingDiagnosticActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 0),
        };
        var clearTypingDiagnostic = CreateButton(
            "clearTypingDiagnostic",
            "Xóa log",
            FluentButtonKind.Secondary,
            108);
        var copyTypingDiagnostic = CreateButton(
            "copyTypingDiagnostic",
            "Sao chép log",
            FluentButtonKind.Primary,
            142);
        var exportTypingDiagnostic = CreateButton(
            "exportTypingDiagnostic",
            "Xuất log",
            FluentButtonKind.Secondary,
            112);
        clearTypingDiagnostic.Margin = new Padding(0, 0, 8, 0);
        copyTypingDiagnostic.Margin = new Padding(0, 0, 8, 0);
        exportTypingDiagnostic.Margin = Padding.Empty;
        clearTypingDiagnostic.Click += (_, _) => ClearTypingDiagnosticLog();
        copyTypingDiagnostic.Click += (_, _) => CopyTypingDiagnosticLog();
        exportTypingDiagnostic.Click += (_, _) => ExportTypingDiagnosticLog();
        typingDiagnosticActions.Controls.Add(clearTypingDiagnostic);
        typingDiagnosticActions.Controls.Add(copyTypingDiagnostic);
        typingDiagnosticActions.Controls.Add(exportTypingDiagnostic);
        typingDiagnosticLayout.Controls.Add(typingDiagnosticActions, 0, 5);
        typingDiagnosticLayout.SetColumnSpan(typingDiagnosticActions, 2);
        stack.Controls.Add(typingDiagnosticCard);

        var latencyCard = CreateCard("typingLatencyCard", 352);
        var latencyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        latencyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        latencyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        latencyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        latencyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        latencyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        latencyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        latencyCard.Controls.Add(latencyLayout);

        var latencyTitle = CreateLabel(
            "typingLatencyTitle",
            "Độ trễ đường gõ",
            LabelRole.Heading);
        latencyTitle.Dock = DockStyle.Fill;
        latencyTitle.TextAlign = ContentAlignment.MiddleLeft;
        latencyLayout.Controls.Add(latencyTitle, 0, 0);
        typingLatencyToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        typingLatencyToggle.Margin = new Padding(8, 4, 0, 0);
        latencyLayout.Controls.Add(typingLatencyToggle, 1, 0);

        var latencyPrivacy = CreateLabel(
            "typingLatencyPrivacy",
            "Chỉ đo thời gian từng công đoạn; không ghi nội dung đã gõ, phím thô hoặc clipboard.",
            LabelRole.Secondary);
        latencyPrivacy.Dock = DockStyle.Fill;
        latencyPrivacy.TextAlign = ContentAlignment.MiddleLeft;
        latencyPrivacy.AccessibleDescription =
            "Bộ đo cục bộ chỉ lưu thống kê thời gian và không lưu nội dung người dùng.";
        latencyLayout.Controls.Add(latencyPrivacy, 0, 1);
        latencyLayout.SetColumnSpan(latencyPrivacy, 2);

        latencyLayout.Controls.Add(typingLatencyTable, 0, 2);
        latencyLayout.SetColumnSpan(typingLatencyTable, 2);

        var latencyActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 5, 0, 0),
        };
        var refreshLatency = CreateButton(
            "refreshTypingLatency",
            "Làm mới số đo",
            FluentButtonKind.Primary,
            142);
        var clearLatency = CreateButton(
            "clearTypingLatency",
            "Xóa số đo",
            FluentButtonKind.Secondary,
            120);
        refreshLatency.Margin = new Padding(0, 0, 8, 0);
        clearLatency.Margin = Padding.Empty;
        refreshLatency.Click += (_, _) => RefreshTypingLatency();
        clearLatency.Click += (_, _) =>
        {
            actions.ClearTypingLatency();
            typingLatencyTable.Items.Clear();
        };
        latencyActions.Controls.Add(refreshLatency);
        latencyActions.Controls.Add(clearLatency);
        latencyLayout.Controls.Add(latencyActions, 0, 3);
        latencyLayout.SetColumnSpan(latencyActions, 2);
        stack.Controls.Add(latencyCard);

        var portabilityCard = CreateCard("settingsPortabilityCard", 152);
        var portabilityLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        portabilityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        portabilityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        portabilityLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        portabilityCard.Controls.Add(portabilityLayout);
        var portabilityTitle = CreateLabel(
            "settingsPortabilityTitle",
            "Sao lưu và khôi phục cài đặt",
            LabelRole.Heading);
        portabilityTitle.Dock = DockStyle.Fill;
        portabilityLayout.Controls.Add(portabilityTitle, 0, 0);
        var portabilityActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 6),
        };
        var exportSettings = CreateButton(
            "exportSettings",
            "Xuất cài đặt",
            FluentButtonKind.Primary,
            132);
        var importSettings = CreateButton(
            "importSettings",
            "Nhập cài đặt",
            FluentButtonKind.Secondary,
            132);
        exportSettings.AccessibleDescription =
            "Xuất preferences và gõ tắt sang JSON, không bao gồm API key.";
        importSettings.AccessibleDescription =
            "Nhập file JSON đã được Keyina kiểm tra đầy đủ trước khi áp dụng.";
        exportSettings.Margin = new Padding(0, 0, 8, 0);
        importSettings.Margin = Padding.Empty;
        exportSettings.Click += (_, _) => ExportSettings();
        importSettings.Click += (_, _) => ImportSettings();
        portabilityActions.Controls.Add(exportSettings);
        portabilityActions.Controls.Add(importSettings);
        portabilityLayout.Controls.Add(portabilityActions, 0, 1);
        var portabilityPrivacy = CreateLabel(
            "settingsPortabilityPrivacy",
            "API key, transcript, clipboard và nội dung đã gõ không bao giờ được đưa vào file xuất.",
            LabelRole.Tertiary);
        portabilityPrivacy.AccessibleDescription =
            "File sao lưu chỉ chứa cài đặt không nhạy cảm; credential vẫn nằm trong Windows Credential Manager.";
        portabilityPrivacy.Dock = DockStyle.Fill;
        portabilityLayout.Controls.Add(portabilityPrivacy, 0, 2);
        stack.Controls.Add(portabilityCard);

        var privacy = CreateCard("diagnosticsPrivacyCard", 116);
        privacy.Controls.Add(CreateIconTextLayout(
            "\uEA18",
            "Không thu thập ngoài sandbox",
            "Báo cáo thường không đọc transcript, âm thanh, clipboard hoặc phím thô; sandbox chỉ ghi đúng ô đang focus."));
        stack.Controls.Add(privacy);
        return page;
    }

    private ListView CreateTypingLatencyTable()
    {
        var table = new ListView
        {
            Name = "typingLatencyTable",
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            AccessibleName = "Bảng độ trễ đường gõ",
            AccessibleDescription =
                "Thống kê thời gian theo công đoạn, không chứa nội dung đã gõ.",
        };
        table.Columns.Add("Công đoạn", 154, HorizontalAlignment.Left);
        table.Columns.Add("Mẫu", 72, HorizontalAlignment.Right);
        table.Columns.Add("P50", 88, HorizontalAlignment.Right);
        table.Columns.Add("P95", 88, HorizontalAlignment.Right);
        table.Columns.Add("P99", 88, HorizontalAlignment.Right);
        table.Columns.Add("Tối đa", 96, HorizontalAlignment.Right);
        table.Columns.Add("Trung bình", 96, HorizontalAlignment.Right);
        table.DrawColumnHeader += (_, eventArgs) =>
        {
            if (eventArgs.Header is not { } header)
            {
                return;
            }
            using var background = new SolidBrush(palette.SurfaceSecondary);
            using var separator = new Pen(palette.Border);
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
            eventArgs.Graphics.DrawLine(
                separator,
                eventArgs.Bounds.Left,
                eventArgs.Bounds.Bottom - 1,
                eventArgs.Bounds.Right,
                eventArgs.Bounds.Bottom - 1);
            var alignment = header.TextAlign == HorizontalAlignment.Right
                ? TextFormatFlags.Right
                : TextFormatFlags.Left;
            using var headerFont = new Font(Font.FontFamily, 8.75F, FontStyle.Bold);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                header.Text,
                headerFont,
                Rectangle.Inflate(eventArgs.Bounds, -10, 0),
                palette.TextSecondary,
                alignment |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        };
        table.DrawItem += (_, eventArgs) =>
        {
            if (eventArgs.Item is not { } item)
            {
                return;
            }
            var backgroundColor = item.Selected
                ? palette.SurfacePressed
                : eventArgs.ItemIndex % 2 == 0
                    ? palette.Surface
                    : palette.SurfaceSecondary;
            using var background = new SolidBrush(backgroundColor);
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
        };
        table.DrawSubItem += (_, eventArgs) =>
        {
            if (eventArgs.Item is not { } item || eventArgs.SubItem is not { } subItem)
            {
                return;
            }
            var backgroundColor = item.Selected
                ? palette.SurfacePressed
                : eventArgs.ItemIndex % 2 == 0
                    ? palette.Surface
                    : palette.SurfaceSecondary;
            using var background = new SolidBrush(backgroundColor);
            using var separator = new Pen(palette.Border);
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
            eventArgs.Graphics.DrawLine(
                separator,
                eventArgs.Bounds.Left,
                eventArgs.Bounds.Bottom - 1,
                eventArgs.Bounds.Right,
                eventArgs.Bounds.Bottom - 1);
            var alignment = eventArgs.Header?.TextAlign == HorizontalAlignment.Right
                ? TextFormatFlags.Right
                : TextFormatFlags.Left;
            var font = eventArgs.ColumnIndex == 0
                ? new Font(Font.FontFamily, 9F, FontStyle.Bold)
                : new Font(Font.FontFamily, 9F, FontStyle.Regular);
            using (font)
            {
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    subItem.Text,
                    font,
                    Rectangle.Inflate(eventArgs.Bounds, -10, 0),
                    item.Selected ? palette.TextPrimary :
                        eventArgs.ColumnIndex == 0 ? palette.TextPrimary : palette.TextSecondary,
                    alignment |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
        };
        table.Resize += (_, _) => ResizeTypingLatencyColumns(table);
        return table;
    }

    private void RefreshTypingLatency()
    {
        var snapshots = actions.GetTypingLatencySnapshot();
        typingLatencyTable.BeginUpdate();
        try
        {
            typingLatencyTable.Items.Clear();
            foreach (var snapshot in snapshots)
            {
                var item = new ListViewItem(GetTypingLatencyStageLabel(snapshot.Stage))
                {
                    Tag = snapshot,
                };
                item.SubItems.Add(snapshot.SampleCount.ToString("N0", CultureInfo.CurrentCulture));
                item.SubItems.Add(FormatLatency(snapshot.MedianNanoseconds));
                item.SubItems.Add(FormatLatency(snapshot.P95Nanoseconds));
                item.SubItems.Add(FormatLatency(snapshot.P99Nanoseconds));
                item.SubItems.Add(FormatLatency(snapshot.MaximumNanoseconds));
                item.SubItems.Add(FormatLatency(snapshot.MeanNanoseconds));
                typingLatencyTable.Items.Add(item);
            }
        }
        finally
        {
            typingLatencyTable.EndUpdate();
        }
    }

    private static string GetTypingLatencyStageLabel(TypingLatencyStage stage) => stage switch
    {
        TypingLatencyStage.CallbackTotal => "Toàn bộ callback",
        TypingLatencyStage.ForegroundContext => "Ngữ cảnh cửa sổ",
        TypingLatencyStage.SafetyGuard => "Kiểm tra an toàn",
        TypingLatencyStage.EngineProcess => "Engine",
        TypingLatencyStage.InputInjection => "Chèn ký tự",
        _ => stage.ToString(),
    };

    private static string FormatLatency(double nanoseconds)
    {
        if (nanoseconds <= 0)
        {
            return "—";
        }
        if (nanoseconds < 1_000)
        {
            return $"{nanoseconds:N0} ns";
        }
        if (nanoseconds < 1_000_000)
        {
            return $"{nanoseconds / 1_000D:N2} µs";
        }
        return $"{nanoseconds / 1_000_000D:N2} ms";
    }

    private static void ResizeTypingLatencyColumns(ListView table)
    {
        if (table.Columns.Count != 7 || table.ClientSize.Width <= 0)
        {
            return;
        }

        var fixedWidth = 72 + 88 + 88 + 88 + 96 + 96;
        table.Columns[0].Width = Math.Max(142, table.ClientSize.Width - fixedWidth - 4);
    }

    private FluentCard CreateStatusCard(
        string name,
        string glyph,
        string title,
        FluentStatusBadge badge,
        string detail)
    {
        var card = CreateCard(name, 110);
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 10, 10);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);
        var icon = CreateIconLabel(name + "Icon", glyph, 14F);
        icon.Dock = DockStyle.Fill;
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);
        var titleLabel = CreateLabel(name + "Title", title, LabelRole.Heading);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(titleLabel, 1, 0);
        badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        layout.Controls.Add(badge, 2, 0);
        var detailLabel = CreateLabel(name + "Detail", detail, LabelRole.Secondary);
        detailLabel.Dock = DockStyle.Fill;
        layout.Controls.Add(detailLabel, 1, 1);
        layout.SetColumnSpan(detailLabel, 2);
        return card;
    }

    private FluentCard CreateSettingRow(
        string name,
        string glyph,
        string title,
        string description,
        string metadata,
        FluentToggle toggle)
    {
        var card = CreateCard(name, 92);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);

        var icon = CreateIconLabel(name + "Icon", glyph, 14F);
        icon.Dock = DockStyle.Fill;
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);
        var titleLabel = CreateLabel(name + "Title", title, LabelRole.Heading);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(titleLabel, 1, 0);
        var metadataLabel = CreateLabel(name + "Metadata", metadata, LabelRole.Caption);
        metadataLabel.AutoSize = true;
        metadataLabel.Anchor = AnchorStyles.Right;
        metadataLabel.Margin = new Padding(12, 0, 12, 0);
        layout.Controls.Add(metadataLabel, 2, 0);
        toggle.Anchor = AnchorStyles.Right;
        toggle.Margin = new Padding(8, 4, 0, 0);
        layout.SetRowSpan(toggle, 2);
        layout.Controls.Add(toggle, 3, 0);
        var descriptionLabel = CreateLabel(name + "Description", description, LabelRole.Secondary);
        descriptionLabel.Dock = DockStyle.Fill;
        layout.Controls.Add(descriptionLabel, 1, 1);
        layout.SetColumnSpan(descriptionLabel, 2);
        return card;
    }

    private FluentCard CreateEditableShortcutRow(
        string name,
        string glyph,
        string title,
        string description,
        HotkeyCommand command)
    {
        var card = CreateCard(name, 92);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);

        var icon = CreateIconLabel(name + "Icon", glyph, 14F);
        icon.Dock = DockStyle.Fill;
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);

        var titleLabel = CreateLabel(name + "Title", title, LabelRole.Heading);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(titleLabel, 1, 0);

        var descriptionLabel = CreateLabel(
            name + "Description",
            description,
            LabelRole.Secondary);
        descriptionLabel.Dock = DockStyle.Fill;
        descriptionLabel.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(descriptionLabel, 1, 1);

        var keycap = CreateLabel(
            name + "Keycap",
            HotkeyText.Format(currentSnapshot.Hotkeys.GetPreference(command).Chord),
            LabelRole.Caption);
        keycap.AccessibleName = $"Phím tắt hiện tại cho {title}";
        keycap.Dock = DockStyle.Fill;
        keycap.TextAlign = ContentAlignment.MiddleCenter;
        keycap.Margin = new Padding(6, 6, 10, 6);
        keycap.Paint += KeycapPaint;
        layout.SetRowSpan(keycap, 2);
        layout.Controls.Add(keycap, 2, 0);
        hotkeyKeycaps.Add(command, keycap);

        var changeButton = CreateButton(
            name + "Change",
            "Đổi",
            FluentButtonKind.Primary,
            76);
        changeButton.AccessibleName = $"Đổi phím tắt cho {title}";
        changeButton.Anchor = AnchorStyles.None;
        changeButton.Click += (_, _) => EditHotkey(command);
        layout.SetRowSpan(changeButton, 2);
        layout.Controls.Add(changeButton, 3, 0);

        var resetButton = CreateButton(
            name + "Reset",
            "Khôi phục",
            FluentButtonKind.Subtle,
            90);
        resetButton.AccessibleName = $"Khôi phục phím tắt mặc định cho {title}";
        resetButton.Anchor = AnchorStyles.None;
        resetButton.Click += (_, _) => actions.ResetHotkey(command);
        layout.SetRowSpan(resetButton, 2);
        layout.Controls.Add(resetButton, 4, 0);
        return card;
    }

    private TableLayoutPanel CreateDiagnosticRow(string glyph, string title, FluentStatusBadge badge)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var icon = CreateIconLabel(title + "Icon", glyph, 12F);
        icon.Dock = DockStyle.Fill;
        row.Controls.Add(icon, 0, 0);
        var titleLabel = CreateLabel(title + "Label", title, LabelRole.Primary);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(titleLabel, 1, 0);
        badge.Anchor = AnchorStyles.Right;
        row.Controls.Add(badge, 2, 0);
        return row;
    }

    private TableLayoutPanel CreateIconTextLayout(string glyph, string title, string detail)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var icon = CreateIconLabel(title + "Icon", glyph, 15F);
        icon.Dock = DockStyle.Fill;
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);
        var titleLabel = CreateLabel(title + "Title", title, LabelRole.Heading);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(titleLabel, 1, 0);
        var detailLabel = CreateLabel(title + "Detail", detail, LabelRole.Secondary);
        detailLabel.Dock = DockStyle.Fill;
        layout.Controls.Add(detailLabel, 1, 1);
        return layout;
    }

    private void AddSnippetRows()
    {
        if (snippetsList is null)
        {
            return;
        }
        snippetsList.SuspendLayout();
        snippetsList.Controls.Clear();
        foreach (var snippet in SnippetRow.All.Concat(
                     currentSnapshot.Snippets.Select(configuration => new SnippetRow(
                         configuration.Trigger,
                         configuration.Execution is null
                             ? configuration.Expansion
                             : $"{Path.GetFileName(configuration.Execution.ExecutablePath)} {configuration.Execution.Arguments}".Trim(),
                         configuration.Execution is not null
                             ? "Chương trình · chèn stdout"
                             : configuration.PreserveDelimiter
                                 ? "Văn bản · giữ Space"
                                 : "Văn bản · nuốt Space",
                         configuration))))
        {
            snippetsList.Controls.Add(CreateSnippetRow(snippet));
        }
        ResizeStackChildren(snippetsList);
        FilterSnippets(snippetsSearch.Text);
        snippetsList.ResumeLayout();
    }

    private FluentCard CreateSnippetRow(SnippetRow snippet)
    {
        var card = CreateCard("snippet_" + snippet.Trigger.TrimStart(';'), 62);
        card.Tag = snippet;
        card.Margin = new Padding(0, 0, 0, 6);
        card.UseSecondarySurface = true;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = snippet.Configuration is null ? 3 : 6,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        if (snippet.Configuration is not null)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
        }
        card.Controls.Add(layout);
        var trigger = CreateLabel(card.Name + "Trigger", snippet.Trigger, LabelRole.Caption);
        trigger.Dock = DockStyle.Fill;
        trigger.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(trigger, 0, 0);
        var expansion = CreateLabel(card.Name + "Expansion", snippet.Expansion, LabelRole.Primary);
        expansion.Dock = DockStyle.Fill;
        expansion.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(expansion, 1, 0);
        var scope = CreateLabel(card.Name + "Scope", snippet.Scope, LabelRole.Tertiary);
        scope.AutoSize = true;
        scope.Anchor = AnchorStyles.Right;
        layout.Controls.Add(scope, 2, 0);
        if (snippet.Configuration is not null)
        {
            var duplicate = CreateButton(card.Name + "Duplicate", "Nhân bản", FluentButtonKind.Secondary, 72);
            duplicate.Height = 30;
            duplicate.Click += (_, _) => DuplicateSnippet(snippet);
            layout.Controls.Add(duplicate, 3, 0);
            var edit = CreateButton(card.Name + "Edit", "Sửa", FluentButtonKind.Secondary, 64);
            edit.Height = 30;
            edit.Click += (_, _) => EditCustomSnippet(snippet.Configuration);
            layout.Controls.Add(edit, 4, 0);
            var delete = CreateButton(card.Name + "Delete", "Xóa", FluentButtonKind.Danger, 64);
            delete.Height = 30;
            delete.Click += (_, _) => DeleteCustomSnippet(snippet.Configuration);
            layout.Controls.Add(delete, 5, 0);
        }
        return card;
    }

    private void AddCustomSnippet()
    {
        using var dialog = new SnippetEditorDialog(
            null,
            currentSnapshot.Snippets.Select(snippet => snippet.Trigger).ToArray());
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
        {
            ApplySnippetChanges(currentSnapshot.Snippets.Append(dialog.Result).ToArray());
        }
    }

    private void DuplicateSnippet(SnippetRow source)
    {
        var baseConfiguration = source.Configuration ?? new SnippetConfiguration(
            ";kcopy",
            source.Expansion,
            CaseSensitive: false,
            PreserveDelimiter: false,
            Delimiters: " ",
            AllowedApplications: [],
            ExcludedApplications: []);
        var suggestedTrigger = CreateUniqueSnippetTrigger(baseConfiguration.Trigger + "copy");
        var duplicate = baseConfiguration with { Trigger = suggestedTrigger };
        using var dialog = new SnippetEditorDialog(
            duplicate,
            currentSnapshot.Snippets.Select(snippet => snippet.Trigger).ToArray());
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
        {
            ApplySnippetChanges(currentSnapshot.Snippets.Append(dialog.Result).ToArray());
        }
    }

    private string CreateUniqueSnippetTrigger(string candidate)
    {
        var normalized = candidate.StartsWith(";k", StringComparison.OrdinalIgnoreCase)
            ? candidate
            : ";k" + candidate.TrimStart(';');
        var existing = currentSnapshot.Snippets
            .Select(snippet => snippet.Trigger)
            .Concat(SnippetRow.All.Select(snippet => snippet.Trigger))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(normalized))
        {
            return normalized;
        }
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var value = normalized + suffix.ToString(CultureInfo.InvariantCulture);
            if (!existing.Contains(value))
            {
                return value;
            }
        }
        return ";kcopy" + Guid.NewGuid().ToString("N")[..8];
    }

    private void EditCustomSnippet(SnippetConfiguration original)
    {
        using var dialog = new SnippetEditorDialog(
            original,
            currentSnapshot.Snippets.Select(snippet => snippet.Trigger).ToArray());
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
        {
            ApplySnippetChanges(currentSnapshot.Snippets
                .Select(snippet => ReferenceEquals(snippet, original) || snippet == original ? dialog.Result : snippet)
                .ToArray());
        }
    }

    private void DeleteCustomSnippet(SnippetConfiguration snippet)
    {
        var result = MessageBox.Show(
            this,
            $"Xóa gõ tắt {snippet.Trigger}?",
            "Xóa gõ tắt",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result == DialogResult.Yes)
        {
            ApplySnippetChanges(currentSnapshot.Snippets.Where(item => item != snippet).ToArray());
        }
    }

    private void ApplySnippetChanges(SnippetConfiguration[] snippets)
    {
        actions.SetSnippets(snippets);
        currentSnapshot = currentSnapshot with { CustomSnippetCount = snippets.Length };
        currentSnapshot = currentSnapshot with { Snippets = snippets };
        snippetCount.Text = snippets.Length == 1 ? "1 gõ tắt tùy chỉnh" : $"{snippets.Length} gõ tắt tùy chỉnh";
        AddSnippetRows();
    }

    private void FilterSnippets(string query)
    {
        var normalized = query.Trim();
        var filterIndex = snippetsFilter.SelectedIndex;
        foreach (Control child in snippetsList.Controls)
        {
            if (child.Tag is not SnippetRow snippet)
            {
                continue;
            }
            var textMatches = normalized.Length == 0 ||
                snippet.Trigger.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                snippet.Expansion.Contains(normalized, StringComparison.OrdinalIgnoreCase);
            var typeMatches = filterIndex switch
            {
                1 => snippet.Configuration is null,
                2 => snippet.Configuration is not null,
                3 => snippet.Configuration?.Execution is not null,
                _ => true,
            };
            child.Visible = textMatches && typeMatches;
        }
    }

    private static Panel CreatePage(string name) => new()
    {
        Name = name,
        Dock = DockStyle.Fill,
        Visible = false,
        AutoScroll = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
    };

    private static FlowLayoutPanel CreateVerticalStack(string name)
    {
        var stack = new FlowLayoutPanel
        {
            Name = name,
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 8, 8),
        };
        stack.SizeChanged += (_, _) => ResizeStackChildren(stack);
        stack.ControlAdded += (_, _) => ResizeStackChildren(stack);
        return stack;
    }

    private static void ResizeStackChildren(FlowLayoutPanel stack)
    {
        var available = Math.Max(520, stack.ClientSize.Width - stack.Padding.Horizontal - 4);
        foreach (Control child in stack.Controls)
        {
            child.Width = Math.Max(0, available - child.Margin.Horizontal);
        }
    }

    private FluentCard CreateCard(string name, int height) => new()
    {
        Name = name,
        Height = height,
        Palette = palette,
    };

    private Label CreateLabel(string name, string text, LabelRole role)
    {
        var label = new Label
        {
            Name = name,
            Text = text,
            AutoSize = false,
            UseMnemonic = false,
            Tag = role,
            Margin = Padding.Empty,
        };
        label.Font = role switch
        {
            LabelRole.Title => new Font("Segoe UI Variable Display", 20F, FontStyle.Bold),
            LabelRole.Heading => new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
            LabelRole.Caption => new Font(Font.FontFamily, 9F, FontStyle.Bold),
            _ => new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
        };
        return label;
    }

    private static Label CreateIconLabel(string name, string glyph, float size) => new()
    {
        Name = name,
        Text = glyph,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe Fluent Icons", size, FontStyle.Regular),
        Tag = LabelRole.Icon,
        Margin = Padding.Empty,
        AccessibleName = string.Empty,
    };

    private FluentStatusBadge CreateBadge(string name, int width) => new()
    {
        Name = name,
        Width = width,
        Font = new Font(Font.FontFamily, 8.75F, FontStyle.Bold),
        Palette = palette,
        AccessibleRole = AccessibleRole.StaticText,
    };

    private FluentToggle CreateToggle(string name, string accessibleName) => new()
    {
        Name = name,
        AccessibleName = accessibleName,
        Palette = palette,
    };

    private FluentButton CreateButton(
        string name,
        string text,
        FluentButtonKind kind,
        int width) =>
        new()
        {
            Name = name,
            Text = text,
            Kind = kind,
            Width = width,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Palette = palette,
            AccessibleName = text,
        };

    private TextBox CreateTextBox(string name, string placeholder, string accessibleName) => new()
    {
        Name = name,
        BorderStyle = BorderStyle.None,
        PlaceholderText = placeholder,
        AccessibleName = accessibleName,
        Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
        Margin = Padding.Empty,
    };

    private TextBox CreateApplicationListTextBox(
        string name,
        string accessibleName)
    {
        var textBox = CreateTextBox(
            name,
            "Mỗi dòng một tên file .exe",
            accessibleName);
        textBox.Multiline = true;
        textBox.AcceptsReturn = true;
        textBox.AcceptsTab = false;
        textBox.ScrollBars = ScrollBars.Vertical;
        textBox.WordWrap = false;
        textBox.MaxLength = 16_384;
        textBox.AccessibleDescription =
            "Nhập tên file thực thi, không nhập đường dẫn hoặc wildcard.";
        return textBox;
    }

    private ComboBox CreateFeedbackModeSelector()
    {
        var selector = new ComboBox
        {
            Name = "feedbackMode",
            AccessibleName = "Cách phản hồi khi dùng phím tắt",
            AccessibleDescription =
                "Chọn tự động, chỉ hình ảnh, chỉ âm thanh hoặc tắt phản hồi.",
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Regular),
            Width = 220,
            Height = 36,
            IntegralHeight = false,
            DropDownHeight = 148,
            Margin = Padding.Empty,
        };
        selector.Items.AddRange(
        [
            "Tự động — khuyến nghị",
            "Chỉ hình ảnh",
            "Chỉ âm thanh",
            "Tắt",
        ]);
        return selector;
    }

    private static int FeedbackModeToIndex(FeedbackMode mode) => mode switch
    {
        FeedbackMode.Automatic => 0,
        FeedbackMode.VisualOnly => 1,
        FeedbackMode.AudioOnly => 2,
        FeedbackMode.Off => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static FeedbackMode FeedbackModeFromIndex(int index) => index switch
    {
        0 => FeedbackMode.Automatic,
        1 => FeedbackMode.VisualOnly,
        2 => FeedbackMode.AudioOnly,
        3 => FeedbackMode.Off,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private Panel CreateInputFrame(TextBox textBox, Control? trailing = null)
    {
        var frame = new Panel
        {
            Name = textBox.Name + "Frame",
            Height = 40,
            Padding = trailing is null
                ? new Padding(12, 10, 12, 7)
                : new Padding(12, 8, 4, 4),
            Margin = Padding.Empty,
            Tag = "inputFrame",
        };
        textBox.Dock = DockStyle.Fill;
        if (trailing is null)
        {
            frame.Controls.Add(textBox);
        }
        else
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            textBox.Margin = new Padding(0, 4, 4, 0);
            trailing.Height = 30;
            trailing.Margin = Padding.Empty;
            layout.Controls.Add(textBox, 0, 0);
            layout.Controls.Add(trailing, 1, 0);
            frame.Controls.Add(layout);
        }
        frame.Paint += InputFramePaint;
        return frame;
    }

    private void SetupTsfButtonClick(object? sender, EventArgs eventArgs)
    {
        if (currentSnapshot.Readiness == KeyinaReadiness.Ready)
        {
            ShowSection("typing");
            return;
        }
        ShowSection("diagnostics");
    }

    private void StartTypingDiagnosticCapture()
    {
        if (!typingDiagnosticInput.IsHandleCreated)
        {
            _ = typingDiagnosticInput.Handle;
        }
        TypingDiagnosticTrace.Activate(typingDiagnosticInput.Handle);
        typingDiagnosticStatus.Text = "Đang ghi — chỉ ô sandbox này.";
        typingDiagnosticStatus.ForeColor = palette.Success;
        typingDiagnosticTimer.Start();
        RecordTypingDiagnosticControlEvent("Session.Start");
    }

    private void PauseTypingDiagnosticCapture()
    {
        if (typingDiagnosticInput.IsHandleCreated)
        {
            RecordTypingDiagnosticControlEvent("Session.Pause");
            TypingDiagnosticTrace.Deactivate(typingDiagnosticInput.Handle);
        }
        typingDiagnosticTimer.Stop();
        typingDiagnosticStatus.Text = "Tạm dừng — log vẫn được giữ để xem hoặc xuất.";
        typingDiagnosticStatus.ForeColor = palette.TextSecondary;
        RefreshTypingDiagnosticLog();
    }

    private void RecordTypingDiagnosticControlEvent(string eventName)
    {
        if (!typingDiagnosticInput.IsHandleCreated)
        {
            return;
        }
        TypingDiagnosticTrace.RecordOutput(
            typingDiagnosticInput.Handle,
            eventName,
            typingDiagnosticInput.Text,
            typingDiagnosticInput.SelectionStart,
            typingDiagnosticInput.SelectionLength);
        RefreshTypingDiagnosticLog();
    }

    private void RefreshTypingDiagnosticLog()
    {
        if (typingDiagnosticLog.IsDisposed)
        {
            return;
        }
        typingDiagnosticLog.Text = TypingDiagnosticTrace.FormatSnapshot(
            GetTypingDiagnosticFilter());
        if (typingDiagnosticLog.TextLength > 0)
        {
            typingDiagnosticLog.SelectionStart = typingDiagnosticLog.TextLength;
            typingDiagnosticLog.SelectionLength = 0;
            typingDiagnosticLog.ScrollToCaret();
        }
    }

    private TypingDiagnosticTraceKind? GetTypingDiagnosticFilter() =>
        typingDiagnosticFilter.SelectedIndex switch
        {
            0 => null,
            1 => TypingDiagnosticTraceKind.Physical,
            2 => TypingDiagnosticTraceKind.Engine,
            3 => TypingDiagnosticTraceKind.Output,
            4 => TypingDiagnosticTraceKind.Anomaly,
            _ => null,
        };

    private void ClearTypingDiagnosticLog()
    {
        TypingDiagnosticTrace.Clear();
        typingDiagnosticLog.Clear();
        typingDiagnosticStatus.Text = TypingDiagnosticTrace.IsEnabled
            ? "Đang ghi — log vừa được xóa."
            : "Tạm dừng — log đã được xóa.";
        typingDiagnosticStatus.ForeColor = TypingDiagnosticTrace.IsEnabled
            ? palette.Success
            : palette.TextSecondary;
    }

    private void CopyTypingDiagnosticLog()
    {
        var text = TypingDiagnosticTrace.FormatSnapshot(GetTypingDiagnosticFilter());
        if (text.Length == 0)
        {
            typingDiagnosticStatus.Text = "Chưa có sự kiện để sao chép.";
            typingDiagnosticStatus.ForeColor = palette.Warning;
            return;
        }

        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            typingDiagnosticStatus.Text = "Đã sao chép log đang hiển thị.";
            typingDiagnosticStatus.ForeColor = palette.Success;
        }
        catch (ExternalException)
        {
            typingDiagnosticStatus.Text = "Clipboard đang bận; hãy thử lại.";
            typingDiagnosticStatus.ForeColor = palette.Error;
        }
    }

    private void ExportTypingDiagnosticLog()
    {
        var text = TypingDiagnosticTrace.FormatSnapshot(GetTypingDiagnosticFilter());
        if (text.Length == 0)
        {
            typingDiagnosticStatus.Text = "Chưa có sự kiện để xuất.";
            typingDiagnosticStatus.ForeColor = palette.Warning;
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Xuất log chẩn đoán bộ gõ",
            Filter = "Keyina typing log (*.log)|*.log|Text file (*.txt)|*.txt",
            DefaultExt = "log",
            AddExtension = true,
            FileName = $"keyina-typing-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(
                dialog.FileName,
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            typingDiagnosticStatus.Text = "Đã xuất log vào file đã chọn.";
            typingDiagnosticStatus.ForeColor = palette.Success;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            typingDiagnosticStatus.Text = "Không thể xuất log; hãy chọn vị trí khác.";
            typingDiagnosticStatus.ForeColor = palette.Error;
        }
    }

    private static string FormatDiagnosticCharacter(char character) => character switch
    {
        '\r' => "\\r",
        '\n' => "\\n",
        '\t' => "\\t",
        _ => character.ToString(),
    };

    private async Task RunDiagnosticsAsync(FluentButton runButton)
    {
        runButton.Enabled = false;
        diagnosticsResult.Text = "Đang kiểm tra host, bộ gõ, phím tắt và tài nguyên…";
        diagnosticsResult.ForeColor = palette.TextSecondary;
        try
        {
            diagnosticsResult.Text = await actions.RunDiagnostics(lifetime.Token).ConfigureAwait(true);
            diagnosticsResult.ForeColor = palette.Success;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception)
        {
            diagnosticsResult.Text = $"Kiểm tra thất bại: {exception.Message}";
            diagnosticsResult.ForeColor = palette.Error;
        }
        finally
        {
            if (!IsDisposed)
            {
                runButton.Enabled = true;
            }
        }
    }

    private TextBox[] GetApplicationRuleTextBoxes() =>
    [
        disableVietnameseApplications,
        disableSpeechApplications,
        disableTranslationApplications,
        suppressVisualFeedbackApplications,
    ];

    private void UpdateApplicationRulesDisplay(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = preferences.Normalize();
        disableVietnameseApplications.Lines = normalized.DisableVietnamese;
        disableSpeechApplications.Lines = normalized.DisableSpeech;
        disableTranslationApplications.Lines = normalized.DisableTranslation;
        suppressVisualFeedbackApplications.Lines = normalized.SuppressVisualFeedback;
        applicationRulesStatus.Text = "Mỗi dòng là một tên file .exe, ví dụ game.exe.";
        applicationRulesStatus.ForeColor = palette.TextTertiary;
    }

    private void SaveApplicationPreferences()
    {
        try
        {
            var preferences = new ApplicationPreferences(
                ParseApplicationRules(disableVietnameseApplications),
                ParseApplicationRules(disableSpeechApplications),
                ParseApplicationRules(disableTranslationApplications),
                ParseApplicationRules(suppressVisualFeedbackApplications))
                .Normalize();
            applicationRulesDirty = false;
            UpdateApplicationRulesDisplay(preferences);
            actions.SetApplicationPreferences(preferences);
            applicationRulesStatus.Text = "Đã kiểm tra và lưu quy tắc ứng dụng.";
            applicationRulesStatus.ForeColor = palette.Success;
        }
        catch (ArgumentException exception)
        {
            applicationRulesDirty = true;
            applicationRulesStatus.Text = LocalizeApplicationRuleError(exception.Message);
            applicationRulesStatus.ForeColor = palette.Error;
        }
    }

    private void AddForegroundApplication(TextBox target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var executableName = actions.GetForegroundApplicationName();
        if (string.IsNullOrWhiteSpace(executableName))
        {
            applicationRulesStatus.Text =
                "Không xác định được ứng dụng trước khi mở Cài đặt. Hãy nhập tên file .exe thủ công.";
            applicationRulesStatus.ForeColor = palette.Warning;
            return;
        }

        try
        {
            var normalized = ApplicationPreferences.NormalizeExecutableName(executableName);
            var existing = ParseApplicationRules(target);
            if (existing.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                applicationRulesStatus.Text = $"{normalized} đã có trong danh sách.";
                applicationRulesStatus.ForeColor = palette.Warning;
                return;
            }
            target.Lines = existing.Append(normalized).ToArray();
            applicationRulesDirty = true;
            applicationRulesStatus.Text = $"Đã thêm {normalized}; nhấn Lưu quy tắc để áp dụng.";
            applicationRulesStatus.ForeColor = palette.Warning;
        }
        catch (ArgumentException)
        {
            applicationRulesStatus.Text =
                "Ứng dụng hiện tại không cung cấp tên file .exe hợp lệ.";
            applicationRulesStatus.ForeColor = palette.Error;
        }
    }

    private static string[] ParseApplicationRules(TextBox textBox) =>
        textBox.Lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private static string LocalizeApplicationRuleError(string message)
    {
        if (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return "Danh sách có tên ứng dụng bị trùng.";
        }
        if (message.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("wildcard", StringComparison.OrdinalIgnoreCase))
        {
            return "Chỉ nhập tên file .exe, không nhập đường dẫn hoặc wildcard.";
        }
        if (message.Contains(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Mỗi dòng phải là một tên file Windows kết thúc bằng .exe.";
        }
        return "Quy tắc ứng dụng không hợp lệ. Hãy kiểm tra từng dòng.";
    }

    private void EditHotkey(HotkeyCommand command)
    {
        using var dialog = new HotkeyCaptureDialog(command, currentSnapshot.Hotkeys);
        if (dialog.ShowDialog(this) == DialogResult.OK &&
            dialog.CapturedChord is { } chord)
        {
            actions.SetHotkey(command, chord);
        }
    }

    private void UpdateHotkeyDisplay(HotkeyPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        foreach (var (command, label) in hotkeyKeycaps)
        {
            label.Text = HotkeyText.Format(
                preferences.GetPreference(command).Chord);
            label.Invalidate();
        }

        var toggleVietnamese = HotkeyText.Format(
            preferences.ToggleVietnamese.Chord);
        var toggleDictation = HotkeyText.Format(
            preferences.ToggleDictation.Chord);
        var translation = HotkeyText.Format(
            preferences.TranslateSelection.Chord);
        SetNamedLabelText(
            "overviewTypingDetail",
            $"Telex · phím tắt {toggleVietnamese}");
        SetNamedLabelText("typingEnabledRowMetadata", toggleVietnamese);
        SetNamedLabelText("speechEnabledRowMetadata", toggleDictation);
        SetNamedLabelText("translationEnabledRowMetadata", translation);
        SetNamedLabelText(
            "translationShortcutTitle",
            $"Phím tắt {translation}");
    }

    private void SetNamedLabelText(string name, string text)
    {
        var label = Controls.Find(name, searchAllChildren: true)
            .OfType<Label>()
            .SingleOrDefault();
        if (label is not null)
        {
            label.Text = text;
        }
    }

    private void SaveSpeechCredential()
    {
        var secret = speechApiKey.Text.Trim();
        if (secret.Length == 0)
        {
            return;
        }
        actions.SaveSpeechApiKey(secret);
        speechApiKey.Clear();
    }

    private void SaveDeepLCredential()
    {
        var secret = deepLApiKey.Text.Trim();
        if (secret.Length == 0)
        {
            return;
        }
        actions.SaveDeepLApiKey(secret);
        deepLApiKey.Clear();
    }

    private void OpenDeepLAuthenticationHelp()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DeepLAuthenticationHelpUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            SetBadge(
                translationCredentialStatus,
                "Không mở được trợ giúp",
                FluentTone.Error);
        }
    }

    private void ExportSettings()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Xuất cài đặt Keyina",
            Filter = "Keyina settings (*.json)|*.json",
            DefaultExt = "json",
            AddExtension = true,
            FileName = $"keyina-settings-{DateTime.Now:yyyy-MM-dd}.json",
            OverwritePrompt = true,
            RestoreDirectory = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            actions.ExportSettings(dialog.FileName);
        }
    }

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Nhập cài đặt Keyina",
            Filter = "Keyina settings (*.json)|*.json",
            DefaultExt = "json",
            AddExtension = true,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            actions.ImportSettings(dialog.FileName);
        }
    }

    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(diagnosticsResult.Text);
            diagnosticsResult.Text = "Đã sao chép báo cáo chẩn đoán.";
            diagnosticsResult.ForeColor = palette.Success;
        }
        catch (ExternalException)
        {
            diagnosticsResult.Text = "Clipboard đang bận. Hãy thử sao chép lại.";
            diagnosticsResult.ForeColor = palette.Warning;
        }
    }

    private void UpdateResponsiveShell()
    {
        var sidebarWidth = ClientSize.Width < 1020 ? 206F : 228F;
        shell.ColumnStyles[0].Width = sidebarWidth;
        contentPanel.Padding = ClientSize.Width < 1020
            ? new Padding(22, 20, 22, 22)
            : new Padding(30, 22, 30, 26);
    }

    private void SystemEventsUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }
        BeginInvoke(new Action(ApplySystemTheme));
    }

    private void ApplySystemTheme()
    {
        palette = FluentTheme.Current;
        BackColor = palette.Window;
        ForeColor = palette.TextPrimary;
        shell.BackColor = palette.Window;
        sidebar.BackColor = palette.Sidebar;
        contentPanel.BackColor = palette.Window;
        pageHost.BackColor = palette.Window;
        systemThemeStatus.Text = FluentTheme.SystemThemeDescription;

        ApplyThemeRecursive(this);
        FluentWindow.Apply(this, palette);
        Invalidate(true);
    }

    private void ApplyThemeRecursive(Control root)
    {
        switch (root)
        {
            case FluentCard card:
                card.Palette = palette;
                break;
            case FluentToggle toggle:
                toggle.Palette = palette;
                toggle.BackColor = palette.Surface;
                break;
            case FluentNavigationButton navigation:
                navigation.Palette = palette;
                navigation.BackColor = palette.Sidebar;
                break;
            case FluentButton button:
                button.Palette = palette;
                button.BackColor = palette.Surface;
                break;
            case FluentStatusBadge badge:
                badge.Palette = palette;
                badge.BackColor = palette.Surface;
                break;
            case TextBox textBox:
                textBox.BackColor = palette.SurfaceSecondary;
                textBox.ForeColor = palette.TextPrimary;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = palette.SurfaceSecondary;
                comboBox.ForeColor = palette.TextPrimary;
                break;
            case ListView listView:
                listView.BackColor = palette.SurfaceSecondary;
                listView.ForeColor = palette.TextPrimary;
                break;
            case Label label:
                label.ForeColor = label.Tag switch
                {
                    LabelRole.Title or LabelRole.Heading or LabelRole.Primary => palette.TextPrimary,
                    LabelRole.Tertiary => palette.TextTertiary,
                    LabelRole.Icon => palette.Accent,
                    _ => palette.TextSecondary,
                };
                break;
            case Panel panel when string.Equals(panel.Tag as string, "inputFrame", StringComparison.Ordinal):
                panel.BackColor = palette.SurfaceSecondary;
                break;
            case TableLayoutPanel table:
                table.BackColor = Color.Transparent;
                break;
            case FlowLayoutPanel flow:
                flow.BackColor = Color.Transparent;
                break;
        }

        foreach (Control child in root.Controls)
        {
            ApplyThemeRecursive(child);
        }
    }

    private static void SetBadge(FluentStatusBadge badge, string text, FluentTone tone)
    {
        badge.Text = text;
        badge.Tone = tone;
        badge.AccessibleName = text;
        badge.Invalidate();
    }

    private void BrandMarkPaint(object? sender, PaintEventArgs eventArgs)
    {
        if (sender is not Label label)
        {
            return;
        }
        eventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, label.Width - 10, label.Height - 10);
        using var path = FluentDrawing.CreateRoundedRectangle(bounds, 10);
        using var brush = new SolidBrush(palette.Accent);
        eventArgs.Graphics.FillPath(brush, path);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            "K",
            label.Font,
            bounds,
            Color.White,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);
    }

    private void InputFramePaint(object? sender, PaintEventArgs eventArgs)
    {
        if (sender is not Panel panel)
        {
            return;
        }
        var bounds = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
        using var path = FluentDrawing.CreateRoundedRectangle(
            bounds,
            FluentMetrics.ControlCornerRadius);
        using var pen = new Pen(
            panel.ContainsFocus ? palette.Focus : palette.BorderStrong,
            panel.ContainsFocus ? 1.5F : 1F);
        eventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        eventArgs.Graphics.DrawPath(pen, path);
    }

    private void KeycapPaint(object? sender, PaintEventArgs eventArgs)
    {
        if (sender is not Label label)
        {
            return;
        }
        var bounds = new Rectangle(0, 0, label.Width - 1, label.Height - 1);
        using var path = FluentDrawing.CreateRoundedRectangle(
            bounds,
            FluentMetrics.ControlCornerRadius);
        using var brush = new SolidBrush(palette.SurfaceSecondary);
        using var pen = new Pen(palette.BorderStrong);
        eventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        eventArgs.Graphics.FillPath(brush, path);
        eventArgs.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            label.Text,
            label.Font,
            bounds,
            palette.TextPrimary,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix);
    }

    private static string LocalizeRuntimeStatus(string status)
    {
        if (status.Contains("Focused app connected", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Native resident connected", StringComparison.OrdinalIgnoreCase))
        {
            return "Đã kết nối";
        }
        if (status.Contains("Registered", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Resident active", StringComparison.OrdinalIgnoreCase))
        {
            return "Đang hoạt động";
        }
        if (status.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return "Không khả dụng";
        }
        if (status.Contains("Not", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Missing", StringComparison.OrdinalIgnoreCase))
        {
            return "Cần xử lý";
        }
        return status;
    }

#pragma warning restore CA1725

    private enum LabelRole
    {
        Title,
        Heading,
        Primary,
        Secondary,
        Tertiary,
        Caption,
        Icon,
    }

    private sealed record SnippetRow(
        string Trigger,
        string Expansion,
        string Scope,
        SnippetConfiguration? Configuration = null)
    {
        public static IReadOnlyList<SnippetRow> All { get; } =
        [
            new(";kvi", "Bật hoặc tắt bộ gõ tiếng Việt", "Lệnh · nuốt Space"),
            new(";kvoice", "Bắt đầu hoặc dừng nhập bằng giọng nói", "Lệnh · nuốt Space"),
            new(";kdate", "${date} → ngày hiện tại", "Biến · giữ Space"),
            new(";ktime", "${time} → giờ hiện tại", "Biến · giữ Space"),
            new(";kdatetime", "${datetime} → ngày và giờ", "Biến · giữ Space"),
        ];
    }
}
