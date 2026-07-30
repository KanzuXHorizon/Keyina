using System.Drawing;
using System.Runtime.InteropServices;
using Keyina.Host.Core.Feedback;

namespace Keyina.Host.Windows.Feedback;

public interface IForegroundPresentationProbe
{
    ForegroundPresentationState GetState();
}

public sealed class WindowsForegroundPresentationProbe : IForegroundPresentationProbe
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    public ForegroundPresentationState GetState()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !GetWindowRect(window, out var windowRect))
        {
            return ForegroundPresentationState.Unknown;
        }

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return ForegroundPresentationState.Unknown;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return ForegroundPresentationState.Unknown;
        }

        return Classify(
            windowRect.ToRectangle(),
            monitorInfo.Monitor.ToRectangle(),
            IsZoomed(window));
    }

    public static ForegroundPresentationState Classify(
        Rectangle window,
        Rectangle monitor,
        bool isMaximized = false,
        double threshold = 0.98)
    {
        if (threshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }
        if (window.Width <= 0 || window.Height <= 0 ||
            monitor.Width <= 0 || monitor.Height <= 0)
        {
            return ForegroundPresentationState.Unknown;
        }
        if (isMaximized)
        {
            return ForegroundPresentationState.Windowed;
        }

        var intersection = Rectangle.Intersect(window, monitor);
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return ForegroundPresentationState.Windowed;
        }

        var widthCoverage = (double)intersection.Width / monitor.Width;
        var heightCoverage = (double)intersection.Height / monitor.Height;
        return widthCoverage >= threshold && heightCoverage >= threshold
            ? ForegroundPresentationState.FullscreenLike
            : ForegroundPresentationState.Windowed;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(
            Left,
            Top,
            Right,
            Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
