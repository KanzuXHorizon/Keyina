using System.Reflection;
using Keyina.Host.Core.Configuration;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

[KeyinaInteractiveTest]
internal static class SettingsContractParityTests
{
    private sealed record SettingBinding(
        string SnapshotMember,
        string ActionMember,
        string UiControl);

    private static readonly Dictionary<string, SettingBinding> Bindings =
        new(StringComparer.Ordinal)
        {
            [nameof(KeyinaConfiguration.VietnameseEnabled)] = new(
                nameof(SettingsSnapshot.VietnameseEnabled),
                nameof(SettingsActions.SetVietnameseEnabled),
                "vietnameseToggle"),
            [nameof(KeyinaConfiguration.SpeechEnabled)] = new(
                nameof(SettingsSnapshot.SpeechEnabled),
                nameof(SettingsActions.SetSpeechEnabled),
                "speechToggle"),
            [nameof(KeyinaConfiguration.Theme)] = new(
                nameof(SettingsSnapshot.Theme),
                nameof(SettingsActions.SetTheme),
                "themeSelector"),
            [nameof(KeyinaConfiguration.Snippets)] = new(
                nameof(SettingsSnapshot.Snippets),
                nameof(SettingsActions.SetSnippets),
                "snippetsList"),
            [nameof(KeyinaConfiguration.Feedback)] = new(
                nameof(SettingsSnapshot.FeedbackMode),
                nameof(SettingsActions.SetFeedbackMode),
                "feedbackMode"),
            [nameof(KeyinaConfiguration.SpeechLanguage)] = new(
                nameof(SettingsSnapshot.SpeechLanguage),
                nameof(SettingsActions.SetSpeechLanguage),
                "speechLanguage"),
            [nameof(KeyinaConfiguration.TranslationEnabled)] = new(
                nameof(SettingsSnapshot.TranslationEnabled),
                nameof(SettingsActions.SetTranslationEnabled),
                "translationToggle"),
            [nameof(KeyinaConfiguration.TranslationPreviewEnabled)] = new(
                nameof(SettingsSnapshot.TranslationPreviewEnabled),
                nameof(SettingsActions.SetTranslationPreviewEnabled),
                "translationPreviewToggle"),
            [nameof(KeyinaConfiguration.TraditionalTonePlacement)] = new(
                nameof(SettingsSnapshot.TraditionalTonePlacement),
                nameof(SettingsActions.SetTraditionalTonePlacement),
                "traditionalTonePlacementToggle"),
            [nameof(KeyinaConfiguration.QuickTelexLetters)] = new(
                nameof(SettingsSnapshot.QuickTelexLetters),
                nameof(SettingsActions.SetQuickTelexLetters),
                "quickTelexLettersToggle"),
            [nameof(KeyinaConfiguration.StandaloneWToUHorn)] = new(
                nameof(SettingsSnapshot.StandaloneWToUHorn),
                nameof(SettingsActions.SetStandaloneWToUHorn),
                "standaloneWToUHornToggle"),
            [nameof(KeyinaConfiguration.ClipboardCompatibilityEnabled)] = new(
                nameof(SettingsSnapshot.ClipboardCompatibilityEnabled),
                nameof(SettingsActions.SetClipboardCompatibilityEnabled),
                "clipboardCompatibilityToggle"),
            [nameof(KeyinaConfiguration.TranslationTargetLanguage)] = new(
                nameof(SettingsSnapshot.TranslationTargetLanguage),
                nameof(SettingsActions.SetTranslationTargetLanguage),
                "translationTargetLanguage"),
            [nameof(KeyinaConfiguration.TranslationProviders)] = new(
                nameof(SettingsSnapshot.TranslationProviders),
                nameof(SettingsActions.SetTranslationProviders),
                "libreTranslateEndpoint"),
            [nameof(KeyinaConfiguration.Hotkeys)] = new(
                nameof(SettingsSnapshot.Hotkeys),
                nameof(SettingsActions.SetHotkey),
                "hotkeysPage"),
            [nameof(KeyinaConfiguration.Applications)] = new(
                nameof(SettingsSnapshot.Applications),
                nameof(SettingsActions.SetApplicationPreferences),
                "applicationsPage"),
            [nameof(KeyinaConfiguration.KeystrokeOverlay)] = new(
                nameof(SettingsSnapshot.KeystrokeOverlay),
                nameof(SettingsActions.SetKeystrokeOverlayPreferences),
                "keystrokeOverlayCard"),
        };

    [KeyinaTest("every user-facing configuration field has snapshot action and UI ownership")]
    private static void UserFacingConfigurationContractIsComplete()
    {
        var intentionallyInternal = new HashSet<string>(StringComparer.Ordinal)
        {
            // Persistence metadata, not a user preference.
            nameof(KeyinaConfiguration.SchemaVersion),
            // Onboarding lifecycle state, owned by FirstRunForm rather than SettingsForm.
            nameof(KeyinaConfiguration.FirstRunCompleted),
        };
        var configurationProperties = typeof(KeyinaConfiguration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var missingBindings = configurationProperties
            .Where(name => !intentionallyInternal.Contains(name))
            .Where(name => !Bindings.ContainsKey(name))
            .ToArray();
        AssertEx.Equal(
            string.Empty,
            string.Join(", ", missingBindings),
            "User-facing configuration fields are missing settings ownership.");

        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);
        foreach (var (configurationMember, binding) in Bindings)
        {
            AssertEx.True(
                typeof(SettingsSnapshot).GetProperty(
                    binding.SnapshotMember,
                    BindingFlags.Instance | BindingFlags.Public) is not null,
                $"{configurationMember} is missing snapshot member {binding.SnapshotMember}.");
            AssertEx.True(
                typeof(SettingsActions).GetProperty(
                    binding.ActionMember,
                    BindingFlags.Instance | BindingFlags.Public) is not null,
                $"{configurationMember} is missing action member {binding.ActionMember}.");
            AssertEx.Equal(
                1,
                form.Controls.Find(binding.UiControl, searchAllChildren: true).Length,
                $"{configurationMember} is missing UI control {binding.UiControl}.");
        }
    }
}
