using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Keyina.Host.UI;

public sealed class SettingsForm : Form
{
    private static readonly Color WindowBackground = Color.FromArgb(14, 18, 28);
    private static readonly Color SidebarBackground = Color.FromArgb(19, 24, 36);
    private static readonly Color CardBackground = Color.FromArgb(27, 33, 48);
    private static readonly Color CardBorder = Color.FromArgb(50, 59, 78);
    private static readonly Color PrimaryText = Color.FromArgb(241, 245, 249);
    private static readonly Color SecondaryText = Color.FromArgb(157, 169, 190);
    private static readonly Color Accent = Color.FromArgb(100, 116, 255);
    private static readonly Color AccentHover = Color.FromArgb(116, 132, 255);
    private static readonly Color Positive = Color.FromArgb(52, 211, 153);
    private static readonly Color Warning = Color.FromArgb(251, 191, 36);

    private readonly SettingsActions actions;
    private readonly Dictionary<string, Panel> pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> navigationButtons = new(StringComparer.Ordinal);
    private readonly Panel pageHost;
    private readonly Label sectionTitle;
    private readonly CheckBox vietnameseToggle;
    private readonly CheckBox speechToggle;
    private readonly CheckBox startupToggle;
    private readonly Label statusMessage;
    private readonly Label inputStatus;
    private readonly Label speechStatus;
    private readonly Label speechCredentialStatus;
    private readonly Label ipcStatus;
    private readonly Label hotkeyStatus;
    private readonly Label snippetCount;
    private readonly TextBox speechApiKey;
    private readonly Button saveSpeechKey;
    private readonly Button removeSpeechKey;
    private readonly Label diagnosticsResult;
    private readonly CancellationTokenSource lifetime = new();
    private bool applyingSnapshot;

    public SettingsForm(SettingsSnapshot snapshot, SettingsActions actions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));

        Text = "Keyina";
        AccessibleName = "Keyina settings";
        AccessibleDescription = "Settings for Vietnamese typing, dictation, hotkeys, snippets, and diagnostics.";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(980, 690);
        BackColor = WindowBackground;
        ForeColor = PrimaryText;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        KeyPreview = true;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = WindowBackground,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        var sidebar = CreateSidebar();
        root.Controls.Add(sidebar, 0, 0);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = WindowBackground,
            Padding = new Padding(34, 26, 34, 30),
        };
        root.Controls.Add(content, 1, 0);

        sectionTitle = new Label
        {
            AutoSize = true,
            Text = "Overview",
            Font = new Font(Font.FontFamily, 21F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Location = new Point(0, 0),
        };
        content.Controls.Add(sectionTitle);

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "Fast, private Vietnamese input for every Windows app.",
            Font = new Font(Font.FontFamily, 9.5F),
            ForeColor = SecondaryText,
            Location = new Point(2, 43),
        };
        content.Controls.Add(subtitle);

        pageHost = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Location = new Point(0, 78),
            Size = new Size(content.ClientSize.Width, content.ClientSize.Height - 78),
            BackColor = WindowBackground,
        };
        content.Controls.Add(pageHost);
        content.Resize += (_, _) =>
        {
            pageHost.Size = new Size(content.ClientSize.Width, Math.Max(0, content.ClientSize.Height - 78));
        };

        statusMessage = CreateValueLabel("statusMessage");
        inputStatus = CreateValueLabel("inputStatus");
        speechStatus = CreateValueLabel("speechStatus");
        speechCredentialStatus = CreateValueLabel("speechCredentialStatus");
        ipcStatus = CreateValueLabel("ipcStatus");
        hotkeyStatus = CreateValueLabel("hotkeyStatus");
        snippetCount = CreateValueLabel("snippetCount");

        vietnameseToggle = CreateToggle("vietnameseToggle", "Vietnamese input", "Ctrl + Shift");
        speechToggle = CreateToggle("speechToggle", "Speech-to-text", "Ctrl + Alt + V");
        startupToggle = CreateToggle("startupToggle", "Start with Windows", "Current user only");

        speechApiKey = new TextBox
        {
            Name = "speechApiKey",
            Dock = DockStyle.Top,
            Height = 36,
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(17, 22, 34),
            ForeColor = PrimaryText,
            AccessibleName = "Speechmatics API key",
            PlaceholderText = "Paste a Speechmatics API key",
            Margin = new Padding(0, 8, 0, 8),
        };
        saveSpeechKey = CreatePrimaryButton("saveSpeechKey", "Save key");
        saveSpeechKey.Enabled = false;
        speechApiKey.TextChanged += (_, _) => saveSpeechKey.Enabled =
            !string.IsNullOrWhiteSpace(speechApiKey.Text);
        saveSpeechKey.Click += (_, _) =>
        {
            var secret = speechApiKey.Text;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return;
            }

            actions.SaveSpeechApiKey(secret);
            speechApiKey.Clear();
        };

        removeSpeechKey = CreateSecondaryButton("removeSpeechKey", "Remove key");
        removeSpeechKey.Click += (_, _) => actions.DeleteSpeechApiKey();

        diagnosticsResult = new Label
        {
            Name = "diagnosticsResult",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 70,
            Text = "Run offline checks to verify hotkeys, IPC, configuration, and resource usage.",
            ForeColor = SecondaryText,
            Padding = new Padding(0, 8, 0, 0),
        };

        pages.Add("overview", CreateOverviewPage());
        pages.Add("typing", CreateTypingPage());
        pages.Add("speech", CreateSpeechPage());
        pages.Add("hotkeys", CreateHotkeysPage());
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
        startupToggle.CheckedChanged += (_, _) =>
        {
            if (!applyingSnapshot)
            {
                actions.SetStartupEnabled(startupToggle.Checked);
            }
        };

        ApplySnapshot(snapshot);
        ShowSection("overview", "Overview");
    }

    public void ApplySnapshot(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        applyingSnapshot = true;
        try
        {
            vietnameseToggle.Checked = snapshot.VietnameseEnabled;
            speechToggle.Checked = snapshot.SpeechEnabled;
            startupToggle.Checked = snapshot.StartupEnabled;
            statusMessage.Text = snapshot.StatusMessage;
            statusMessage.ForeColor = snapshot.Listening ? Warning : Positive;
            inputStatus.Text = snapshot.VietnameseEnabled ? "Enabled" : "Disabled";
            inputStatus.ForeColor = snapshot.VietnameseEnabled ? Positive : SecondaryText;
            speechStatus.Text = snapshot.Listening
                ? "Listening"
                : snapshot.SpeechEnabled
                    ? "Ready"
                    : "Disabled";
            speechStatus.ForeColor = snapshot.Listening
                ? Warning
                : snapshot.SpeechEnabled
                    ? Positive
                    : SecondaryText;
            speechCredentialStatus.Text = snapshot.SpeechCredentialConfigured
                ? "Configured"
                : "Not configured";
            speechCredentialStatus.ForeColor = snapshot.SpeechCredentialConfigured
                ? Positive
                : Warning;
            removeSpeechKey.Enabled = snapshot.SpeechCredentialConfigured;
            ipcStatus.Text = snapshot.IpcStatus;
            hotkeyStatus.Text = snapshot.HotkeyStatus;
            snippetCount.Text = snapshot.CustomSnippetCount == 1
                ? "1 custom snippet"
                : $"{snapshot.CustomSnippetCount} custom snippets";
        }
        finally
        {
            applyingSnapshot = false;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        base.OnFormClosed(e);
    }

    private Panel CreateSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SidebarBackground,
            Padding = new Padding(18, 24, 18, 18),
        };

        var mark = new Label
        {
            AutoSize = false,
            Text = "K",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Accent,
            Location = new Point(18, 22),
            Size = new Size(44, 44),
            AccessibleName = "Keyina logo",
        };
        sidebar.Controls.Add(mark);

        var product = new Label
        {
            AutoSize = true,
            Text = "Keyina",
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            ForeColor = PrimaryText,
            Location = new Point(72, 23),
        };
        sidebar.Controls.Add(product);

        var productSubtitle = new Label
        {
            AutoSize = true,
            Text = "Vietnamese input",
            Font = new Font(Font.FontFamily, 8.5F),
            ForeColor = SecondaryText,
            Location = new Point(74, 49),
        };
        sidebar.Controls.Add(productSubtitle);

        var navigation = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            Location = new Point(12, 98),
            Size = new Size(196, 360),
            BackColor = Color.Transparent,
        };
        sidebar.Controls.Add(navigation);

        AddNavigation(navigation, "navOverview", "Overview", "overview");
        AddNavigation(navigation, "navTyping", "Typing", "typing");
        AddNavigation(navigation, "navSpeech", "Speech-to-text", "speech");
        AddNavigation(navigation, "navHotkeys", "Hotkeys", "hotkeys");
        AddNavigation(navigation, "navSnippets", "Snippets", "snippets");
        AddNavigation(navigation, "navDiagnostics", "Diagnostics", "diagnostics");

        var privacy = new Label
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AutoSize = false,
            Text = "LOCAL-FIRST\r\nTyping never calls the network.",
            Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
            ForeColor = SecondaryText,
            Location = new Point(20, 500),
            Size = new Size(178, 54),
        };
        sidebar.Controls.Add(privacy);
        sidebar.Resize += (_, _) => privacy.Top = Math.Max(470, sidebar.ClientSize.Height - 76);

        return sidebar;
    }

    private void AddNavigation(
        FlowLayoutPanel navigation,
        string name,
        string text,
        string pageKey)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 190,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = SecondaryText,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 5),
            Padding = new Padding(14, 0, 0, 0),
            TabStop = true,
            AccessibleName = text,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 41, 59);
        button.Click += (_, _) => ShowSection(pageKey, text);
        navigation.Controls.Add(button);
        navigationButtons.Add(pageKey, button);
    }

    private void ShowSection(string pageKey, string title)
    {
        foreach (var (key, page) in pages)
        {
            page.Visible = string.Equals(key, pageKey, StringComparison.Ordinal);
        }
        foreach (var (key, button) in navigationButtons)
        {
            var selected = string.Equals(key, pageKey, StringComparison.Ordinal);
            button.BackColor = selected ? Color.FromArgb(46, 54, 78) : Color.Transparent;
            button.ForeColor = selected ? Color.White : SecondaryText;
            button.Font = new Font(
                button.Font,
                selected ? FontStyle.Bold : FontStyle.Regular);
        }
        sectionTitle.Text = title;
    }

    private Panel CreateOverviewPage()
    {
        var page = CreatePage();
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 146F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
        page.Controls.Add(grid);

        grid.Controls.Add(CreateStatusCard(
            "Input",
            inputStatus,
            "Native TSF composition",
            "Ctrl + Shift"), 0, 0);
        grid.Controls.Add(CreateStatusCard(
            "Dictation",
            speechStatus,
            "Speechmatics · Vietnamese",
            "Ctrl + Alt + Space"), 1, 0);
        grid.Controls.Add(CreateStatusCard(
            "System",
            statusMessage,
            "Private resident host",
            "Low-latency"), 2, 0);

        var privacyCard = CreateCard();
        privacyCard.Margin = new Padding(6, 12, 6, 0);
        grid.SetColumnSpan(privacyCard, 2);
        grid.Controls.Add(privacyCard, 0, 1);
        privacyCard.Controls.Add(CreateCardTitle("Private by design", 18, 18));
        privacyCard.Controls.Add(new Label
        {
            AutoSize = false,
            Text = "The native typing path is offline. Speech is optional, credentials stay in Windows Credential Manager, and final text is inserted through the focused TSF context instead of clipboard paste.",
            ForeColor = SecondaryText,
            Font = new Font(Font.FontFamily, 9.5F),
            Location = new Point(20, 52),
            Size = new Size(480, 68),
        });

        var connectionCard = CreateCard();
        connectionCard.Margin = new Padding(6, 12, 6, 0);
        grid.Controls.Add(connectionCard, 2, 1);
        connectionCard.Controls.Add(CreateCardTitle("Focused app", 18, 18));
        ipcStatus.Location = new Point(20, 57);
        ipcStatus.Size = new Size(210, 42);
        connectionCard.Controls.Add(ipcStatus);

        return page;
    }

    private Panel CreateTypingPage()
    {
        var page = CreatePage();
        var stack = CreateVerticalStack();
        page.Controls.Add(stack);
        stack.Controls.Add(CreateSectionDescription(
            "Familiar controls, native TSF edits, and Context Guard for code, URLs, commands, paths, and English-heavy tokens."));
        stack.Controls.Add(CreateToggleCard(
            vietnameseToggle,
            "Enable or disable Vietnamese input without changing Windows focus."));
        stack.Controls.Add(CreateToggleCard(
            startupToggle,
            "Launch the lightweight host when this Windows user signs in."));
        stack.Controls.Add(CreateInformationCard(
            "Compatibility strategy",
            "Keyina uses Text Services Framework compositions and validates suffix ownership before replacing text. Unsupported or secure fields fail open to literal input."));
        return page;
    }

    private Panel CreateSpeechPage()
    {
        var page = CreatePage();
        var stack = CreateVerticalStack();
        page.Controls.Add(stack);
        stack.Controls.Add(CreateSectionDescription(
            "Optional Vietnamese realtime dictation. Partials stay in the overlay; only stable final segments are inserted into the focused app."));
        stack.Controls.Add(CreateToggleCard(
            speechToggle,
            "Speech failures never disable or delay ordinary Vietnamese typing."));

        var credentialCard = CreateCard();
        credentialCard.Height = 222;
        credentialCard.Padding = new Padding(20);
        credentialCard.Controls.Add(CreateCardTitle("Speechmatics credential", 20, 18));
        speechCredentialStatus.Location = new Point(20, 52);
        speechCredentialStatus.Size = new Size(240, 28);
        credentialCard.Controls.Add(speechCredentialStatus);
        speechApiKey.Location = new Point(20, 88);
        speechApiKey.Width = 520;
        credentialCard.Controls.Add(speechApiKey);
        saveSpeechKey.Location = new Point(20, 140);
        removeSpeechKey.Location = new Point(132, 140);
        credentialCard.Controls.Add(saveSpeechKey);
        credentialCard.Controls.Add(removeSpeechKey);
        credentialCard.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Stored only in Windows Credential Manager. Never written to settings.json.",
            ForeColor = SecondaryText,
            Location = new Point(20, 188),
        });
        stack.Controls.Add(credentialCard);
        return page;
    }

    private Panel CreateHotkeysPage()
    {
        var page = CreatePage();
        var stack = CreateVerticalStack();
        page.Controls.Add(stack);
        stack.Controls.Add(CreateSectionDescription(
            "Defaults follow familiar UniKey/EVKey muscle memory and avoid stealing focus."));
        stack.Controls.Add(CreateShortcutCard("Ctrl + Shift", "Toggle Vietnamese input"));
        stack.Controls.Add(CreateShortcutCard("Ctrl + Alt + Space", "Hold to dictate"));
        stack.Controls.Add(CreateShortcutCard("Ctrl + Alt + V", "Toggle dictation session"));
        stack.Controls.Add(CreateShortcutCard("Escape", "Cancel dictation"));
        var statusCard = CreateInformationCard("Registration status", string.Empty);
        hotkeyStatus.Location = new Point(20, 53);
        hotkeyStatus.Size = new Size(500, 30);
        statusCard.Controls.Add(hotkeyStatus);
        stack.Controls.Add(statusCard);
        return page;
    }

    private Panel CreateSnippetsPage()
    {
        var page = CreatePage();
        var stack = CreateVerticalStack();
        page.Controls.Add(stack);
        stack.Controls.Add(CreateSectionDescription(
            "Fast local expansions with explicit delimiters, application scopes, Unicode validation, and secure-field bypass."));

        var card = CreateCard();
        card.Height = 308;
        card.Padding = new Padding(18);
        card.Controls.Add(CreateCardTitle("Snippet library", 18, 16));
        snippetCount.Location = new Point(18, 48);
        snippetCount.Size = new Size(300, 26);
        card.Controls.Add(snippetCount);

        var table = new DataGridView
        {
            Name = "snippetTable",
            Location = new Point(18, 82),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Size = new Size(660, 200),
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.FromArgb(18, 23, 35),
            BorderStyle = BorderStyle.None,
            GridColor = CardBorder,
            ForeColor = PrimaryText,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(35, 42, 58),
                ForeColor = PrimaryText,
                SelectionBackColor = Color.FromArgb(35, 42, 58),
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(22, 27, 40),
                ForeColor = PrimaryText,
                SelectionBackColor = Color.FromArgb(52, 61, 91),
                SelectionForeColor = Color.White,
            },
            EnableHeadersVisualStyles = false,
        };
        table.Columns.Add("trigger", "Trigger");
        table.Columns.Add("action", "Expansion / command");
        table.Rows.Add(";kvi", "Toggle Vietnamese");
        table.Rows.Add(";kvoice", "Toggle dictation");
        table.Rows.Add(";kdate", "Current date");
        table.Rows.Add(";ktime", "Current time");
        card.Controls.Add(table);
        stack.Controls.Add(card);
        return page;
    }

    private Panel CreateDiagnosticsPage()
    {
        var page = CreatePage();
        var stack = CreateVerticalStack();
        page.Controls.Add(stack);
        stack.Controls.Add(CreateSectionDescription(
            "Offline checks never collect transcript text, audio, snippets, clipboard content, or raw keystrokes."));

        var card = CreateCard();
        card.Height = 208;
        card.Padding = new Padding(20);
        card.Controls.Add(CreateCardTitle("System health", 20, 18));
        diagnosticsResult.Location = new Point(20, 50);
        diagnosticsResult.Width = 620;
        card.Controls.Add(diagnosticsResult);

        var run = CreatePrimaryButton("runDiagnostics", "Run offline checks");
        run.Location = new Point(20, 132);
        run.Width = 154;
        run.Click += async (_, _) =>
        {
            run.Enabled = false;
            diagnosticsResult.Text = "Running checks…";
            try
            {
                diagnosticsResult.Text = await actions.RunDiagnostics(lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                diagnosticsResult.Text = $"Diagnostics failed: {exception.GetType().Name}";
            }
            finally
            {
                if (!IsDisposed)
                {
                    run.Enabled = true;
                }
            }
        };
        card.Controls.Add(run);

        var folder = CreateSecondaryButton("openConfigFolder", "Open config folder");
        folder.Location = new Point(186, 132);
        folder.Width = 154;
        folder.Click += (_, _) => actions.OpenConfigurationFolder();
        card.Controls.Add(folder);
        stack.Controls.Add(card);
        return page;
    }

    private static Panel CreatePage() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = WindowBackground,
        Visible = false,
        AutoScroll = true,
        Padding = new Padding(0, 0, 8, 8),
    };

    private static FlowLayoutPanel CreateVerticalStack() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        BackColor = Color.Transparent,
        Padding = new Padding(0),
    };

    private Label CreateSectionDescription(string text) => new()
    {
        AutoSize = false,
        Width = 690,
        Height = 58,
        Text = text,
        ForeColor = SecondaryText,
        Font = new Font(Font.FontFamily, 10F),
        Margin = new Padding(4, 0, 4, 14),
    };

    private static RoundedPanel CreateToggleCard(CheckBox toggle, string description)
    {
        var card = CreateCard();
        card.Height = 104;
        card.Margin = new Padding(4, 0, 4, 12);
        toggle.Location = new Point(20, 18);
        toggle.Width = 620;
        card.Controls.Add(toggle);
        card.Controls.Add(new Label
        {
            AutoSize = false,
            Text = description,
            ForeColor = SecondaryText,
            Location = new Point(46, 53),
            Size = new Size(600, 36),
        });
        return card;
    }

    private RoundedPanel CreateStatusCard(
        string title,
        Label value,
        string detail,
        string footer)
    {
        var card = CreateCard();
        card.Margin = new Padding(6);
        card.Controls.Add(CreateCardTitle(title, 18, 16));
        value.Location = new Point(18, 53);
        value.Size = new Size(200, 28);
        card.Controls.Add(value);
        card.Controls.Add(new Label
        {
            AutoSize = true,
            Text = detail,
            ForeColor = SecondaryText,
            Location = new Point(18, 86),
        });
        card.Controls.Add(new Label
        {
            AutoSize = true,
            Text = footer,
            ForeColor = Color.FromArgb(126, 139, 166),
            Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
            Location = new Point(18, 112),
        });
        return card;
    }

    private RoundedPanel CreateShortcutCard(string chord, string description)
    {
        var card = CreateCard();
        card.Height = 72;
        card.Margin = new Padding(4, 0, 4, 10);
        var chordLabel = new Label
        {
            AutoSize = false,
            Text = chord,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(48, 57, 82),
            Location = new Point(18, 17),
            Size = new Size(160, 38),
        };
        card.Controls.Add(chordLabel);
        card.Controls.Add(new Label
        {
            AutoSize = true,
            Text = description,
            ForeColor = PrimaryText,
            Location = new Point(198, 27),
        });
        return card;
    }

    private RoundedPanel CreateInformationCard(string title, string description)
    {
        var card = CreateCard();
        card.Height = 112;
        card.Margin = new Padding(4, 0, 4, 12);
        card.Controls.Add(CreateCardTitle(title, 20, 16));
        if (!string.IsNullOrEmpty(description))
        {
            card.Controls.Add(new Label
            {
                AutoSize = false,
                Text = description,
                ForeColor = SecondaryText,
                Location = new Point(20, 49),
                Size = new Size(620, 50),
            });
        }
        return card;
    }

    private static RoundedPanel CreateCard() => new()
    {
        Width = 700,
        Height = 120,
        BackColor = CardBackground,
        BorderColor = CardBorder,
        CornerRadius = 14,
        Margin = new Padding(4, 0, 4, 12),
    };

    private Label CreateCardTitle(string text, int x, int y) => new()
    {
        AutoSize = true,
        Text = text,
        Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
        ForeColor = PrimaryText,
        Location = new Point(x, y),
    };

    private CheckBox CreateToggle(string name, string text, string shortcut) => new()
    {
        Name = name,
        AutoSize = false,
        Height = 28,
        Text = $"{text}     {shortcut}",
        Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold),
        ForeColor = PrimaryText,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = text,
    };

    private Label CreateValueLabel(string name) => new()
    {
        Name = name,
        AutoSize = false,
        Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
        ForeColor = PrimaryText,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private Button CreatePrimaryButton(string name, string text)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = false,
            Size = new Size(100, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        return button;
    }

    private Button CreateSecondaryButton(string name, string text)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = false,
            Size = new Size(112, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 44, 62),
            ForeColor = PrimaryText,
            Cursor = Cursors.Hand,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
        };
        button.FlatAppearance.BorderColor = CardBorder;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 57, 79);
        return button;
    }

    private sealed class RoundedPanel : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; init; } = 12;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; init; } = CardBorder;

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rectangle = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundedRectangle(rectangle, CornerRadius);
            using var pen = new Pen(BorderColor);
            eventArgs.Graphics.DrawPath(pen, path);
        }

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            using var path = CreateRoundedRectangle(
                new Rectangle(0, 0, Width, Height),
                CornerRadius);
            Region = new Region(path);
            Invalidate();
        }

        private static GraphicsPath CreateRoundedRectangle(
            Rectangle rectangle,
            int radius)
        {
            var diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
