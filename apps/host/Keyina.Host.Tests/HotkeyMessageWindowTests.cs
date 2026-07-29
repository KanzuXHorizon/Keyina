using Keyina.Host.Windows.Hotkeys;

namespace Keyina.Host.Tests;

internal static class HotkeyMessageWindowTests
{
    [KeyinaTest("message-only hotkey window dispatches WM_HOTKEY without becoming visible")]
    private static void MessageWindowDispatchesHotkey()
    {
        using var received = new ManualResetEventSlim();
        using var window = new HotkeyMessageWindow();
        var receivedId = 0;
        window.HotkeyReceived += (_, id) =>
        {
            receivedId = id;
            received.Set();
        };

        AssertEx.True(window.Handle != 0, "Message-only window did not create an HWND.");
        var callerThread = Environment.CurrentManagedThreadId;
        var windowThread = window.Invoke(() => Environment.CurrentManagedThreadId);
        AssertEx.True(windowThread != callerThread, "Window dispatcher did not execute on its owner thread.");
        AssertEx.True(window.PostHotkeyForTest(42), "Could not post test WM_HOTKEY.");
        AssertEx.True(received.Wait(TimeSpan.FromSeconds(2)), "WM_HOTKEY was not dispatched.");
        AssertEx.Equal(42, receivedId);

        window.Dispose();
        window.Dispose();
    }
}
