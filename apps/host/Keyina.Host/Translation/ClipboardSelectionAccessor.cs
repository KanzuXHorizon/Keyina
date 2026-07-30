using System.ComponentModel;
using System.Runtime.InteropServices;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Translation;

public interface IClipboardSelectionPlatform
{
    nint GetForegroundWindow();

    nint GetFocusedWindow();

    uint GetClipboardSequenceNumber();

    object? CaptureClipboard();

    string? ReadUnicodeText();

    void RestoreClipboard(object? snapshot);

    void SendCopyShortcut();

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    void InsertUnicode(string text);
}

public sealed class ClipboardSelectionAccessor : ISelectedTextAccessor
{
    private const int ClipboardRetryAttempts = 6;
    private const int CopyPollAttempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(15);

    private readonly IClipboardSelectionPlatform platform;

    public ClipboardSelectionAccessor(IClipboardSelectionPlatform? platform = null)
    {
        this.platform = platform ?? new WindowsClipboardSelectionPlatform();
    }

    public async Task<SelectedTextCapture?> CaptureAsync(
        CancellationToken cancellationToken)
    {
        var foregroundWindow = platform.GetForegroundWindow();
        var focusedWindow = platform.GetFocusedWindow();
        if (foregroundWindow == nint.Zero ||
            focusedWindow == nint.Zero ||
            platform.GetForegroundWindow() != foregroundWindow)
        {
            return null;
        }

        var snapshot = await RetryClipboardAsync(
                platform.CaptureClipboard,
                cancellationToken)
            .ConfigureAwait(true);
        try
        {
            var sequenceBeforeCopy = platform.GetClipboardSequenceNumber();
            platform.SendCopyShortcut();

            for (var attempt = 0; attempt < CopyPollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (platform.GetClipboardSequenceNumber() != sequenceBeforeCopy)
                {
                    var text = await RetryClipboardAsync(
                            platform.ReadUnicodeText,
                            cancellationToken)
                        .ConfigureAwait(true);
                    return string.IsNullOrWhiteSpace(text)
                        ? null
                        : new SelectedTextCapture(
                            text,
                            foregroundWindow,
                            focusedWindow);
                }

                await platform.DelayAsync(RetryDelay, cancellationToken)
                    .ConfigureAwait(true);
            }

            return null;
        }
        finally
        {
            await RetryClipboardAsync(
                    () =>
                    {
                        platform.RestoreClipboard(snapshot);
                        return true;
                    },
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
    }

    public bool TryReplace(SelectedTextCapture selectedText, string translatedText)
    {
        ArgumentNullException.ThrowIfNull(selectedText);
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedText);

        if (platform.GetForegroundWindow() != selectedText.ForegroundWindow ||
            platform.GetFocusedWindow() != selectedText.FocusedWindow)
        {
            return false;
        }

        platform.InsertUnicode(translatedText);
        return true;
    }

    private async Task<T> RetryClipboardAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return action();
            }
            catch (ExternalException) when (attempt + 1 < ClipboardRetryAttempts)
            {
                await platform.DelayAsync(RetryDelay, cancellationToken)
                    .ConfigureAwait(true);
            }
        }
    }
}

public sealed class WindowsClipboardSelectionPlatform : IClipboardSelectionPlatform
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyC = 0x43;

    private readonly UnicodeInputInjector injector;

    public WindowsClipboardSelectionPlatform(UnicodeInputInjector? injector = null)
    {
        this.injector = injector ?? new UnicodeInputInjector();
    }

    public nint GetForegroundWindow() => NativeGetForegroundWindow();

    public nint GetFocusedWindow()
    {
        var info = new GuiThreadInfo
        {
            Size = checked((uint)Marshal.SizeOf<GuiThreadInfo>()),
        };
        return GetGUIThreadInfo(0, ref info)
            ? info.FocusWindow
            : 0;
    }

    public uint GetClipboardSequenceNumber() => NativeGetClipboardSequenceNumber();

    public object? CaptureClipboard()
    {
        EnsureStaThread();
        return Clipboard.GetDataObject();
    }

    public string? ReadUnicodeText()
    {
        EnsureStaThread();
        return Clipboard.ContainsText(TextDataFormat.UnicodeText)
            ? Clipboard.GetText(TextDataFormat.UnicodeText)
            : null;
    }

    public void RestoreClipboard(object? snapshot)
    {
        EnsureStaThread();
        if (snapshot is null)
        {
            Clipboard.Clear();
            return;
        }
        Clipboard.SetDataObject(snapshot, copy: true);
    }

    public void SendCopyShortcut()
    {
        Input[] inputs =
        [
            Input.Key(VirtualKeyControl, keyUp: false),
            Input.Key(VirtualKeyC, keyUp: false),
            Input.Key(VirtualKeyC, keyUp: true),
            Input.Key(VirtualKeyControl, keyUp: true),
        ];

        var sent = SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows did not send the complete copy shortcut.");
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

    public void InsertUnicode(string text) =>
        injector.Apply(new HookEdit(0, text, ConsumePhysicalKey: true));

    private static void EnsureStaThread()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(
                "Clipboard selection capture must run on the Windows UI thread.");
        }
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
        public Rectangle CaretRectangle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;

        public static Input Key(ushort virtualKey, bool keyUp) => new()
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                    ExtraInfo = UnicodeInputInjector.InjectionMarker,
                },
            },
        };
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
    private static extern uint NativeGetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(
        uint threadId,
        ref GuiThreadInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);
}
