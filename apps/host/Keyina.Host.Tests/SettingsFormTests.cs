using System.Reflection;
using Keyina.Host.Core.Applications;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.UI;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class SettingsFormTests
{
    [KeyinaTest("settings form is accessible DPI-aware and exposes all production sections")]
    private static void SettingsFormStructureIsComplete()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);

        AssertEx.Equal("Keyina", form.Text);
        AssertEx.Equal("Cài đặt Keyina", form.AccessibleName);
        AssertEx.True(
            form.AccessibleDescription?.Contains("dịch", StringComparison.OrdinalIgnoreCase) == true,
            "Settings accessibility description did not include translation.");
        AssertEx.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        AssertEx.True(form.MinimumSize.Width >= 760, "Settings minimum width is too small.");
        AssertEx.True(form.MinimumSize.Height >= 560, "Settings minimum height is too small.");
        AssertEx.True(form.ShowInTaskbar, "Settings window should appear in the taskbar when opened.");
        AssertEx.Equal(FormStartPosition.CenterScreen, form.StartPosition);
        AssertEx.True(form.UsesBufferedRendering,
            "Settings shell should use double buffering for smooth Fluent rendering.");
        AssertEx.Equal(1, form.Controls.Find("settingsShell", true).Length);
        AssertEx.Equal(1, form.Controls.Find("systemThemeStatus", true).Length);

        foreach (var name in new[]
                 {
                     "navOverview",
                     "navTyping",
                     "navSpeech",
                     "navTranslation",
                     "navHotkeys",
                     "navApplications",
                     "navSnippets",
                     "navDiagnostics",
                 })
        {
            AssertEx.Equal(1, form.Controls.Find(name, searchAllChildren: true).Length);
        }
    }

    [KeyinaTest("settings form protects speech credentials and exposes familiar controls")]
    private static void SensitiveAndFamiliarControlsAreCorrect()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);

        var apiKey = (TextBox)form.Controls.Find("speechApiKey", true).Single();
        AssertEx.True(apiKey.UseSystemPasswordChar, "Speech API key was not masked.");
        AssertEx.Equal(string.Empty, apiKey.Text);

        var deepLApiKey = (TextBox)form.Controls.Find("deepLApiKey", true).Single();
        AssertEx.True(deepLApiKey.UseSystemPasswordChar, "DeepL API key was not masked.");
        AssertEx.Equal(string.Empty, deepLApiKey.Text);
        AssertEx.True(deepLApiKey.MaxLength is > 0 and <= 256,
            "DeepL API key input should have a bounded length.");
        AssertEx.Equal(1, form.Controls.Find("openDeepLApiHelp", true).Length);
        AssertEx.Equal(1, form.Controls.Find("translationToggle", true).Length);
        AssertEx.Equal(1, form.Controls.Find("translationTargetLanguage", true).Length);
        AssertEx.Equal(1, form.Controls.Find("translationHotkeyStatus", true).Length);
        var privacy = (Label)form.Controls.Find("translationPrivacyWarning", true).Single();
        AssertEx.True(
            privacy.Text.Contains("nhạy cảm", StringComparison.OrdinalIgnoreCase),
            "DeepL Free privacy warning must mention sensitive content.");

        var vietnamese = (CheckBox)form.Controls.Find("vietnameseToggle", true).Single();
        var startup = (CheckBox)form.Controls.Find("startupToggle", true).Single();
        AssertEx.True(vietnamese.Checked, "Vietnamese input should be enabled in the sample state.");
        AssertEx.True(startup.Checked, "Startup should be enabled in the sample state.");

        var saveButton = (Button)form.Controls.Find("saveSpeechKey", true).Single();
        AssertEx.True(!saveButton.Enabled, "Empty API key should not be saveable.");
        AssertEx.Equal(
            "Cập nhật khóa",
            ((Button)form.Controls.Find("saveDeepLKey", true).Single()).Text);

        AssertEx.Equal(0, FindDescendants<DataGridView>(form).Count,
            "Production snippets UI should not expose the legacy DataGridView chrome.");
        AssertEx.True(
            FindDescendants<CheckBox>(form).All(control =>
                control.GetType().Name.Contains("FluentToggle", StringComparison.Ordinal)),
            "Settings toggles should use the Fluent owner-drawn control.");
    }

    [KeyinaTest("translation credential input trims pasted whitespace before secure storage")]
    private static void TranslationCredentialInputNormalizesPastedSecret()
    {
        var savedSecrets = new List<string>();
        var actions = SettingsActions.NoOp with
        {
            SaveDeepLApiKey = savedSecrets.Add,
        };
        using var form = new SettingsForm(SettingsSnapshot.Sample, actions);
        var input = (TextBox)form.Controls.Find("deepLApiKey", true).Single();
        var save = (Button)form.Controls.Find("saveDeepLKey", true).Single();

        input.Text = "  test-key:fx\r\n";
        InvokeClick(save);

        AssertEx.Equal(1, savedSecrets.Count);
        AssertEx.Equal("test-key:fx", savedSecrets[0]);
        AssertEx.Equal(string.Empty, input.Text);
        AssertEx.True(!save.Enabled, "Save should disable after the secret is cleared.");

        input.Text = "keyboard-key:fx";
        var keyEvent = InvokeKeyDown(input, Keys.Enter);
        AssertEx.Equal(2, savedSecrets.Count);
        AssertEx.Equal("keyboard-key:fx", savedSecrets[1]);
        AssertEx.True(keyEvent.Handled, "Enter should be handled by the credential input.");
        AssertEx.True(keyEvent.SuppressKeyPress,
            "Enter should not emit a system beep after saving the credential.");
    }

    [KeyinaTest("hotkeys settings expose editable rows and restore actions")]
    private static void HotkeyEditorControlsAreBound()
    {
        var resetCommands = new List<HotkeyCommand>();
        var resetAllCount = 0;
        var actions = SettingsActions.NoOp with
        {
            ResetHotkey = resetCommands.Add,
            ResetAllHotkeys = () => resetAllCount++,
        };
        using var form = new SettingsForm(SettingsSnapshot.Sample, actions);

        foreach (var name in new[]
                 {
                     "hotkeyVietnameseChange",
                     "hotkeyVietnameseReset",
                     "hotkeyPushToTalkChange",
                     "hotkeyPushToTalkReset",
                     "hotkeyToggleDictationChange",
                     "hotkeyToggleDictationReset",
                     "hotkeyTranslationChange",
                     "hotkeyTranslationReset",
                     "hotkeyCancelChange",
                     "hotkeyCancelReset",
                     "resetAllHotkeys",
                 })
        {
            AssertEx.Equal(1, form.Controls.Find(name, true).Length);
        }

        var custom = HotkeyPreferences.Default with
        {
            TranslateSelection = HotkeyPreferences.Default.TranslateSelection with
            {
                Chord = new HotkeyChord(
                    HotkeyModifiers.Control | HotkeyModifiers.Shift,
                    VirtualKey.K),
            },
        };
        form.ApplySnapshot(SettingsSnapshot.Sample with { Hotkeys = custom });
        AssertEx.Equal(
            "Ctrl + Shift + K",
            ((Label)form.Controls.Find("hotkeyTranslationKeycap", true).Single()).Text);
        AssertEx.Equal(
            "Phím tắt Ctrl + Shift + K",
            ((Label)form.Controls.Find("translationShortcutTitle", true).Single()).Text);

        InvokeClick((Button)form.Controls.Find("hotkeyTranslationReset", true).Single());
        InvokeClick((Button)form.Controls.Find("resetAllHotkeys", true).Single());
        AssertEx.True(
            resetCommands.SequenceEqual([HotkeyCommand.TranslateSelection]),
            "Per-command restore invoked the wrong action.");
        AssertEx.Equal(1, resetAllCount);
    }

    [KeyinaTest("hotkeys settings configure and preview non-intrusive feedback")]
    private static void HotkeysFeedbackControlsAreBound()
    {
        var modes = new List<FeedbackMode>();
        var previews = 0;
        var actions = SettingsActions.NoOp with
        {
            SetFeedbackMode = modes.Add,
            PreviewFeedback = () => previews++,
        };
        using var form = new SettingsForm(SettingsSnapshot.Sample, actions);
        var selector = (ComboBox)form.Controls.Find("feedbackMode", true).Single();
        var preview = (Button)form.Controls.Find("previewFeedback", true).Single();
        var note = (Label)form.Controls.Find("feedbackFullscreenNote", true).Single();

        AssertEx.Equal(0, selector.SelectedIndex);
        selector.SelectedIndex = 2;
        AssertEx.Equal(1, modes.Count);
        AssertEx.Equal(FeedbackMode.AudioOnly, modes[0]);
        InvokeClick(preview);
        AssertEx.Equal(1, previews);
        AssertEx.Equal(1, modes.Count);
        AssertEx.True(
            note.Text.Contains("toàn màn hình", StringComparison.OrdinalIgnoreCase),
            "Automatic fullscreen behavior was not explained.");

        form.ApplySnapshot(SettingsSnapshot.Sample with
        {
            FeedbackMode = FeedbackMode.VisualOnly,
        });
        AssertEx.Equal(1, selector.SelectedIndex);
        AssertEx.Equal(1, modes.Count);
    }

    [KeyinaTest("typing test reports pass and fail through the runtime action")]
    private static void TypingTestReportsRuntimeEvidence()
    {
        var results = new List<bool>();
        var actions = SettingsActions.NoOp with
        {
            RecordTypingTest = results.Add,
        };
        using var form = new SettingsForm(SettingsSnapshot.Sample, actions);
        var input = (TextBox)form.Controls.Find("typingTestInput", true).Single();

        input.Text = "tiếng Việt";
        input.Text = "tieengs Vieetj";

        AssertEx.Equal(2, results.Count);
        AssertEx.True(results[0], "Successful focused typing was not recorded.");
        AssertEx.False(results[1], "Failed focused typing was not recorded.");
    }

    [KeyinaTest("applications settings edit bounded executable-name rules")]
    private static void ApplicationRulesAreEditable()
    {
        var saved = new List<ApplicationPreferences>();
        var actions = SettingsActions.NoOp with
        {
            SetApplicationPreferences = saved.Add,
            GetForegroundApplicationName = () => "Code.EXE",
        };
        var snapshot = SettingsSnapshot.Sample with
        {
            Applications = new ApplicationPreferences(
                DisableVietnamese: ["game.exe"],
                DisableSpeech: [],
                DisableTranslation: ["vault.exe"],
                SuppressVisualFeedback: []),
        };
        using var form = new SettingsForm(snapshot, actions);

        foreach (var name in new[]
                 {
                     "disableVietnameseApplications",
                     "disableSpeechApplications",
                     "disableTranslationApplications",
                     "suppressVisualFeedbackApplications",
                     "applicationSpeechRuleAddCurrent",
                     "saveApplicationPreferences",
                 })
        {
            AssertEx.Equal(1, form.Controls.Find(name, true).Length);
        }
        AssertEx.Equal(
            "game.exe",
            ((TextBox)form.Controls.Find(
                "disableVietnameseApplications",
                true).Single()).Text);
        InvokeClick((Button)form.Controls.Find(
            "applicationSpeechRuleAddCurrent",
            true).Single());
        InvokeClick((Button)form.Controls.Find(
            "saveApplicationPreferences",
            true).Single());

        AssertEx.Equal(1, saved.Count);
        AssertEx.True(saved[0].DisableSpeech.SequenceEqual(["code.exe"]),
            "Foreground executable was not normalized into the speech exclusion list.");
        AssertEx.True(saved[0].DisableTranslation.SequenceEqual(["vault.exe"]),
            "Existing translation exclusion was not preserved.");
    }

    [KeyinaTest("diagnostics exposes safe settings import and export controls")]
    private static void DiagnosticsExposesSafeSettingsPortability()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);

        AssertEx.Equal(1, form.Controls.Find("exportSettings", true).Length);
        AssertEx.Equal(1, form.Controls.Find("importSettings", true).Length);
        var privacy = (Label)form.Controls.Find(
            "settingsPortabilityPrivacy",
            true).Single();
        AssertEx.True(
            privacy.Text.Contains("API key", StringComparison.OrdinalIgnoreCase),
            "Settings portability copy did not exclude API keys.");
        AssertEx.True(
            privacy.Text.Contains("không bao giờ", StringComparison.OrdinalIgnoreCase),
            "Settings portability copy did not make the privacy invariant explicit.");
    }

    [KeyinaTest("diagnostics exposes opt-in per-stage typing latency without content capture")]
    private static void DiagnosticsExposesTypingLatency()
    {
        var enabledValues = new List<bool>();
        var clearCount = 0;
        IReadOnlyList<TypingLatencyStageSnapshot> snapshot =
        [
            new TypingLatencyStageSnapshot(
                TypingLatencyStage.EngineProcess,
                SampleCount: 24,
                MedianNanoseconds: 220,
                P95Nanoseconds: 410,
                P99Nanoseconds: 620,
                MaximumNanoseconds: 910,
                MeanNanoseconds: 280.5),
        ];
        var actions = SettingsActions.NoOp with
        {
            SetTypingLatencyEnabled = enabledValues.Add,
            GetTypingLatencySnapshot = () => snapshot,
            ClearTypingLatency = () => clearCount++,
        };
        using var form = new SettingsForm(SettingsSnapshot.Sample, actions);

        var diagnostics = (Button)form.Controls.Find("navDiagnostics", true).Single();
        var toggle = (CheckBox)form.Controls.Find("typingLatencyToggle", true).Single();
        var refresh = (Button)form.Controls.Find("refreshTypingLatency", true).Single();
        var clear = (Button)form.Controls.Find("clearTypingLatency", true).Single();
        var table = (ListView)form.Controls.Find("typingLatencyTable", true).Single();
        var privacy = (Label)form.Controls.Find("typingLatencyPrivacy", true).Single();

        InvokeClick(diagnostics);
        toggle.Checked = false;
        enabledValues.Clear();
        toggle.Checked = true;
        InvokeClick(refresh);

        AssertEx.Equal(1, enabledValues.Count);
        AssertEx.True(enabledValues[0], "Latency profiling toggle did not call the runtime action.");
        AssertEx.Equal(1, table.Items.Count);
        AssertEx.Equal("Engine", table.Items[0].Text);
        AssertEx.True(
            privacy.Text.Contains("không ghi nội dung", StringComparison.OrdinalIgnoreCase),
            "Latency privacy copy must explicitly reject content capture.");

        InvokeClick(clear);
        AssertEx.Equal(1, clearCount);
        AssertEx.Equal(0, table.Items.Count);

    }

    [KeyinaTest("settings form applies runtime snapshots without exposing secret text")]
    private static void SnapshotUpdatesVisibleState()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);
        form.ApplySnapshot(SettingsSnapshot.Sample with
        {
            VietnameseEnabled = false,
            StartupEnabled = false,
            Listening = true,
            SpeechCredentialConfigured = false,
            TranslationEnabled = true,
            TranslationCredentialConfigured = false,
            TranslationHotkeyRegistered = false,
            TranslationTargetLanguage = "VI",
            StatusMessage = "Listening",
        });

        AssertEx.True(
            !((CheckBox)form.Controls.Find("vietnameseToggle", true).Single()).Checked,
            "Vietnamese toggle did not update.");
        AssertEx.True(
            !((CheckBox)form.Controls.Find("startupToggle", true).Single()).Checked,
            "Startup toggle did not update.");
        AssertEx.Equal(
            "Đang nghe",
            ((Label)form.Controls.Find("statusMessage", true).Single()).Text);
        AssertEx.Equal(
            "Chưa cấu hình",
            ((Label)form.Controls.Find("speechCredentialStatus", true).Single()).Text);
        AssertEx.Equal(
            "Cần khóa API",
            ((Label)form.Controls.Find("translationHotkeyStatus", true).Single()).Text);
    }

    [KeyinaTest("settings form uses localized production copy")]
    private static void ProductionCopyIsVietnameseFirst()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);
        var visibleText = string.Join(
            "\n",
            FindDescendants<Control>(form)
                .Select(control => control.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        foreach (var forbidden in new[]
                 {
                     "Optional Vietnamese realtime dictation",
                     "Speechmatics credential",
                     "Run offline checks",
                     "Open config folder",
                     "Toggle Vietnamese input",
                     "Registration status",
                 })
        {
            AssertEx.True(!visibleText.Contains(forbidden, StringComparison.Ordinal),
                $"Mixed-language production copy remained: {forbidden}.");
        }
    }

    [KeyinaTest("settings screenshot renderer creates a deterministic review image")]
    private static void ScreenshotRendererCreatesReviewImage()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"Keyina.Screenshot.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings-overview.png");
        try
        {
            SettingsScreenshotRenderer.Render(path, SettingsSnapshot.Sample);

            AssertEx.True(File.Exists(path), "Settings screenshot was not created.");
            using var image = Image.FromFile(path);
            AssertEx.Equal(980, image.Width);
            AssertEx.Equal(690, image.Height);
            AssertEx.True(new FileInfo(path).Length > 10_000,
                "Settings screenshot was unexpectedly small or blank.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void InvokeClick(Button button)
    {
        var onClick = button.GetType().GetMethod(
            "OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Button click handler could not be invoked.");
        _ = onClick.Invoke(button, [EventArgs.Empty]);
    }

    private static KeyEventArgs InvokeKeyDown(Control control, Keys key)
    {
        var onKeyDown = control.GetType().GetMethod(
            "OnKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Control key handler could not be invoked.");
        var eventArgs = new KeyEventArgs(key);
        _ = onKeyDown.Invoke(control, [eventArgs]);
        return eventArgs;
    }

    private static List<TControl> FindDescendants<TControl>(Control root)
        where TControl : Control
    {
        var results = new List<TControl>();
        foreach (Control child in root.Controls)
        {
            if (child is TControl typed)
            {
                results.Add(typed);
            }
            results.AddRange(FindDescendants<TControl>(child));
        }
        return results;
    }

    [KeyinaTest("settings screenshot gallery covers every navigation section")]
    private static void ScreenshotGalleryCoversEverySection()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"Keyina.Screenshot.Gallery.Tests.{Guid.NewGuid():N}");
        try
        {
            var paths = SettingsScreenshotRenderer.RenderGallery(
                directory,
                SettingsSnapshot.Sample);

            var expectedNames = new[]
            {
                "overview.png",
                "typing.png",
                "speech.png",
                "translation.png",
                "hotkeys.png",
                "applications.png",
                "snippets.png",
                "diagnostics.png",
            };
            AssertEx.Equal(expectedNames.Length, paths.Count);
            AssertEx.True(
                expectedNames.SequenceEqual(
                    paths.Select(Path.GetFileName),
                    StringComparer.Ordinal),
                "Screenshot gallery names or ordering changed.");
            foreach (var path in paths)
            {
                AssertEx.True(File.Exists(path), $"Missing gallery screenshot: {path}.");
                using var image = Image.FromFile(path);
                AssertEx.Equal(980, image.Width);
                AssertEx.Equal(690, image.Height);
                AssertEx.True(new FileInfo(path).Length > 10_000,
                    $"Gallery screenshot was unexpectedly small: {path}.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
