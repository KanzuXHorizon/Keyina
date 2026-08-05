using Keyina.Host.Core.Configuration;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.Tests;

internal static class FluentThemeTests
{
    [KeyinaTest("stored light and dark themes override the Windows app theme")]
    private static void StoredThemeOverridesSystemTheme()
    {
        AssertEx.Equal(
            FluentThemeMode.Light,
            FluentTheme.Resolve(
                KeyinaTheme.Light,
                highContrast: false,
                systemDark: true).Mode);
        AssertEx.Equal(
            FluentThemeMode.Dark,
            FluentTheme.Resolve(
                KeyinaTheme.Dark,
                highContrast: false,
                systemDark: false).Mode);
    }

    [KeyinaTest("system theme follows Windows and high contrast always wins")]
    private static void SystemAndHighContrastResolutionIsDeterministic()
    {
        AssertEx.Equal(
            FluentThemeMode.Dark,
            FluentTheme.Resolve(
                KeyinaTheme.System,
                highContrast: false,
                systemDark: true).Mode);
        AssertEx.Equal(
            FluentThemeMode.Light,
            FluentTheme.Resolve(
                KeyinaTheme.System,
                highContrast: false,
                systemDark: false).Mode);
        AssertEx.Equal(
            FluentThemeMode.HighContrast,
            FluentTheme.Resolve(
                KeyinaTheme.Dark,
                highContrast: true,
                systemDark: false).Mode);
    }
}
