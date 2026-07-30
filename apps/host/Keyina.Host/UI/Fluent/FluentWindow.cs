using System.Runtime.InteropServices;

namespace Keyina.Host.UI.Fluent;

public static partial class FluentWindow
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmWindowCornerRound = 2;
    private const int DwmSystemBackdropMainWindow = 2;
    private const int DwmSystemBackdropTransientWindow = 3;

    public static void Apply(Form form, FluentThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(palette);
        if (!form.IsHandleCreated || !OperatingSystem.IsWindows())
        {
            return;
        }

        var dark = palette.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(
                form.Handle,
                DwmwaUseImmersiveDarkMode,
                in dark,
                sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmwaUseImmersiveDarkModeBefore20H1,
                in dark,
                sizeof(int));
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var corners = DwmWindowCornerRound;
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmwaWindowCornerPreference,
                in corners,
                sizeof(int));

            var border = ToColorRef(palette.BorderStrong);
            var caption = ToColorRef(palette.Sidebar);
            var text = ToColorRef(palette.TextPrimary);
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmwaBorderColor,
                in border,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmwaCaptionColor,
                in caption,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmwaTextColor,
                in text,
                sizeof(int));
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var backdrop = DwmSystemBackdropMainWindow;
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmwaSystemBackdropType,
                in backdrop,
                sizeof(int));
        }
    }

    public static void ApplyTransient(Control control, FluentThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(palette);
        if (!control.IsHandleCreated || !OperatingSystem.IsWindows())
        {
            return;
        }

        var dark = palette.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(
            control.Handle,
            DwmwaUseImmersiveDarkMode,
            in dark,
            sizeof(int));

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var corners = DwmWindowCornerRound;
            _ = DwmSetWindowAttribute(
                control.Handle,
                DwmwaWindowCornerPreference,
                in corners,
                sizeof(int));
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var backdrop = DwmSystemBackdropTransientWindow;
            _ = DwmSetWindowAttribute(
                control.Handle,
                DwmwaSystemBackdropType,
                in backdrop,
                sizeof(int));
        }
    }

    private static int ToColorRef(Color color) =>
        color.R | color.G << 8 | color.B << 16;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        nint window,
        int attribute,
        in int value,
        int valueSize);
}
