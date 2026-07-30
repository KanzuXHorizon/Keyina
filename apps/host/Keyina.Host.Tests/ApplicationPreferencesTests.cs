using Keyina.Host.Core.Applications;

namespace Keyina.Host.Tests;

internal static class ApplicationPreferencesTests
{
    [KeyinaTest("application preferences normalize executable names and match case insensitively")]
    private static void NormalizesAndMatchesExecutableNames()
    {
        var preferences = new ApplicationPreferences(
            DisableVietnamese: ["  CODE.EXE  "],
            DisableSpeech: ["game.exe"],
            DisableTranslation: ["password-manager.exe"],
            SuppressVisualFeedback: ["Game.EXE"])
            .Normalize();

        AssertEx.True(preferences.IsDisabled(
            ApplicationFeature.VietnameseTyping,
            "code.exe"), "Vietnamese exclusion did not match.");
        AssertEx.True(preferences.IsDisabled(
            ApplicationFeature.Speech,
            "GAME.EXE"), "Speech exclusion did not match case-insensitively.");
        AssertEx.True(preferences.IsDisabled(
            ApplicationFeature.Translation,
            "password-manager.exe"), "Translation exclusion did not match.");
        AssertEx.True(preferences.IsDisabled(
            ApplicationFeature.VisualFeedback,
            "game.exe"), "Visual feedback exclusion did not match.");
        AssertEx.False(preferences.IsDisabled(
            ApplicationFeature.Translation,
            "notepad.exe"), "Unlisted application was incorrectly excluded.");
        AssertEx.Equal("code.exe", preferences.DisableVietnamese[0]);
    }

    [KeyinaTest("application preferences reject paths wildcards duplicates and non executables")]
    private static void RejectsUnsafeApplicationRules()
    {
        foreach (var invalid in new[]
                 {
                     @"C:\Windows\notepad.exe",
                     "../notepad.exe",
                     "*.exe",
                     "notepad",
                     "bad?.exe",
                     " ",
                 })
        {
            AssertThrows<ArgumentException>(() => new ApplicationPreferences(
                DisableVietnamese: [invalid],
                DisableSpeech: [],
                DisableTranslation: [],
                SuppressVisualFeedback: []).Normalize());
        }

        AssertThrows<ArgumentException>(() => new ApplicationPreferences(
            DisableVietnamese: ["code.exe", "CODE.EXE"],
            DisableSpeech: [],
            DisableTranslation: [],
            SuppressVisualFeedback: []).Normalize());
    }

    [KeyinaTest("application preferences enforce bounded rule lists")]
    private static void EnforcesBoundedLists()
    {
        var tooMany = Enumerable.Range(0, ApplicationPreferences.MaximumEntriesPerFeature + 1)
            .Select(index => $"app{index}.exe")
            .ToArray();

        AssertThrows<ArgumentException>(() => new ApplicationPreferences(
            DisableVietnamese: tooMany,
            DisableSpeech: [],
            DisableTranslation: [],
            SuppressVisualFeedback: []).Normalize());
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
