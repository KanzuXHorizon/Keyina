using System.Security;
using Microsoft.Win32;

namespace Keyina.Host.UI.Fluent;

public enum FluentThemeMode
{
    Light,
    Dark,
    HighContrast,
}

public enum FluentTone
{
    Neutral,
    Accent,
    Success,
    Warning,
    Error,
}

public sealed record FluentThemePalette(
    FluentThemeMode Mode,
    Color Window,
    Color Sidebar,
    Color Surface,
    Color SurfaceSecondary,
    Color SurfaceHover,
    Color SurfacePressed,
    Color Border,
    Color BorderStrong,
    Color TextPrimary,
    Color TextSecondary,
    Color TextTertiary,
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color AccentText,
    Color Success,
    Color Warning,
    Color Error,
    Color Focus)
{
    public bool IsDark => Mode == FluentThemeMode.Dark;
}

public static class FluentTheme
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly FluentThemePalette DarkPalette = new(
        FluentThemeMode.Dark,
        Window: Color.FromArgb(32, 32, 32),
        Sidebar: Color.FromArgb(28, 28, 28),
        Surface: Color.FromArgb(45, 45, 45),
        SurfaceSecondary: Color.FromArgb(39, 39, 39),
        SurfaceHover: Color.FromArgb(55, 55, 55),
        SurfacePressed: Color.FromArgb(61, 61, 61),
        Border: Color.FromArgb(63, 63, 63),
        BorderStrong: Color.FromArgb(82, 82, 82),
        TextPrimary: Color.FromArgb(255, 255, 255),
        TextSecondary: Color.FromArgb(206, 206, 206),
        TextTertiary: Color.FromArgb(157, 157, 157),
        Accent: Color.FromArgb(22, 119, 255),
        AccentHover: Color.FromArgb(55, 139, 255),
        AccentPressed: Color.FromArgb(13, 98, 218),
        AccentText: Color.White,
        Success: Color.FromArgb(76, 194, 140),
        Warning: Color.FromArgb(252, 194, 73),
        Error: Color.FromArgb(255, 99, 117),
        Focus: Color.FromArgb(137, 180, 255));

    private static readonly FluentThemePalette LightPalette = new(
        FluentThemeMode.Light,
        Window: Color.FromArgb(243, 243, 243),
        Sidebar: Color.FromArgb(238, 238, 238),
        Surface: Color.FromArgb(255, 255, 255),
        SurfaceSecondary: Color.FromArgb(249, 249, 249),
        SurfaceHover: Color.FromArgb(245, 245, 245),
        SurfacePressed: Color.FromArgb(236, 236, 236),
        Border: Color.FromArgb(229, 229, 229),
        BorderStrong: Color.FromArgb(207, 207, 207),
        TextPrimary: Color.FromArgb(26, 26, 26),
        TextSecondary: Color.FromArgb(80, 80, 80),
        TextTertiary: Color.FromArgb(112, 112, 112),
        Accent: Color.FromArgb(22, 119, 255),
        AccentHover: Color.FromArgb(0, 102, 224),
        AccentPressed: Color.FromArgb(0, 91, 197),
        AccentText: Color.White,
        Success: Color.FromArgb(15, 123, 82),
        Warning: Color.FromArgb(157, 93, 0),
        Error: Color.FromArgb(196, 43, 61),
        Focus: Color.FromArgb(0, 95, 184));

    public static FluentThemePalette Current
    {
        get
        {
            if (SystemInformation.HighContrast)
            {
                return CreateHighContrastPalette();
            }

            return IsSystemDarkMode() ? DarkPalette : LightPalette;
        }
    }

    public static string SystemThemeDescription => Current.Mode switch
    {
        FluentThemeMode.Dark => "Tối · theo Windows",
        FluentThemeMode.Light => "Sáng · theo Windows",
        FluentThemeMode.HighContrast => "Tương phản cao · theo Windows",
        _ => "Theo Windows",
    };

    public static void InitializeApplicationColorMode()
    {
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
    }

    public static Color ToneColor(FluentThemePalette palette, FluentTone tone) => tone switch
    {
        FluentTone.Accent => palette.Accent,
        FluentTone.Success => palette.Success,
        FluentTone.Warning => palette.Warning,
        FluentTone.Error => palette.Error,
        _ => palette.TextSecondary,
    };

    private static bool IsSystemDarkMode()
    {
        try
        {
            var value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
            return value is int integer && integer == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    private static FluentThemePalette CreateHighContrastPalette() => new(
        FluentThemeMode.HighContrast,
        Window: SystemColors.Window,
        Sidebar: SystemColors.Control,
        Surface: SystemColors.Window,
        SurfaceSecondary: SystemColors.Control,
        SurfaceHover: SystemColors.Highlight,
        SurfacePressed: SystemColors.Highlight,
        Border: SystemColors.WindowText,
        BorderStrong: SystemColors.WindowText,
        TextPrimary: SystemColors.WindowText,
        TextSecondary: SystemColors.WindowText,
        TextTertiary: SystemColors.GrayText,
        Accent: SystemColors.Highlight,
        AccentHover: SystemColors.Highlight,
        AccentPressed: SystemColors.Highlight,
        AccentText: SystemColors.HighlightText,
        Success: SystemColors.WindowText,
        Warning: SystemColors.WindowText,
        Error: SystemColors.WindowText,
        Focus: SystemColors.Highlight);
}
