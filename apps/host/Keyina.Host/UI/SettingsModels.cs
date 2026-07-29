namespace Keyina.Host.UI;

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
    string HotkeyStatus)
{
    public static SettingsSnapshot Sample { get; } = new(
        VietnameseEnabled: true,
        SpeechEnabled: true,
        StartupEnabled: true,
        Listening: false,
        SpeechCredentialConfigured: true,
        CustomSnippetCount: 3,
        StatusMessage: "Ready",
        Version: "0.1.0-dev",
        IpcStatus: "Focused app connected",
        HotkeyStatus: "Registered");
}

public sealed record SettingsActions(
    Action<bool> SetVietnameseEnabled,
    Action<bool> SetSpeechEnabled,
    Action<bool> SetStartupEnabled,
    Action<string> SaveSpeechApiKey,
    Action DeleteSpeechApiKey,
    Action OpenConfigurationFolder,
    Func<CancellationToken, Task<string>> RunDiagnostics)
{
    public static SettingsActions NoOp { get; } = new(
        _ => { },
        _ => { },
        _ => { },
        _ => { },
        () => { },
        () => { },
        _ => Task.FromResult("All offline checks passed."));
}
