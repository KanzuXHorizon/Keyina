using Keyina.Host.Core.Applications;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Overlay;
using Keyina.Host.Core.Translation;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.UI;

public enum CredentialSetupTarget
{
    Speechmatics,
    Translation,
    LibreTranslate,
}

public sealed record SettingsSnapshot(
    bool VietnameseEnabled,
    bool SpeechEnabled,
    bool StartupEnabled,
    bool Listening,
    bool SpeechCredentialConfigured,
    int CustomSnippetCount,
    string StatusMessage,
    string Version,
    string IpcStatus,
    string HotkeyStatus,
    bool TsfRegistered,
    FeedbackMode FeedbackMode = Keyina.Host.Core.Feedback.FeedbackMode.Automatic)
{
    public KeyinaHealthSnapshot Health { get; init; } = KeyinaHealthSnapshot.Healthy;

    public string SpeechLanguage { get; init; } = "auto";

    public bool TranslationEnabled { get; init; }

    public bool TranslationCredentialConfigured { get; init; }

    public bool TranslationPreviewEnabled { get; init; }

    public bool TraditionalTonePlacement { get; init; }

    public bool QuickTelexLetters { get; init; }

    public bool StandaloneWToUHorn { get; init; } = true;

    public bool ClipboardCompatibilityEnabled { get; init; }

    public bool LibreTranslateCredentialConfigured { get; init; }

    public TranslationProviderPreferences TranslationProviders { get; init; } =
        TranslationProviderPreferences.Default;

    public bool TranslationHotkeyRegistered { get; init; }

    public string TranslationTargetLanguage { get; init; } = "VI";

    public HotkeyPreferences Hotkeys { get; init; } = HotkeyPreferences.Default;

    public ApplicationPreferences Applications { get; init; } = ApplicationPreferences.Default;

    public KeystrokeOverlayPreferences KeystrokeOverlay { get; init; } =
        KeystrokeOverlayPreferences.Default;

    public IReadOnlyList<SnippetConfiguration> Snippets { get; init; } = Array.Empty<SnippetConfiguration>();

    public KeyinaReadiness Readiness => ReadinessMapper.Map(Health);
    public static SettingsSnapshot Sample { get; } = new(
        VietnameseEnabled: true,
        SpeechEnabled: true,
        StartupEnabled: true,
        Listening: false,
        SpeechCredentialConfigured: true,
        CustomSnippetCount: 3,
        StatusMessage: "Ready",
        Version: "0.1.6-dev",
        IpcStatus: "Focused app connected",
        HotkeyStatus: "Registered",
        TsfRegistered: true,
        FeedbackMode: FeedbackMode.Automatic)
    {
        TranslationEnabled = true,
        TranslationCredentialConfigured = true,
        TranslationPreviewEnabled = false,
        LibreTranslateCredentialConfigured = false,
        TranslationProviders = TranslationProviderPreferences.Default,
        TranslationHotkeyRegistered = true,
        TranslationTargetLanguage = "VI",
        Hotkeys = HotkeyPreferences.Default,
        Applications = ApplicationPreferences.Default,
        Snippets =
        [
            new(";kmail", "hello@example.com", false, false, " ", [], []),
            new(";ksig", "Trân trọng,", false, false, " ", [], []),
            new(";kaddr", "Biên Hòa, Đồng Nai", false, false, " ", [], []),
        ],
    };
}

public sealed record SettingsActions(
    Action<bool> SetVietnameseEnabled,
    Action<bool> SetSpeechEnabled,
    Action<bool> SetStartupEnabled,
    Action<string> SaveSpeechApiKey,
    Action DeleteSpeechApiKey,
    Action OpenConfigurationFolder,
    Func<CancellationToken, Task<string>> RunDiagnostics,
    Func<CancellationToken, Task<string>> SetupTsf,
    Action<bool> RecordTypingTest,
    Action<bool> SetTypingLatencyEnabled,
    Func<IReadOnlyList<TypingLatencyStageSnapshot>> GetTypingLatencySnapshot,
    Action ClearTypingLatency,
    Action<FeedbackMode> SetFeedbackMode,
    Action PreviewFeedback)
{
    public Action<string> SetSpeechLanguage { get; init; } = _ => { };

    public Action<bool> SetTranslationEnabled { get; init; } = _ => { };

    public Action<string> SetTranslationTargetLanguage { get; init; } = _ => { };

    public Action<bool> SetTranslationPreviewEnabled { get; init; } = _ => { };

    public Action<bool> SetTraditionalTonePlacement { get; init; } = _ => { };

    public Action<bool> SetQuickTelexLetters { get; init; } = _ => { };

    public Action<bool> SetStandaloneWToUHorn { get; init; } = _ => { };

    public Action<bool> SetClipboardCompatibilityEnabled { get; init; } = _ => { };

    public Action<TranslationProviderPreferences> SetTranslationProviders { get; init; } = _ => { };

    public Action<string> SaveDeepLApiKey { get; init; } = _ => { };

    public Action DeleteDeepLApiKey { get; init; } = () => { };

    public Action<string> SaveLibreTranslateApiKey { get; init; } = _ => { };

    public Action DeleteLibreTranslateApiKey { get; init; } = () => { };

    public Action<HotkeyCommand, HotkeyChord> SetHotkey { get; init; } = (_, _) => { };

    public Action<HotkeyCommand> ResetHotkey { get; init; } = _ => { };

    public Action ResetAllHotkeys { get; init; } = () => { };

    public Action<string> ExportSettings { get; init; } = _ => { };

    public Action<string> ImportSettings { get; init; } = _ => { };

    public Action<ApplicationPreferences> SetApplicationPreferences { get; init; } = _ => { };

    public Action<KeystrokeOverlayPreferences> SetKeystrokeOverlayPreferences { get; init; } = _ => { };

    public Action PreviewKeystrokeOverlay { get; init; } = () => { };

    public Action<IReadOnlyList<SnippetConfiguration>> SetSnippets { get; init; } = _ => { };

    public Func<string?> GetForegroundApplicationName { get; init; } = () => null;

    public static SettingsActions NoOp { get; } = new(
        _ => { },
        _ => { },
        _ => { },
        _ => { },
        () => { },
        () => { },
        _ => Task.FromResult("All offline checks passed."),
        _ => Task.FromResult("TSF setup completed."),
        _ => { },
        _ => { },
        () => Array.Empty<TypingLatencyStageSnapshot>(),
        () => { },
        _ => { },
        () => { });
}
