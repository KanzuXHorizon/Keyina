using System.Reflection;
using Keyina.Host.Configuration;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Translation;
using Keyina.Host.Runtime;
using Keyina.Host.Translation;
using Keyina.Host.UI;
using Keyina.Host.UI.Feedback;
using Keyina.Host.Windows.Credentials;
using Keyina.Host.Windows.Feedback;

namespace Keyina.Host.Tests;

internal static class KeyinaApplicationContextTests
{
    [KeyinaTest("resident context loads configuration toggles input and creates settings lazily")]
    private static void SafeRuntimeLifecycleWorks()
    {
        using var directory = new TemporaryDirectory();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}");

        using var context = new KeyinaApplicationContext(options);
        AssertEx.True(context.CurrentState.VietnameseEnabled,
            "Vietnamese input did not start enabled.");
        AssertEx.True(!context.SettingsCreated,
            "Settings form was created before the user opened it.");
        AssertEx.True(!context.NotifyIconVisible,
            "Safe runtime unexpectedly exposed a tray icon.");

        context.DispatchCommandAsync(
                HotkeyCommand.ToggleVietnamese,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2))
            .GetAwaiter().GetResult();
        AssertEx.True(!context.CurrentState.VietnameseEnabled,
            "Toggle command did not update runtime state.");

        context.OpenSettings();
        AssertEx.True(context.SettingsCreated,
            "Settings form was not created lazily.");
        context.CloseSettings();
        AssertEx.True(!context.SettingsCreated,
            "Closed settings form remained owned by the context.");

        context.Dispose();
        context.Dispose();
    }

    [KeyinaTest("resident context publishes shortcut feedback without changing focus state")]
    private static void ToggleVietnamesePublishesFeedback()
    {
        using var directory = new TemporaryDirectory();
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}") with
        {
            ForegroundPresentationProbeFactory =
                static () => new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            FeedbackOverlayFactory = () => overlay,
            FeedbackSoundPlayerFactory = () => sound,
        };

        using var context = new KeyinaApplicationContext(options);
        context.DispatchCommandAsync(
                HotkeyCommand.ToggleVietnamese,
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(1, overlay.Events.Count);
        AssertEx.Equal(FeedbackEventKind.VietnameseDisabled, overlay.Events[0].Kind);
        AssertEx.Equal(1, sound.Cues.Count);
        AssertEx.Equal(FeedbackSoundCue.Disabled, sound.Cues[0]);
        AssertEx.Equal(null, context.CurrentState.ErrorCode);
    }

    [KeyinaTest("feedback failures never fail the resident host command")]
    private static void FeedbackFailureIsIsolatedFromRuntimeState()
    {
        using var directory = new TemporaryDirectory();
        var sound = new RecordingSoundPlayer();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}") with
        {
            ForegroundPresentationProbeFactory =
                static () => new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            FeedbackOverlayFactory = static () => new ThrowingOverlay(),
            FeedbackSoundPlayerFactory = () => sound,
        };

        using var context = new KeyinaApplicationContext(options);
        context.DispatchCommandAsync(
                HotkeyCommand.ToggleVietnamese,
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.False(context.CurrentState.VietnameseEnabled,
            "Feedback failure prevented the input-mode command.");
        AssertEx.Equal(null, context.CurrentState.ErrorCode);
        AssertEx.Equal(1, sound.Cues.Count);
    }

    [KeyinaTest("resident settings persist feedback mode and preview the selected channels")]
    private static void FeedbackSettingsPersistAndPreview()
    {
        using var directory = new TemporaryDirectory();
        var configurationPath = Path.Combine(directory.Path, "settings.json");
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            configurationPath,
            $"Keyina.Tests.{Guid.NewGuid():N}") with
        {
            ForegroundPresentationProbeFactory =
                static () => new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            FeedbackOverlayFactory = () => overlay,
            FeedbackSoundPlayerFactory = () => sound,
        };

        using var context = new KeyinaApplicationContext(options);
        context.OpenSettings();
        var formField = typeof(KeyinaApplicationContext).GetField(
            "settingsForm",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Settings form field was not found.");
        var form = (SettingsForm?)formField.GetValue(context)
            ?? throw new InvalidOperationException("Settings form was not created.");
        var selector = (ComboBox)form.Controls.Find("feedbackMode", true).Single();
        var preview = (Button)form.Controls.Find("previewFeedback", true).Single();

        selector.SelectedIndex = 2;
        InvokeClick(preview);

        AssertEx.Equal(FeedbackMode.AudioOnly, context.CurrentSettingsSnapshot.FeedbackMode);
        AssertEx.Equal(0, overlay.Events.Count);
        AssertEx.Equal(1, sound.Cues.Count);
        AssertEx.True(
            SpinWait.SpinUntil(
                () => ConfigurationSaveCompleted(
                    configurationPath,
                    FeedbackMode.AudioOnly),
                TimeSpan.FromSeconds(2)),
            "Feedback settings were not persisted atomically.");
        var persisted = new AtomicConfigurationStore(configurationPath)
            .LoadAsync(CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertEx.Equal(FeedbackMode.AudioOnly, persisted.Feedback!.Mode);
        context.CloseSettings();
    }

    [KeyinaTest("resident context never reports ready without native TSF and focused typing evidence")]
    private static void RuntimeReadinessIsTruthful()
    {
        using var directory = new TemporaryDirectory();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}");
        using var context = new KeyinaApplicationContext(options);

        AssertEx.True(
            context.CurrentSettingsSnapshot.Readiness != Keyina.Host.UI.KeyinaReadiness.Ready,
            "Self-test runtime claimed Ready without TSF, IPC, or typing evidence.");
    }

    [KeyinaTest("resident context exposes complete tray commands without changing global state")]
    private static void TrayMenuContractIsComplete()
    {
        using var directory = new TemporaryDirectory();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}");
        using var context = new KeyinaApplicationContext(options);

        var commands = context.TrayCommandNames;
        AssertEx.True(context.TrayUsesCustomRenderer,
            "Tray menu should use the Fluent renderer.");
        AssertEx.True(context.TrayShowsImageMargin,
            "Tray menu should reserve a consistent icon column.");
        AssertEx.True(context.TrayHorizontalPadding >= 8,
            "Tray menu padding is too tight for a modern touch-friendly surface.");
        foreach (var expected in new[]
                 {
                     "status",
                     "setup",
                     "toggleVietnamese",
                     "toggleDictation",
                     "translateSelection",
                     "startup",
                     "settings",
                     "exit",
                 })
        {
            AssertEx.True(commands.Contains(expected, StringComparer.Ordinal),
                $"Tray command {expected} was missing.");
        }
    }

    [KeyinaTest("translation tray command stays unavailable until a DeepL credential exists")]
    private static void TranslationTrayRequiresCredential()
    {
        using var directory = new TemporaryDirectory();
        var configurationPath = Path.Combine(directory.Path, "settings.json");
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            configurationPath,
            $"Keyina.Tests.{Guid.NewGuid():N}");
        new AtomicConfigurationStore(configurationPath)
            .SaveAsync(
                KeyinaConfiguration.Default with { TranslationEnabled = true },
                CancellationToken.None)
            .GetAwaiter().GetResult();

        using var context = new KeyinaApplicationContext(
            options,
            new FakeCredentialVault(null),
            new FakeTranslationProvider(),
            new FakeSelectionAccessor());
        var menuItemField = typeof(KeyinaApplicationContext).GetField(
            "translateSelectionMenuItem",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Translation tray item field was not found.");
        var menuItem = (ToolStripMenuItem?)menuItemField.GetValue(context)
            ?? throw new InvalidOperationException("Translation tray item was not created.");

        AssertEx.False(menuItem.Enabled,
            "Translation tray command should not run without a configured credential.");
        AssertEx.Equal("Cần khóa DeepL", menuItem.ShortcutKeyDisplayString);
        AssertEx.True(context.CurrentSettingsSnapshot.TranslationEnabled,
            "The user's translation preference should remain visible while setup is incomplete.");
        AssertEx.False(context.CurrentSettingsSnapshot.TranslationCredentialConfigured,
            "Missing credentials were incorrectly reported as configured.");
    }

    [KeyinaTest("removing the DeepL credential disables translation and persists the safe state")]
    private static void RemovingTranslationCredentialDisablesFeature()
    {
        using var directory = new TemporaryDirectory();
        var configurationPath = Path.Combine(directory.Path, "settings.json");
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            configurationPath,
            $"Keyina.Tests.{Guid.NewGuid():N}");
        new AtomicConfigurationStore(configurationPath)
            .SaveAsync(
                KeyinaConfiguration.Default with { TranslationEnabled = true },
                CancellationToken.None)
            .GetAwaiter().GetResult();
        var credentialVault = new FakeCredentialVault("test-key:fx");

        using var context = new KeyinaApplicationContext(
            options,
            credentialVault,
            new FakeTranslationProvider(),
            new FakeSelectionAccessor());
        context.OpenSettings();
        var formField = typeof(KeyinaApplicationContext).GetField(
            "settingsForm",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Settings form field was not found.");
        var form = (SettingsForm?)formField.GetValue(context)
            ?? throw new InvalidOperationException("Settings form was not created.");
        var remove = (Button)form.Controls.Find("removeDeepLKey", true).Single();

        InvokeClick(remove);

        AssertEx.Equal(null, credentialVault.Read(CredentialTargets.DeepLApiKey));
        AssertEx.False(context.CurrentSettingsSnapshot.TranslationEnabled,
            "Translation remained enabled after its credential was removed.");
        AssertEx.False(context.CurrentSettingsSnapshot.TranslationCredentialConfigured,
            "Removed credential remained visible as configured.");
        AssertEx.True(
            SpinWait.SpinUntil(
                () => TranslationSettingSaveCompleted(configurationPath, expectedEnabled: false),
                TimeSpan.FromSeconds(2)),
            "Disabled translation state was not persisted after credential removal.");
        context.CloseSettings();
    }

    [KeyinaTest("resident context translates the selected text with configured DeepL credentials")]
    private static void TranslationCommandUsesConfiguredProvider()
    {
        using var directory = new TemporaryDirectory();
        var configurationPath = Path.Combine(directory.Path, "settings.json");
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            configurationPath,
            $"Keyina.Tests.{Guid.NewGuid():N}") with
        {
            ForegroundPresentationProbeFactory =
                static () => new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            FeedbackOverlayFactory = () => overlay,
            FeedbackSoundPlayerFactory = () => sound,
        };
        var store = new AtomicConfigurationStore(configurationPath);
        store.SaveAsync(
                KeyinaConfiguration.Default with
                {
                    TranslationEnabled = true,
                    TranslationTargetLanguage = "EN-US",
                },
                CancellationToken.None)
            .GetAwaiter().GetResult();
        var credentialVault = new FakeCredentialVault("test-key:fx");
        var provider = new FakeTranslationProvider();
        var accessor = new FakeSelectionAccessor();

        using var context = new KeyinaApplicationContext(
            options,
            credentialVault,
            provider,
            accessor);
        context.DispatchCommandAsync(
                HotkeyCommand.TranslateSelection,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2))
            .GetAwaiter().GetResult();

        AssertEx.Equal(1, provider.CallCount);
        AssertEx.Equal("test-key:fx", provider.LastApiKey);
        AssertEx.Equal("Xin chào", provider.LastRequest!.Text);
        AssertEx.Equal("EN-US", provider.LastRequest.TargetLanguage);
        AssertEx.Equal(1, accessor.ReplaceCount);
        AssertEx.Equal("Hello", accessor.LastReplacement);
        AssertEx.Equal(2, overlay.Events.Count);
        AssertEx.Equal(FeedbackEventKind.TranslationStarted, overlay.Events[0].Kind);
        AssertEx.Equal(FeedbackEventKind.TranslationCompleted, overlay.Events[1].Kind);
        AssertEx.Equal(2, sound.Cues.Count);
        AssertEx.Equal(FeedbackSoundCue.Start, sound.Cues[0]);
        AssertEx.Equal(FeedbackSoundCue.Success, sound.Cues[1]);
    }

    private static void InvokeClick(Button button)
    {
        var onClick = button.GetType().GetMethod(
            "OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Button click handler could not be invoked.");
        _ = onClick.Invoke(button, [EventArgs.Empty]);
    }

    private static bool TranslationSettingSaveCompleted(string path, bool expectedEnabled)
    {
        if (!File.Exists(path) || File.Exists(path + ".tmp"))
        {
            return false;
        }

        try
        {
            var persisted = new AtomicConfigurationStore(path)
                .LoadAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            return persisted.TranslationEnabled == expectedEnabled;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ConfigurationSaveCompleted(
        string path,
        FeedbackMode expectedMode)
    {
        if (!File.Exists(path) || File.Exists(path + ".tmp"))
        {
            return false;
        }

        try
        {
            var persisted = new AtomicConfigurationStore(path)
                .LoadAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            return persisted.Feedback?.Mode == expectedMode;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class FixedForegroundProbe(ForegroundPresentationState state)
        : IForegroundPresentationProbe
    {
        public ForegroundPresentationState GetState() => state;
    }

    private sealed class RecordingOverlay : IFeedbackOverlay
    {
        public List<FeedbackEvent> Events { get; } = [];

        public void Present(FeedbackEvent feedbackEvent) => Events.Add(feedbackEvent);

        public void HideFeedback()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingOverlay : IFeedbackOverlay
    {
        public void Present(FeedbackEvent feedbackEvent) =>
            throw new InvalidOperationException("overlay failed");

        public void HideFeedback()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSoundPlayer : IFeedbackSoundPlayer
    {
        public List<FeedbackSoundCue> Cues { get; } = [];

        public void Play(FeedbackSoundCue cue) => Cues.Add(cue);
    }

    private sealed class FakeCredentialVault(string? initialSecret) : ICredentialVault
    {
        private string? secret = initialSecret;

        public void Write(string target, string value) => secret = value;

        public string? Read(string target) => secret;

        public bool Delete(string target)
        {
            var existed = secret is not null;
            secret = null;
            return existed;
        }
    }

    private sealed class FakeTranslationProvider : ITranslationProvider
    {
        public int CallCount { get; private set; }

        public string? LastApiKey { get; private set; }

        public TranslationRequest? LastRequest { get; private set; }

        public Task<TranslationResult> TranslateAsync(
            string apiKey,
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastApiKey = apiKey;
            LastRequest = request;
            return Task.FromResult(new TranslationResult("Hello", "VI", "Fake"));
        }
    }

    private sealed class FakeSelectionAccessor : ISelectedTextAccessor
    {
        public int ReplaceCount { get; private set; }

        public string? LastReplacement { get; private set; }

        public Task<SelectedTextCapture?> CaptureAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SelectedTextCapture?>(
                new("Xin chào", (nint)42, (nint)420));

        public bool TryReplace(SelectedTextCapture selectedText, string translatedText)
        {
            ReplaceCount++;
            LastReplacement = translatedText;
            return true;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Keyina.Runtime.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
