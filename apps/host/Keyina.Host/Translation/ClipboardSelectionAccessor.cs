using System.ComponentModel;
using System.Globalization;
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

    void SelectPreviousText(int textElementCount) =>
        throw new NotSupportedException();

    void CollapseSelectionToEnd() =>
        throw new NotSupportedException();

    bool TryRestoreFocus(nint foregroundWindow, nint focusedWindow) => false;

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
        if (focusedWindow == nint.Zero)
        {
            focusedWindow = foregroundWindow;
        }
        if (foregroundWindow == nint.Zero ||
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

        if (!HasCapturedFocus(selectedText))
        {
            return false;
        }

        platform.InsertUnicode(translatedText);
        return true;
    }

    public bool TryReplaceFromPreview(
        SelectedTextCapture selectedText,
        string translatedText)
    {
        ArgumentNullException.ThrowIfNull(selectedText);
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedText);
        if (!HasCapturedFocus(selectedText) &&
            !platform.TryRestoreFocus(
                selectedText.ForegroundWindow,
                selectedText.FocusedWindow))
        {
            return false;
        }
        return TryReplace(selectedText, translatedText);
    }

    public async Task<bool> TryRestoreAsync(
        SelectedTextCapture selectedText,
        string expectedTranslatedText,
        string originalText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedText);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTranslatedText);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalText);
        if (!HasCapturedFocus(selectedText))
        {
            return false;
        }

        var snapshot = await RetryClipboardAsync(
                platform.CaptureClipboard,
                cancellationToken)
            .ConfigureAwait(true);
        var selectionCreated = false;
        try
        {
            var textElementCount = StringInfo.ParseCombiningCharacters(
                expectedTranslatedText).Length;
            platform.SelectPreviousText(textElementCount);
            selectionCreated = true;
            var sequenceBeforeCopy = platform.GetClipboardSequenceNumber();
            platform.SendCopyShortcut();
            for (var attempt = 0; attempt < CopyPollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (platform.GetClipboardSequenceNumber() != sequenceBeforeCopy)
                {
                    var selectedTextValue = await RetryClipboardAsync(
                            platform.ReadUnicodeText,
                            cancellationToken)
                        .ConfigureAwait(true);
                    if (!string.Equals(
                            selectedTextValue,
                            expectedTranslatedText,
                            StringComparison.Ordinal) ||
                        !HasCapturedFocus(selectedText))
                    {
                        platform.CollapseSelectionToEnd();
                        selectionCreated = false;
                        return false;
                    }

                    platform.InsertUnicode(originalText);
                    selectionCreated = false;
                    return true;
                }
                await platform.DelayAsync(RetryDelay, cancellationToken)
                    .ConfigureAwait(true);
            }

            platform.CollapseSelectionToEnd();
            selectionCreated = false;
            return false;
        }
        finally
        {
            if (selectionCreated)
            {
                try
                {
                    platform.CollapseSelectionToEnd();
                }
                catch (Exception)
                {
                    // Best effort: never leave clipboard restoration blocked.
                }
            }
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

    private bool HasCapturedFocus(SelectedTextCapture selectedText) =>
        platform.GetForegroundWindow() == selectedText.ForegroundWindow &&
        platform.GetFocusedWindow() == selectedText.FocusedWindow;

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
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyLeft = 0x25;
    private const ushort VirtualKeyRight = 0x27;
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

        SendInputs(inputs, "copy shortcut");
    }

    public void SelectPreviousText(int textElementCount)
    {
        if (textElementCount <= 0 || textElementCount > 20_000)
        {
            throw new ArgumentOutOfRangeException(nameof(textElementCount));
        }

        var inputs = new Input[(textElementCount * 2) + 2];
        var index = 0;
        inputs[index++] = Input.Key(VirtualKeyShift, keyUp: false);
        for (var current = 0; current < textElementCount; current++)
        {
            inputs[index++] = Input.Key(VirtualKeyLeft, keyUp: false);
            inputs[index++] = Input.Key(VirtualKeyLeft, keyUp: true);
        }
        inputs[index] = Input.Key(VirtualKeyShift, keyUp: true);
        SendInputs(inputs, "translation undo selection");
    }

    public void CollapseSelectionToEnd()
    {
        Input[] inputs =
        [
            Input.Key(VirtualKeyRight, keyUp: false),
            Input.Key(VirtualKeyRight, keyUp: true),
        ];
        SendInputs(inputs, "selection collapse");
    }

    public bool TryRestoreFocus(nint foregroundWindow, nint focusedWindow)
    {
        if (foregroundWindow == 0 || focusedWindow == 0)
        {
            return false;
        }

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(
            foregroundWindow,
            out _);
        var attached = targetThread != 0 &&
            targetThread != currentThread &&
            AttachThreadInput(currentThread, targetThread, attach: true);
        try
        {
            _ = SetForegroundWindow(foregroundWindow);
            _ = SetFocus(focusedWindow);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(currentThread, targetThread, attach: false);
            }
        }

        return GetForegroundWindow() == foregroundWindow &&
            GetFocusedWindow() == focusedWindow;
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

    public void InsertUnicode(string text) =>
        injector.Apply(new HookEdit(0, text, ConsumePhysicalKey: true));

    private static void SendInputs(Input[] inputs, string operation)
    {
        var sent = SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Windows did not send the complete {operation}.");
        }
    }

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

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint sourceThreadId,
        uint targetThreadId,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);
}
