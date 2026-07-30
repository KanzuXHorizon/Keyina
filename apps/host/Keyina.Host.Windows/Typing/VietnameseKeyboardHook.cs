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

public readonly record struct VietnameseTypingContext(
    int ForegroundProcessId,
    nint FocusWindow,
    bool ShouldBypassTyping);

public interface IVietnameseKeyboardHookNativeApi
{
    IDisposable Install(Func<VietnameseKeyboardEvent, bool> keyboardCallback);
    IDisposable InstallPointerReset(Action pointerResetCallback);
    VietnameseTypingContext GetTypingContext();
}

public sealed class VietnameseKeyboardHook : IDisposable
{
    private readonly IVietnameseEngine engine;
    private readonly IUnicodeInputInjector injector;
    private readonly IVietnameseKeyboardHookNativeApi nativeApi;
    private readonly bool[] suppressedKeys = new bool[256];
    private IDisposable? installation;
    private IDisposable? pointerInstallation;
    private long processedPhysicalEventCount;
    private int resetRequested;
    private VietnameseTypingContext typingContext;
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

    public long ProcessedPhysicalEventCount =>
        Interlocked.Read(ref processedPhysicalEventCount);

    public void Start(bool enabledInitially)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (installation is not null)
        {
            throw new InvalidOperationException("Vietnamese keyboard hook is already installed.");
        }
        enabled = enabledInitially;
        typingContext = nativeApi.GetTypingContext();
        var keyboard = nativeApi.Install(ProcessRawEvent);
        try
        {
            var pointer = nativeApi.InstallPointerReset(RequestReset);
            installation = keyboard;
            pointerInstallation = pointer;
        }
        catch
        {
            keyboard.Dispose();
            throw;
        }
    }

    public void SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (enabled == value)
        {
            return;
        }
        enabled = value;
        RequestReset();
    }

    public void Reset() => RequestReset();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        List<Exception>? failures = null;
        DisposeResource(
            Interlocked.Exchange(ref pointerInstallation, null),
            ref failures);
        DisposeResource(
            Interlocked.Exchange(ref installation, null),
            ref failures);
        DisposeResource(engine, ref failures);
        Array.Clear(suppressedKeys);
        if (failures is not null)
        {
            throw new AggregateException(
                "Keyina could not release every typing resource.",
                failures);
        }
    }

    private bool ProcessRawEvent(VietnameseKeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.ExtraInfo == UnicodeInputInjector.InjectionMarker)
        {
            return false;
        }

        var resetPending = Interlocked.Exchange(ref resetRequested, 0) != 0;
        var keyIndex = keyboardEvent.VirtualKey;
        if (!keyboardEvent.IsKeyDown)
        {
            try
            {
                if (resetPending)
                {
                    ResetEngineState();
                }
                if ((uint)keyIndex < (uint)suppressedKeys.Length && suppressedKeys[keyIndex])
                {
                    suppressedKeys[keyIndex] = false;
                    return true;
                }
                return false;
            }
            finally
            {
                Interlocked.Increment(ref processedPhysicalEventCount);
            }
        }

        var profiling = TypingLatencyProfiler.IsEnabled;
        var callbackStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
        try
        {
            var currentContext = typingContext;
            var foregroundStartedAt = profiling ? Stopwatch.GetTimestamp() : 0;
            try
            {
                currentContext = nativeApi.GetTypingContext();
                if (resetPending || HasContextChanged(typingContext, currentContext))
                {
                    TypingTraceBuffer.Record(
                        resetPending ? "pointer-reset" : "focus-reset",
                        keyIndex,
                        currentContext.ForegroundProcessId);
                    ResetEngineState();
                }
                typingContext = currentContext;
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
                        currentContext.ForegroundProcessId,
                        keyboardEvent.Shift,
                        keyboardEvent.Control,
                        keyboardEvent.Alt,
                        keyboardEvent.Windows);
                    engine.Reset();
                    return false;
                }
                if (currentContext.ShouldBypassTyping)
                {
                    TypingTraceBuffer.Record(
                        "secure-bypass",
                        keyIndex,
                        currentContext.ForegroundProcessId);
                    engine.Reset();
                    return false;
                }

                if (keyboardEvent.VirtualKey == 0x08)
                {
                    TypingTraceBuffer.Record(
                        "backspace-pass",
                        keyIndex,
                        currentContext.ForegroundProcessId);
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
                    if (TypingTraceBuffer.IsEnabled)
                    {
                        TypingTraceBuffer.Record(
                            "transform",
                            keyIndex,
                            currentContext.ForegroundProcessId,
                            keyboardEvent.Shift,
                            detail: $"backspaces={edit.BackspaceCount};insertUnits={edit.InsertText.Length}");
                    }
                    injector.Apply(edit);
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException)
                {
                    if (TypingTraceBuffer.IsEnabled)
                    {
                        TypingTraceBuffer.Record(
                            "inject-failed",
                            keyIndex,
                            currentContext.ForegroundProcessId,
                            detail: exception.GetType().Name);
                    }
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
            Interlocked.Increment(ref processedPhysicalEventCount);
        }
    }

    private static void DisposeResource(
        IDisposable? resource,
        ref List<Exception>? failures)
    {
        if (resource is null)
        {
            return;
        }
        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }

    private void RequestReset() =>
        Interlocked.Exchange(ref resetRequested, 1);

    private void ResetEngineState()
    {
        engine.Reset();
        Array.Clear(suppressedKeys);
    }

    private static bool HasContextChanged(
        VietnameseTypingContext previous,
        VietnameseTypingContext current) => previous != current;

    private static bool IsLiteralPassThrough(HookEdit edit, Rune character) =>
        edit.BackspaceCount == 0 &&
        character.Value is > 0 and <= char.MaxValue &&
        edit.InsertText.Length == 1 &&
        edit.InsertText[0] == (char)character.Value;

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
        private const int WhKeyboardLowLevel = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int GlobalWindowStyle = -16;
        private const nint EditStylePassword = 0x20;
        private const uint LlkhfLowerIntegrityInjected = 0x00000002;
        private const uint LlkhfInjected = 0x00000010;
        private static readonly uint GuiThreadInfoSize =
            checked((uint)Marshal.SizeOf<GuiThreadInfo>());

        private nint cachedActiveWindow;
        private int cachedProcessId;
        private nint cachedFocusWindow;
        private bool cachedShouldBypass = true;

        public IDisposable Install(Func<VietnameseKeyboardEvent, bool> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new HookLease(callback);
        }

        public IDisposable InstallPointerReset(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new RawMouseResetLease(callback);
        }

        public VietnameseTypingContext GetTypingContext()
        {
            var info = new GuiThreadInfo { Size = GuiThreadInfoSize };
            if (!GetGUIThreadInfo(0, ref info) || info.FocusWindow == 0)
            {
                cachedActiveWindow = 0;
                cachedProcessId = 0;
                cachedFocusWindow = 0;
                cachedShouldBypass = true;
                return new VietnameseTypingContext(0, 0, ShouldBypassTyping: true);
            }

            var activeWindow = info.ActiveWindow != 0
                ? info.ActiveWindow
                : info.FocusWindow;
            if (activeWindow != cachedActiveWindow)
            {
                _ = GetWindowThreadProcessId(activeWindow, out var processId);
                cachedActiveWindow = activeWindow;
                cachedProcessId = checked((int)processId);
            }

            var style = GetWindowLongPtrW(info.FocusWindow, GlobalWindowStyle);
            cachedFocusWindow = info.FocusWindow;
            cachedShouldBypass = (style & EditStylePassword) != 0;

            return new VietnameseTypingContext(
                cachedProcessId,
                cachedFocusWindow,
                cachedShouldBypass);
        }

        private sealed class HookLease : IDisposable
        {
            private const uint WmQuit = 0x0012;
            private const uint PeekMessageNoRemove = 0x0000;
            private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
            private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

            private readonly Func<VietnameseKeyboardEvent, bool> callback;
            private readonly LowLevelKeyboardProcedure procedure;
            private readonly bool[] pressedKeys = new bool[256];
            private readonly ManualResetEventSlim ready = new(initialState: false);
            private readonly Thread thread;
            private Exception? startupFailure;
            private Exception? shutdownFailure;
            private uint threadId;
            private nint hookHandle;
            private int disposed;

            public HookLease(Func<VietnameseKeyboardEvent, bool> callback)
            {
                this.callback = callback;
                procedure = HookCallback;
                thread = new Thread(Run, maxStackSize: 256 * 1024)
                {
                    IsBackground = true,
                    Name = "Keyina typing hook",
                };
                thread.Start();

                if (!ready.Wait(StartupTimeout))
                {
                    RequestThreadExit();
                    _ = thread.Join(ShutdownTimeout);
                    ready.Dispose();
                    throw new TimeoutException(
                        "The Keyina typing hook thread did not become ready in time.");
                }

                if (startupFailure is not null)
                {
                    _ = thread.Join(ShutdownTimeout);
                    ready.Dispose();
                    throw new InvalidOperationException(
                        "The Keyina typing hook thread could not start.",
                        startupFailure);
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                RequestThreadExit();
                if (!thread.Join(ShutdownTimeout))
                {
                    ready.Dispose();
                    throw new TimeoutException(
                        "The Keyina typing hook thread did not stop in time.");
                }

                ready.Dispose();
                if (shutdownFailure is not null)
                {
                    throw new InvalidOperationException(
                        "The Keyina typing hook thread stopped with an error.",
                        shutdownFailure);
                }
            }

            private void Run()
            {
                threadId = GetCurrentThreadId();
                try
                {
                    _ = PeekMessageW(
                        out _,
                        Window: 0,
                        MinimumMessage: 0,
                        MaximumMessage: 0,
                        PeekMessageNoRemove);
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

                    ready.Set();
                    while (true)
                    {
                        var result = GetMessageW(out var message, 0, 0, 0);
                        if (result == 0)
                        {
                            break;
                        }
                        if (result < 0)
                        {
                            throw new Win32Exception(
                                Marshal.GetLastWin32Error(),
                                "The Keyina typing hook message loop failed.");
                        }

                        _ = TranslateMessage(ref message);
                        _ = DispatchMessageW(ref message);
                    }
                }
                catch (Exception exception)
                {
                    if (!ready.IsSet)
                    {
                        startupFailure = exception;
                        ready.Set();
                    }
                    else
                    {
                        shutdownFailure = exception;
                    }
                }
                finally
                {
                    var handle = Interlocked.Exchange(ref hookHandle, 0);
                    if (handle != 0 && !UnhookWindowsHookEx(handle))
                    {
                        shutdownFailure ??= new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Windows could not remove the Keyina typing hook.");
                    }
                    if (!ready.IsSet)
                    {
                        ready.Set();
                    }
                }
            }

            private void RequestThreadExit()
            {
                var id = Volatile.Read(ref threadId);
                if (id != 0 && thread.IsAlive)
                {
                    _ = PostThreadMessageW(id, WmQuit, 0, 0);
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

        private sealed class RawMouseResetLease : IDisposable
        {
            private const uint WmInput = 0x00FF;
            private const uint WmQuit = 0x0012;
            private const uint RidInput = 0x10000003;
            private const uint RimTypeMouse = 0;
            private const uint RidevRemove = 0x00000001;
            private const uint RidevInputSink = 0x00000100;
            private const ushort MouseLeftDown = 0x0001;
            private const ushort MouseRightDown = 0x0004;
            private const ushort MouseMiddleDown = 0x0010;
            private const ushort MouseButton4Down = 0x0040;
            private const ushort MouseButton5Down = 0x0100;
            private const ushort MouseWheel = 0x0400;
            private const ushort MouseHorizontalWheel = 0x0800;
            private static readonly nint MessageOnlyWindow = new(-3);
            private static readonly uint RawInputDeviceSize =
                checked((uint)Marshal.SizeOf<RawInputDevice>());
            private static readonly uint RawInputHeaderSize =
                checked((uint)Marshal.SizeOf<RawInputHeader>());
            private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
            private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

            private readonly Action callback;
            private readonly ManualResetEventSlim ready = new(initialState: false);
            private readonly Thread thread;
            private Exception? startupFailure;
            private Exception? shutdownFailure;
            private uint threadId;
            private nint window;
            private int disposed;

            public RawMouseResetLease(Action callback)
            {
                this.callback = callback;
                thread = new Thread(Run, maxStackSize: 128 * 1024)
                {
                    IsBackground = true,
                    Name = "Keyina pointer observer",
                };
                thread.Start();

                if (!ready.Wait(StartupTimeout))
                {
                    RequestThreadExit();
                    _ = thread.Join(ShutdownTimeout);
                    ready.Dispose();
                    throw new TimeoutException(
                        "The Keyina pointer observer did not become ready in time.");
                }

                if (startupFailure is not null)
                {
                    _ = thread.Join(ShutdownTimeout);
                    ready.Dispose();
                    throw new InvalidOperationException(
                        "The Keyina pointer observer could not start.",
                        startupFailure);
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                RequestThreadExit();
                if (!thread.Join(ShutdownTimeout))
                {
                    ready.Dispose();
                    throw new TimeoutException(
                        "The Keyina pointer observer did not stop in time.");
                }

                ready.Dispose();
                if (shutdownFailure is not null)
                {
                    throw new InvalidOperationException(
                        "The Keyina pointer observer stopped with an error.",
                        shutdownFailure);
                }
            }

            private void Run()
            {
                threadId = GetCurrentThreadId();
                var registered = false;
                try
                {
                    window = CreateWindowExW(
                        ExtendedStyle: 0,
                        ClassName: "STATIC",
                        WindowName: "Keyina pointer observer",
                        Style: 0,
                        X: 0,
                        Y: 0,
                        Width: 0,
                        Height: 0,
                        Parent: MessageOnlyWindow,
                        Menu: 0,
                        Instance: 0,
                        Parameter: 0);
                    if (window == 0)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Windows could not create the Keyina raw-input window.");
                    }

                    var device = new RawInputDevice
                    {
                        UsagePage = 0x01,
                        Usage = 0x02,
                        Flags = RidevInputSink,
                        TargetWindow = window,
                    };
                    if (!RegisterRawInputDevices(ref device, 1, RawInputDeviceSize))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Windows rejected Keyina raw mouse input registration.");
                    }
                    registered = true;
                    ready.Set();

                    while (true)
                    {
                        var result = GetMessageW(out var message, 0, 0, 0);
                        if (result == 0)
                        {
                            break;
                        }
                        if (result < 0)
                        {
                            throw new Win32Exception(
                                Marshal.GetLastWin32Error(),
                                "The Keyina pointer observer message loop failed.");
                        }

                        if (message.Message == WmInput && message.Window == window)
                        {
                            ProcessRawInput(message.LParam);
                            _ = DefWindowProcW(
                                message.Window,
                                message.Message,
                                message.WParam,
                                message.LParam);
                            continue;
                        }

                        _ = TranslateMessage(ref message);
                        _ = DispatchMessageW(ref message);
                    }
                }
                catch (Exception exception)
                {
                    if (!ready.IsSet)
                    {
                        startupFailure = exception;
                        ready.Set();
                    }
                    else
                    {
                        shutdownFailure = exception;
                    }
                }
                finally
                {
                    if (registered)
                    {
                        var device = new RawInputDevice
                        {
                            UsagePage = 0x01,
                            Usage = 0x02,
                            Flags = RidevRemove,
                            TargetWindow = 0,
                        };
                        if (!RegisterRawInputDevices(ref device, 1, RawInputDeviceSize))
                        {
                            shutdownFailure ??= new Win32Exception(
                                Marshal.GetLastWin32Error(),
                                "Windows could not unregister Keyina raw mouse input.");
                        }
                    }

                    var handle = Interlocked.Exchange(ref window, 0);
                    if (handle != 0 && !DestroyWindow(handle))
                    {
                        shutdownFailure ??= new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Windows could not destroy the Keyina raw-input window.");
                    }
                    if (!ready.IsSet)
                    {
                        ready.Set();
                    }
                }
            }

            private unsafe void ProcessRawInput(nint rawInputHandle)
            {
                var resetRequested = ReadCurrentPacket(rawInputHandle) |
                    DrainBufferedPackets();
                if (!resetRequested)
                {
                    return;
                }

                try
                {
                    callback();
                }
                catch (Exception)
                {
                    // Pointer observation is defensive and must not disrupt
                    // the asynchronous raw-input message loop.
                }
            }

            private static unsafe bool ReadCurrentPacket(nint rawInputHandle)
            {
                Span<byte> buffer = stackalloc byte[64];
                var size = checked((uint)buffer.Length);
                fixed (byte* pointer = buffer)
                {
                    var copied = GetRawInputData(
                        rawInputHandle,
                        RidInput,
                        (nint)pointer,
                        ref size,
                        RawInputHeaderSize);
                    if (copied == uint.MaxValue ||
                        copied < RawInputHeaderSize ||
                        size > buffer.Length)
                    {
                        return false;
                    }

                    return IsResetPacket((RawInput*)pointer);
                }
            }

            private static unsafe bool DrainBufferedPackets()
            {
                Span<byte> storage = stackalloc byte[4_096 + 8];
                fixed (byte* storagePointer = storage)
                {
                    var address = (nuint)storagePointer;
                    var alignedAddress = (address + 7u) & ~7u;
                    var pointer = (byte*)alignedAddress;
                    var capacity = checked((uint)(storage.Length -
                        checked((int)(alignedAddress - address))));
                    var resetRequested = false;

                    while (true)
                    {
                        var size = capacity;
                        var count = GetRawInputBuffer(
                            (nint)pointer,
                            ref size,
                            RawInputHeaderSize);
                        if (count == uint.MaxValue || count == 0)
                        {
                            return resetRequested;
                        }

                        var current = pointer;
                        var end = pointer + capacity;
                        for (var index = 0u; index < count; index++)
                        {
                            var input = (RawInput*)current;
                            if (input->Header.Size < RawInputHeaderSize ||
                                current + input->Header.Size > end)
                            {
                                return resetRequested;
                            }

                            resetRequested |= IsResetPacket(input);
                            var alignedSize = checked((nuint)(
                                (input->Header.Size + 7u) & ~7u));
                            current += alignedSize;
                        }
                    }
                }
            }

            private static unsafe bool IsResetPacket(RawInput* input) =>
                input->Header.Type == RimTypeMouse &&
                IsResetButtonFlags(input->Mouse.ButtonFlags);

            private static bool IsResetButtonFlags(ushort flags) =>
                (flags & (MouseLeftDown |
                          MouseRightDown |
                          MouseMiddleDown |
                          MouseButton4Down |
                          MouseButton5Down |
                          MouseWheel |
                          MouseHorizontalWheel)) != 0;

            private void RequestThreadExit()
            {
                var id = Volatile.Read(ref threadId);
                if (id != 0 && thread.IsAlive)
                {
                    _ = PostThreadMessageW(id, WmQuit, 0, 0);
                }
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

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public nint TargetWindow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public nint Device;
            public nuint WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawMouse
        {
            public ushort Flags;
            public ushort Padding;
            public ushort ButtonFlags;
            public ushort ButtonData;
            public uint RawButtons;
            public int LastX;
            public int LastY;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInput
        {
            public RawInputHeader Header;
            public RawMouse Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public nint Window;
            public uint Message;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public Point Point;
            public uint Private;
        }

        private delegate nint LowLevelKeyboardProcedure(int code, nint message, nint data);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SetWindowsHookExW(
            int hookId,
            LowLevelKeyboardProcedure procedure,
            nint module,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(nint hook);

        [DllImport("user32.dll")]
        private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices(
            ref RawInputDevice devices,
            uint deviceCount,
            uint deviceSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            nint rawInput,
            uint command,
            nint data,
            ref uint size,
            uint headerSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputBuffer(
            nint data,
            ref uint size,
            uint headerSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(
            uint ExtendedStyle,
            string ClassName,
            string? WindowName,
            uint Style,
            int X,
            int Y,
            int Width,
            int Height,
            nint Parent,
            nint Menu,
            nint Instance,
            nint Parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessageW(
            out NativeMessage message,
            nint window,
            uint minimumMessage,
            uint maximumMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessageW(
            out NativeMessage message,
            nint Window,
            uint MinimumMessage,
            uint MaximumMessage,
            uint RemoveMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessageW(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern nint DefWindowProcW(
            nint window,
            uint message,
            nuint wParam,
            nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessageW(
            uint threadId,
            uint message,
            nuint wParam,
            nint lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

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
