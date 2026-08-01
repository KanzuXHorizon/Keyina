using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Overlay;

namespace Keyina.Host.Tests;

internal static class KeystrokeOverlayPreferencesTests
{
    [KeyinaTest("keystroke overlay defaults are disabled and valid")]
    private static void DefaultsAreDisabledAndValid()
    {
        var preferences = KeystrokeOverlayPreferences.Default;
        AssertEx.False(preferences.Enabled, "Overlay must remain opt-in by default.");
        AssertEx.Equal(KeystrokeOverlayMotionLevel.Adaptive, preferences.Motion);
        preferences.Validate();
    }

    [KeyinaTest("keystroke overlay validates bounded settings")]
    private static void ValidatesBounds()
    {
        AssertThrows(() => (KeystrokeOverlayPreferences.Default with
        {
            SizePercent = 74,
        }).Validate());
        AssertThrows(() => (KeystrokeOverlayPreferences.Default with
        {
            OpacityPercent = 101,
        }).Validate());
        AssertThrows(() => (KeystrokeOverlayPreferences.Default with
        {
            HideDelayMilliseconds = 2_001,
        }).Validate());
        AssertThrows(() => (KeystrokeOverlayPreferences.Default with
        {
            Motion = (KeystrokeOverlayMotionLevel)999,
        }).Validate());
        AssertThrows(() => (KeystrokeOverlayPreferences.Default with
        {
            FallbackCorner = (KeystrokeOverlayFallbackCorner)999,
        }).Validate());
    }

    [KeyinaTest("configuration schema remains version one with overlay preferences")]
    private static void SchemaRemainsVersionOne()
    {
        AssertEx.Equal(1, KeyinaConfiguration.CurrentSchemaVersion);
        var configuration = KeyinaConfiguration.Default with
        {
            KeystrokeOverlay = KeystrokeOverlayPreferences.Default with
            {
                Enabled = true,
                SizePercent = 125,
            },
        };
        _ = configuration.ValidateAndCreateSnippets();
    }

    private static void AssertThrows(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Expected validation to throw.");
    }
}
