using System.Runtime.InteropServices;

namespace Keyina.Host.Windows.Typing;

public static class WindowsTypingContextProbe
{
    private const int GlobalWindowStyle = -16;
    private const nint EditStylePassword = 0x20;
    private static readonly uint GuiThreadInfoSize =
        checked((uint)Marshal.SizeOf<GuiThreadInfo>());

    public static VietnameseTypingContext Capture()
    {
        var info = new GuiThreadInfo { Size = GuiThreadInfoSize };
        if (!GetGUIThreadInfo(0, ref info) || info.FocusWindow == 0)
        {
            return new VietnameseTypingContext(0, 0, ShouldBypassTyping: true);
        }

        var activeWindow = info.ActiveWindow != 0
            ? info.ActiveWindow
            : info.FocusWindow;
        var threadId = GetWindowThreadProcessId(activeWindow, out var processId);
        if (threadId == 0)
        {
            return new VietnameseTypingContext(
                0,
                info.FocusWindow,
                ShouldBypassTyping: true);
        }

        Marshal.SetLastPInvokeError(0);
        var style = GetWindowLongPtrW(info.FocusWindow, GlobalWindowStyle);
        var styleReadFailed = style == 0 && Marshal.GetLastPInvokeError() != 0;
        return new VietnameseTypingContext(
            checked((int)processId),
            info.FocusWindow,
            styleReadFailed || (style & EditStylePassword) != 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint ActiveWindow;
        public nint FocusWindow;
        public nint CaptureWindow;
        public nint MenuOwnerWindow;
        public nint MoveSizeWindow;
        public nint CaretWindow;
        public System.Drawing.Rectangle CaretRectangle;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(
        uint threadId,
        ref GuiThreadInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);
}
