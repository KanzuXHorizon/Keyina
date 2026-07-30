using System.Runtime.InteropServices;
using Keyina.Host.Core.Feedback;
using Keyina.Host.UI.Feedback;

namespace Keyina.Host.Tests;

internal static class FeedbackOverlayTests
{
    private const int ExtendedStyleIndex = -20;
    private const long ExtendedToolWindow = 0x00000080L;
    private const long ExtendedTransparent = 0x00000020L;
    private const long ExtendedNoActivate = 0x08000000L;
    private const uint NonClientHitTest = 0x0084;
    private const int HitTransparent = -1;

    [KeyinaTest("feedback overlay is a click-through no-activate tool window")]
    private static void OverlayUsesNonInteractiveWindowStyles()
    {
        using var overlay = new NoActivateFeedbackOverlay();
        _ = overlay.Handle;

        var style = GetWindowLongPtr(overlay.Handle, ExtendedStyleIndex).ToInt64();
        AssertEx.True((style & ExtendedNoActivate) != 0, "Overlay missed WS_EX_NOACTIVATE.");
        AssertEx.True((style & ExtendedToolWindow) != 0, "Overlay missed WS_EX_TOOLWINDOW.");
        AssertEx.True((style & ExtendedTransparent) != 0, "Overlay missed WS_EX_TRANSPARENT.");
        AssertEx.False(overlay.ShowInTaskbar, "Overlay appeared in the taskbar.");
        AssertEx.Equal(FormBorderStyle.None, overlay.FormBorderStyle);

        var hit = SendMessage(overlay.Handle, NonClientHitTest, IntPtr.Zero, IntPtr.Zero).ToInt64();
        AssertEx.Equal((long)HitTransparent, hit);
    }

    [KeyinaTest("showing feedback overlay preserves the foreground window")]
    private static void OverlayDoesNotStealForegroundFocus()
    {
        using var target = new Form
        {
            Text = "Keyina feedback focus target",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(80, 80, 420, 240),
            ShowInTaskbar = false,
        };
        using var overlay = new NoActivateFeedbackOverlay();
        target.Show();
        EnsureForeground(target);
        var before = GetForegroundWindow();
        AssertEx.Equal(target.Handle, before, "Focus target did not become foreground.");

        overlay.Present(new FeedbackEvent(
            FeedbackEventKind.VietnameseEnabled,
            "Tiếng Việt đã bật",
            FeedbackTone.Success,
            FeedbackSoundCue.Enabled,
            TimeSpan.FromMilliseconds(900)));
        Application.DoEvents();
        var after = GetForegroundWindow();

        AssertEx.Equal(before, after, "Overlay changed the foreground window.");
        overlay.HideFeedback();
        target.Close();
    }

    private static void EnsureForeground(Form target)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var currentThread = GetCurrentThreadId();
            var foregroundThread = GetWindowThreadProcessId(
                GetForegroundWindow(),
                out _);
            var attached = foregroundThread != 0 &&
                foregroundThread != currentThread &&
                AttachThreadInput(currentThread, foregroundThread, attach: true);
            try
            {
                target.TopMost = true;
                _ = ShowWindow(target.Handle, showCommand: 9);
                _ = BringWindowToTop(target.Handle);
                target.Activate();
                _ = SetForegroundWindow(target.Handle);
                _ = SetActiveWindow(target.Handle);
                target.TopMost = false;
                Application.DoEvents();
            }
            finally
            {
                if (attached)
                {
                    _ = AttachThreadInput(currentThread, foregroundThread, attach: false);
                }
            }

            if (GetForegroundWindow() == target.Handle)
            {
                return;
            }
            Thread.Sleep(20);
        }

        throw new InvalidOperationException(
            "The feedback focus target could not acquire foreground focus.");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int showCommand);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint attachThread,
        uint attachToThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
