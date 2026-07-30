using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Keyina.Host.Windows.Typing;

public readonly record struct VietnameseKeyboardEvent(
    int VirtualKey,
    bool IsKeyDown,
    bool IsInjected,
    nuint ExtraInfo,
    bool Shift,
    bool Control,
    bool Alt,
    bool Windows,
    Rune Character);

public interface IVietnameseKeyboardHookNativeApi
{
    IDisposable Install(Func<VietnameseKeyboardEvent, bool> callback);
    IDisposable InstallMouseReset(Action callback);
    int GetForegroundProcessId();
    bool ShouldBypassTyping();
}

public sealed class VietnameseKeyboardHook : IDisposable
{
    private readonly IVietnameseEngine engine;
    private readonly IUnicodeInputInjector injector;
    private readonly IVietnameseKeyboardHookNativeApi nativeApi;
    private readonly bool[] suppressedKeys = new bool[256];
    private IDisposable? installation;
    private IDisposable? mouseInstallation;
    private int foregroundProcessId;
    private bool enabled;
    private bool disposed;

    public VietnameseKeyboardHook(
        IVietnameseEngine? engine = null,
        IUnicodeInputInjector? injector = null,
        IVietnameseKeyboardHookNativeApi? nativeApi = null)
    {
        this.engine = engine ?? new NativeEngineClient();
        this.injector = injector ?? new UnicodeInputInjector();
        this.nativeApi = nativeApi ?? new WindowsVietnameseKeyboardHookNativeApi();
    }

    public bool IsRunning => installation is not null;

    public void Start(bool enabledInitially)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (installation is not null)
        {
            throw new InvalidOperationException("Vietnamese keyboard hook is already installed.");
        }
        enabled = enabledInitially;
        foregroundProcessId = nativeApi.GetForegroundProcessId();
        installation = nativeApi.Install(ProcessRawEvent);
        mouseInstallation = nativeApi.InstallMouseReset(Reset);
    }

    public void SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (enabled == value)
        {
            return;
        }
        enabled = value;
        Reset();
    }

    public void Reset()
    {
        engine.Reset();
        Array.Clear(suppressedKeys);
        foregroundProcessId = nativeApi.GetForegroundProcessId();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Interlocked.Exchange(ref mouseInstallation, null)?.Dispose();
        Interlocked.Exchange(ref installation, null)?.Dispose();
        engine.Dispose();
        Array.Clear(suppressedKeys);
    }

    private bool ProcessRawEvent(VietnameseKeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.ExtraInfo == UnicodeInputInjector.InjectionMarker)
        {
            return false;
        }

        var keyIndex = keyboardEvent.VirtualKey;
        if (!keyboardEvent.IsKeyDown)
        {
            if ((uint)keyIndex < (uint)suppressedKeys.Length && suppressedKeys[keyIndex])
            {
                suppressedKeys[keyIndex] = false;
                return true;
            }
            return false;
        }

        var profiling = TypingLatencyProfiler.IsEnabled;
        var callbackStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
        try
        {
            var currentProcessId = foregroundProcessId;
            var foregroundStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
            try
            {
                currentProcessId = nativeApi.GetForegroundProcessId();
                if (currentProcessId != foregroundProcessId)
                {
                    TypingTraceBuffer.Record("focus-reset", keyIndex, currentProcessId);
                    engine.Reset();
                    Array.Clear(suppressedKeys);
                    foregroundProcessId = currentProcessId;
                }
            }
            finally
            {
                if (profiling)
                {
                    TypingLatencyProfiler.Record(
                        TypingLatencyStage.ForegroundContext,
                        foregroundStartedAt);
                }
            }

            var safetyStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
            try
            {
                if (!enabled || keyboardEvent.Control || keyboardEvent.Alt || keyboardEvent.Windows)
                {
                    TypingTraceBuffer.Record(
                        "shortcut-bypass",
                        keyIndex,
                        currentProcessId,
                        keyboardEvent.Shift,
                        keyboardEvent.Control,
                        keyboardEvent.Alt,
                        keyboardEvent.Windows);
                    engine.Reset();
                    return false;
                }
                if (nativeApi.ShouldBypassTyping())
                {
                    TypingTraceBuffer.Record("secure-bypass", keyIndex, currentProcessId);
                    engine.Reset();
                    return false;
                }

                if (keyboardEvent.VirtualKey == 0x08)
                {
                    TypingTraceBuffer.Record("backspace-pass", keyIndex, currentProcessId);
                    engine.Reset();
                    return false;
                }
            }
            finally
            {
                if (profiling)
                {
                    TypingLatencyProfiler.Record(
                        TypingLatencyStage.SafetyGuard,
                        safetyStartedAt);
                }
            }

            HookEdit edit;
            if (keyboardEvent.VirtualKey == 0x20 ||
                IsCommitBoundaryCharacter(keyboardEvent.Character))
            {
                var engineStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
                try
                {
                    edit = engine.Process(
                        NativeEngineKeyKind.CommitBoundary,
                        keyboardEvent.VirtualKey == 0x20
                            ? new Rune(' ')
                            : keyboardEvent.Character);
                }
                finally
                {
                    if (profiling)
                    {
                        TypingLatencyProfiler.Record(
                            TypingLatencyStage.EngineProcess,
                            engineStartedAt);
                    }
                }
            }
            else if (keyboardEvent.Character.Value != 0 &&
                     IsSupportedCharacter(keyboardEvent.Character))
            {
                var engineStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
                try
                {
                    edit = engine.Process(
                        NativeEngineKeyKind.Character,
                        keyboardEvent.Character,
                        keyboardEvent.Shift);
                }
                finally
                {
                    if (profiling)
                    {
                        TypingLatencyProfiler.Record(
                            TypingLatencyStage.EngineProcess,
                            engineStartedAt);
                    }
                }
            }
            else
            {
                if (IsResetBoundary(keyboardEvent.VirtualKey))
                {
                    engine.Reset();
                }
                return false;
            }

            if (!edit.ConsumePhysicalKey ||
                IsLiteralPassThrough(edit, keyboardEvent.Character))
            {
                return false;
            }

            var injectionStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
            try
            {
                try
                {
                    TypingTraceBuffer.Record(
                        "transform",
                        keyIndex,
                        currentProcessId,
                        keyboardEvent.Shift,
                        detail: $"backspaces={edit.BackspaceCount};insertUnits={edit.InsertText.Length}");
                    injector.Apply(edit);
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException)
                {
                    TypingTraceBuffer.Record(
                        "inject-failed",
                        keyIndex,
                        currentProcessId,
                        detail: exception.GetType().Name);
                    engine.Reset();
                    return false;
                }
            }
            finally
            {
                if (profiling)
                {
                    TypingLatencyProfiler.Record(
                        TypingLatencyStage.InputInjection,
                        injectionStartedAt);
                }
            }

            if ((uint)keyIndex < (uint)suppressedKeys.Length)
            {
                suppressedKeys[keyIndex] = true;
            }
            return true;
        }
        finally
        {
            if (profiling)
            {
                TypingLatencyProfiler.Record(
                    TypingLatencyStage.CallbackTotal,
                    callbackStartedAt);
            }
        }
    }

    private static bool IsLiteralPassThrough(HookEdit edit, Rune character) =>
        edit.BackspaceCount == 0 &&
        character.Value != 0 &&
        string.Equals(edit.InsertText, character.ToString(), StringComparison.Ordinal);

    private static bool IsSupportedCharacter(Rune character)
    {
        var value = character.Value;
        return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    private static bool IsCommitBoundaryCharacter(Rune character) =>
        character.Value is '.' or ',' or ';' or ':' or '!' or '?' or
            ')' or ']' or '}' or '"';

    private static bool IsResetBoundary(int virtualKey) => virtualKey is
        0x09 or 0x0D or 0x1B or 0x21 or 0x22 or 0x23 or 0x24 or
        0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E;

    private sealed class WindowsVietnameseKeyboardHookNativeApi :
        IVietnameseKeyboardHookNativeApi
    {
        private const int WhMouseLowLevel = 14;
        private const int WhKeyboardLowLevel = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const uint LlkhfLowerIntegrityInjected = 0x00000002;
        private const uint LlkhfInjected = 0x00000010;

        public IDisposable Install(Func<VietnameseKeyboardEvent, bool> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new HookLease(callback);
        }

        public IDisposable InstallMouseReset(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new MouseHookLease(callback);
        }

        public int GetForegroundProcessId()
        {
            var window = GetForegroundWindow();
            if (window == 0)
            {
                return 0;
            }
            _ = GetWindowThreadProcessId(window, out var processId);
            return checked((int)processId);
        }

        public bool ShouldBypassTyping()
        {
            var info = new GuiThreadInfo
            {
                Size = checked((uint)Marshal.SizeOf<GuiThreadInfo>()),
            };
            if (!GetGUIThreadInfo(0, ref info) || info.FocusWindow == 0)
            {
                return false;
            }

            const int globalWindowStyle = -16;
            const nint editStylePassword = 0x20;
            var style = GetWindowLongPtrW(info.FocusWindow, globalWindowStyle);
            return (style & editStylePassword) != 0;
        }

        private sealed class HookLease : IDisposable
        {
            private readonly Func<VietnameseKeyboardEvent, bool> callback;
            private readonly LowLevelKeyboardProcedure procedure;
            private readonly bool[] pressedKeys = new bool[256];
            private nint hookHandle;

            public HookLease(Func<VietnameseKeyboardEvent, bool> callback)
            {
                this.callback = callback;
                procedure = HookCallback;
                hookHandle = SetWindowsHookExW(
                    WhKeyboardLowLevel,
                    procedure,
                    0,
                    0);
                if (hookHandle == 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows rejected the Keyina typing hook.");
                }
            }

            public void Dispose()
            {
                var handle = Interlocked.Exchange(ref hookHandle, 0);
                if (handle != 0 && !UnhookWindowsHookEx(handle))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows could not remove the Keyina typing hook.");
                }
            }

            private nint HookCallback(int code, nint message, nint data)
            {
                try
                {
                    if (code >= 0 && IsKeyboardMessage(message))
                    {
                        var nativeEvent = Marshal.PtrToStructure<LowLevelKeyboardInput>(data);
                        var isKeyDown = message == WmKeyDown || message == WmSysKeyDown;
                        var virtualKey = checked((int)nativeEvent.VirtualKey);
                        if ((uint)virtualKey < (uint)pressedKeys.Length)
                        {
                            pressedKeys[virtualKey] = isKeyDown;
                        }

                        var shift = IsPressed(0x10) || IsPressed(0xA0) || IsPressed(0xA1);
                        var control = IsPressed(0x11) || IsPressed(0xA2) || IsPressed(0xA3);
                        var alt = IsPressed(0x12) || IsPressed(0xA4) || IsPressed(0xA5);
                        var windows = IsPressed(0x5B) || IsPressed(0x5C);
                        var capsLock = (GetKeyState(0x14) & 0x0001) != 0;
                        var character = TranslateCharacter(
                            nativeEvent.VirtualKey,
                            shift,
                            capsLock);
                        var isInjected = (nativeEvent.Flags &
                            (LlkhfInjected | LlkhfLowerIntegrityInjected)) != 0;
                        var handled = callback(new VietnameseKeyboardEvent(
                            virtualKey,
                            isKeyDown,
                            isInjected,
                            nativeEvent.ExtraInfo,
                            shift,
                            control,
                            alt,
                            windows,
                            character));
                        if (handled)
                        {
                            return 1;
                        }
                    }
                }
                catch (Exception)
                {
                    // A global hook must always fail open. Never let managed
                    // exceptions cross the native callback boundary.
                }
                return CallNextHookEx(0, code, message, data);
            }

            private static Rune TranslateCharacter(
                uint virtualKey,
                bool shift,
                bool capsLock)
            {
                if (virtualKey is < 'A' or > 'Z')
                {
                    return default;
                }
                var upper = shift ^ capsLock;
                var character = upper
                    ? checked((char)virtualKey)
                    : checked((char)(virtualKey + ('a' - 'A')));
                return new Rune(character);
            }

            private bool IsPressed(int virtualKey) =>
                (uint)virtualKey < (uint)pressedKeys.Length && pressedKeys[virtualKey];

            private static bool IsKeyboardMessage(nint message)
            {
                var value = unchecked((int)message);
                return value is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp;
            }
        }

        private sealed class MouseHookLease : IDisposable
        {
            private const int WmLeftButtonDown = 0x0201;
            private const int WmRightButtonDown = 0x0204;
            private const int WmMiddleButtonDown = 0x0207;
            private const int WmMouseWheel = 0x020A;
            private const int WmXButtonDown = 0x020B;

            private readonly Action callback;
            private readonly LowLevelMouseProcedure procedure;
            private nint hookHandle;

            public MouseHookLease(Action callback)
            {
                this.callback = callback;
                procedure = HookCallback;
                hookHandle = SetWindowsMouseHookExW(
                    WhMouseLowLevel,
                    procedure,
                    0,
                    0);
                if (hookHandle == 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows rejected the Keyina mouse reset hook.");
                }
            }

            public void Dispose()
            {
                var handle = Interlocked.Exchange(ref hookHandle, 0);
                if (handle != 0 && !UnhookWindowsHookEx(handle))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows could not remove the Keyina mouse reset hook.");
                }
            }

            private nint HookCallback(int code, nint message, nint data)
            {
                static bool IsResetMessage(int value) => value is
                    WmLeftButtonDown or WmRightButtonDown or
                    WmMiddleButtonDown or WmMouseWheel or WmXButtonDown;

                try
                {
                    if (code >= 0 && IsResetMessage(unchecked((int)message)))
                    {
                        callback();
                    }
                }
                catch (Exception)
                {
                    // Mouse reset is defensive; failures must never block input.
                }
                return CallNextHookEx(0, code, message, data);
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
        private readonly struct LowLevelKeyboardInput
        {
            public readonly uint VirtualKey;
            public readonly uint ScanCode;
            public readonly uint Flags;
            public readonly uint Time;
            public readonly nuint ExtraInfo;
        }

        private delegate nint LowLevelKeyboardProcedure(int code, nint message, nint data);
        private delegate nint LowLevelMouseProcedure(int code, nint message, nint data);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SetWindowsHookExW(
            int hookId,
            LowLevelKeyboardProcedure procedure,
            nint module,
            uint threadId);

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static extern nint SetWindowsMouseHookExW(
            int hookId,
            LowLevelMouseProcedure procedure,
            nint module,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(nint hook);

        [DllImport("user32.dll")]
        private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandleW(string? moduleName);

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtrW(nint window, int index);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

    }
}
