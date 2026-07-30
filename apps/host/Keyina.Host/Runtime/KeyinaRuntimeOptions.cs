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
            DisplaySettingsWindows: true);

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
            DisplaySettingsWindows: false);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConfigurationPath);
        if (!Path.IsPathFullyQualified(ConfigurationPath))
        {
            throw new ArgumentException(
                "Runtime configuration path must be fully qualified.",
                nameof(ConfigurationPath));
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
