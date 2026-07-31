using Keyina.Host.Configuration;
using Keyina.Host.UI.Feedback;
using Keyina.Host.Windows.Feedback;
using Keyina.Host.Windows.Ipc;
using Keyina.Host.Windows.Startup;

namespace Keyina.Host.Runtime;

public sealed record KeyinaRuntimeOptions(
    string ConfigurationPath,
    string StartupValueName,
    bool EnableNotifyIcon,
    bool EnableGlobalHotkeys,
    bool EnablePipe,
    bool EnableSpeech,
    bool ShowSettingsOnStart,
    bool DisplaySettingsWindows)
{
    public string? RuntimeInputProfilePath { get; init; }

    public bool ExitWhenLastWindowCloses { get; init; }

    public bool PublishRuntimeProfileOnStartup { get; init; } = true;

    public Func<FocusedUnicodeEnvelopeWriter>? FocusedDictationWriterFactory { get; init; }

    public bool DisplayTranslationPreview { get; init; }

    public Func<IForegroundPresentationProbe>? ForegroundPresentationProbeFactory { get; init; }

    public Func<IFeedbackOverlay>? FeedbackOverlayFactory { get; init; }

    public Func<IFeedbackSoundPlayer>? FeedbackSoundPlayerFactory { get; init; }

    public static KeyinaRuntimeOptions CreateProduction(bool showSettingsOnStart) =>
        new(
            ConfigurationPaths.GetProductionPath(),
            StartupRegistrationDefaults.ValueName,
            EnableNotifyIcon: true,
            EnableGlobalHotkeys: true,
            EnablePipe: true,
            EnableSpeech: true,
            ShowSettingsOnStart: showSettingsOnStart,
            DisplaySettingsWindows: true)
        {
            DisplayTranslationPreview = true,
        };

    public static KeyinaRuntimeOptions CreateProductionCommandCompanion() =>
        new(
            ConfigurationPaths.GetProductionPath(),
            StartupRegistrationDefaults.ValueName,
            EnableNotifyIcon: false,
            EnableGlobalHotkeys: false,
            EnablePipe: false,
            EnableSpeech: true,
            ShowSettingsOnStart: false,
            DisplaySettingsWindows: false)
        {
            PublishRuntimeProfileOnStartup = false,
            DisplayTranslationPreview = true,
            FocusedDictationWriterFactory = static () =>
                new FocusedUnicodeEnvelopeWriter(),
        };

    public static KeyinaRuntimeOptions CreateProductionSettingsCompanion() =>
        CreateSettingsCompanion(
            ConfigurationPaths.GetProductionPath(),
            StartupRegistrationDefaults.ValueName);

    public static KeyinaRuntimeOptions CreateSettingsCompanion(
        string configurationPath,
        string startupValueName) =>
        new(
            configurationPath,
            startupValueName,
            EnableNotifyIcon: false,
            EnableGlobalHotkeys: false,
            EnablePipe: false,
            EnableSpeech: false,
            ShowSettingsOnStart: true,
            DisplaySettingsWindows: true)
        {
            ExitWhenLastWindowCloses = true,
        };

    public static KeyinaRuntimeOptions CreateSelfTest(
        string configurationPath,
        string startupValueName) =>
        new(
            configurationPath,
            startupValueName,
            EnableNotifyIcon: false,
            EnableGlobalHotkeys: false,
            EnablePipe: false,
            EnableSpeech: false,
            ShowSettingsOnStart: false,
            DisplaySettingsWindows: false)
        {
            RuntimeInputProfilePath = Path.Combine(
                Path.GetDirectoryName(configurationPath)
                    ?? throw new ArgumentException(
                        "Self-test configuration path has no parent directory.",
                        nameof(configurationPath)),
                "runtime-input.bin"),
        };

    public string ResolveRuntimeInputProfilePath()
    {
        if (!string.IsNullOrWhiteSpace(RuntimeInputProfilePath))
        {
            return RuntimeInputProfilePath;
        }

        var directory = Path.GetDirectoryName(ConfigurationPath)
            ?? throw new ArgumentException(
                "Runtime configuration path has no parent directory.",
                nameof(ConfigurationPath));
        return Path.Combine(directory, "runtime-input.bin");
    }

    public string ResolveRuntimeSnippetProfilePath()
    {
        var directory = Path.GetDirectoryName(ResolveRuntimeInputProfilePath())
            ?? throw new ArgumentException(
                "Runtime input profile path has no parent directory.",
                nameof(RuntimeInputProfilePath));
        return Path.Combine(directory, "runtime-snippets.bin");
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConfigurationPath);
        if (!Path.IsPathFullyQualified(ConfigurationPath))
        {
            throw new ArgumentException(
                "Runtime configuration path must be fully qualified.",
                nameof(ConfigurationPath));
        }
        var runtimeProfilePath = ResolveRuntimeInputProfilePath();
        if (!Path.IsPathFullyQualified(runtimeProfilePath))
        {
            throw new ArgumentException(
                "Runtime input profile path must be fully qualified.",
                nameof(RuntimeInputProfilePath));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(StartupValueName);
        if (StartupValueName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Runtime startup value name contains control characters.",
                nameof(StartupValueName));
        }
    }
}
