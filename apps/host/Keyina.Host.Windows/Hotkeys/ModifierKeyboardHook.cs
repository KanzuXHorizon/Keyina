using System.ComponentModel;
using System.Runtime.InteropServices;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Windows.Hotkeys;

public readonly record struct RawKeyboardEvent(
    VirtualKey Key,
    bool IsKeyDown,
    bool IsInjected);

public interface IKeyboardHookNativeApi
{
    IDisposable Install(Func<RawKeyboardEvent, bool> callback);
}

public sealed class SharedTypingKeyboardHookNativeApi(
    VietnameseKeyboardHook typingHook) : IKeyboardHookNativeApi
{
    public IDisposable Install(Func<RawKeyboardEvent, bool> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return typingHook.SubscribePhysicalEvents(keyboardEvent =>
        {
            _ = callback(new RawKeyboardEvent(
                (VirtualKey)keyboardEvent.VirtualKey,
                keyboardEvent.IsKeyDown,
                keyboardEvent.IsInjected));
        });
    }
}

public sealed class ModifierKeyboardHook : IDisposable
{
    private readonly IKeyboardHookNativeApi nativeApi;
    private HookGestureState gestureState = HookGestureState.CreateDefault();
    private IDisposable? installation;
    private bool disposed;

    public ModifierKeyboardHook(IKeyboardHookNativeApi? nativeApi = null)
    {
        this.nativeApi = nativeApi ?? new WindowsKeyboardHookNativeApi();
    }

    public event EventHandler<HotkeyCommand>? CommandReceived;

    public void Configure(
        HotkeyPreference modifierGesture,
        HotkeyPreference holdGesture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(modifierGesture);
        ArgumentNullException.ThrowIfNull(holdGesture);
        if (modifierGesture.GestureKind != HotkeyGestureKind.ModifierGesture ||
            modifierGesture.Chord.Key != VirtualKey.None)
        {
            throw new ArgumentException(
                "The input toggle requires a modifier-only gesture.",
                nameof(modifierGesture));
        }
        if (holdGesture.GestureKind != HotkeyGestureKind.Hold ||
            !holdGesture.Chord.HasPrimaryKey ||
            holdGesture.Chord.Modifiers == HotkeyModifiers.None)
        {
            throw new ArgumentException(
                "Push to talk requires a modified hold gesture.",
                nameof(holdGesture));
        }

        Interlocked.Exchange(
            ref gestureState,
            new HookGestureState(modifierGesture.Chord, holdGesture.Chord));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (installation is not null)
        {
            throw new InvalidOperationException("Modifier keyboard hook is already installed.");
        }

        installation = nativeApi.Install(ProcessRawEvent);
    }

    public void Reset() => Volatile.Read(ref gestureState).Reset();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Interlocked.Exchange(ref installation, null)?.Dispose();
        Reset();
    }

    private bool ProcessRawEvent(RawKeyboardEvent rawEvent)
    {
        if (rawEvent.IsInjected)
        {
            return false;
        }

        var state = Volatile.Read(ref gestureState);
        var keyIndex = (int)rawEvent.Key;
        var wasPressed = (uint)keyIndex < (uint)state.PressedKeys.Length &&
            state.PressedKeys[keyIndex];
        var isRepeat = rawEvent.IsKeyDown && wasPressed;

        if (rawEvent.IsKeyDown && !isRepeat &&
            rawEvent.Key == state.HoldChord.Key &&
            state.GetPressedModifiers() == state.HoldChord.Modifiers)
        {
            state.PushToTalkActive = true;
        }

        if ((uint)keyIndex < (uint)state.PressedKeys.Length)
        {
            state.PressedKeys[keyIndex] = rawEvent.IsKeyDown;
        }

        var releasePushToTalk = !rawEvent.IsKeyDown &&
            state.PushToTalkActive &&
            (rawEvent.Key == state.HoldChord.Key ||
             !state.HasRequiredHoldModifiers());
        if (releasePushToTalk)
        {
            state.PushToTalkActive = false;
            CommandReceived?.Invoke(this, HotkeyCommand.PushToTalkReleased);
        }

        var transition = new KeyboardTransition(
            rawEvent.Key,
            rawEvent.IsKeyDown
                ? KeyboardTransitionKind.KeyDown
                : KeyboardTransitionKind.KeyUp,
            isRepeat);
        var command = state.ToggleStateMachine.Process(transition);
        if (command != HotkeyCommand.None)
        {
            CommandReceived?.Invoke(this, command);
        }

        return false;
    }

    private sealed class HookGestureState
    {
        public HookGestureState(
            HotkeyChord modifierGesture,
            HotkeyChord holdChord)
        {
            ToggleStateMachine = new ModifierToggleStateMachine(
                modifierGesture.Modifiers);
            HoldChord = holdChord;
        }

        public ModifierToggleStateMachine ToggleStateMachine { get; }

        public HotkeyChord HoldChord { get; }

        public bool[] PressedKeys { get; } = new bool[256];

        public bool PushToTalkActive { get; set; }

        public static HookGestureState CreateDefault() => new(
            HotkeyPreferences.Default.ToggleVietnamese.Chord,
            HotkeyPreferences.Default.PushToTalk.Chord);

        public HotkeyModifiers GetPressedModifiers()
        {
            var modifiers = HotkeyModifiers.None;
            if (IsPressed(VirtualKey.LeftControl) || IsPressed(VirtualKey.RightControl))
            {
                modifiers |= HotkeyModifiers.Control;
            }
            if (IsPressed(VirtualKey.LeftShift) || IsPressed(VirtualKey.RightShift))
            {
                modifiers |= HotkeyModifiers.Shift;
            }
            if (IsPressed(VirtualKey.LeftAlt) || IsPressed(VirtualKey.RightAlt))
            {
                modifiers |= HotkeyModifiers.Alt;
            }
            if (IsPressed(VirtualKey.LeftWindows) || IsPressed(VirtualKey.RightWindows))
            {
                modifiers |= HotkeyModifiers.Windows;
            }
            return modifiers;
        }

        public bool HasRequiredHoldModifiers() =>
            (GetPressedModifiers() & HoldChord.Modifiers) == HoldChord.Modifiers;

        public void Reset()
        {
            Array.Clear(PressedKeys);
            PushToTalkActive = false;
            _ = ToggleStateMachine.Process(KeyboardTransition.Reset);
        }

        private bool IsPressed(VirtualKey key)
        {
            var index = (int)key;
            return (uint)index < (uint)PressedKeys.Length && PressedKeys[index];
        }
    }

    private sealed class WindowsKeyboardHookNativeApi : IKeyboardHookNativeApi
    {
        private const int WhKeyboardLowLevel = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const uint LlkhfLowerIntegrityInjected = 0x00000002;
        private const uint LlkhfInjected = 0x00000010;

        public IDisposable Install(Func<RawKeyboardEvent, bool> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new HookLease(callback);
        }

        private sealed class HookLease : IDisposable
        {
            private readonly Func<RawKeyboardEvent, bool> callback;
            private readonly LowLevelKeyboardProcedure procedure;
            private nint hookHandle;

            public HookLease(Func<RawKeyboardEvent, bool> callback)
            {
                this.callback = callback;
                procedure = HookCallback;
                var module = GetModuleHandleW(null);
                hookHandle = SetWindowsHookExW(
                    WhKeyboardLowLevel,
                    procedure,
                    module,
                    0);
                if (hookHandle == 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows rejected the low-level modifier hook.");
                }
            }

            public void Dispose()
            {
                var handle = Interlocked.Exchange(ref hookHandle, 0);
                if (handle != 0 && !UnhookWindowsHookEx(handle))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows could not remove the modifier hook.");
                }
            }

            private nint HookCallback(int code, nint message, nint data)
            {
                if (code >= 0 && IsKeyboardMessage(message))
                {
                    var nativeEvent = Marshal.PtrToStructure<LowLevelKeyboardInput>(data);
                    var isInjected = (nativeEvent.Flags &
                        (LlkhfInjected | LlkhfLowerIntegrityInjected)) != 0;
                    _ = callback(new RawKeyboardEvent(
                        (VirtualKey)nativeEvent.VirtualKey,
                        message == WmKeyDown || message == WmSysKeyDown,
                        isInjected));
                }

                return CallNextHookEx(0, code, message, data);
            }

            private static bool IsKeyboardMessage(nint message)
            {
                var value = unchecked((int)message);
                return value is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp;
            }
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

        private delegate nint LowLevelKeyboardProcedure(
            int code,
            nint message,
            nint data);

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
        private static extern nint CallNextHookEx(
            nint hook,
            int code,
            nint message,
            nint data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandleW(string? moduleName);
    }
}
