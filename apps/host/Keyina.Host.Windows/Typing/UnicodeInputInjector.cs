using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Keyina.Host.Windows.Typing;

public interface IUnicodeInputInjector
{
    void Apply(HookEdit edit);
}

public sealed class UnicodeInputInjector : IUnicodeInputInjector
{
    public static readonly nuint InjectionMarker = unchecked((nuint)0x4B4559494E41UL);

    private readonly IInputSender sender;

    public UnicodeInputInjector(IInputSender? sender = null)
    {
        this.sender = sender ?? new WindowsInputSender();
    }

    public void Apply(HookEdit edit)
    {
        if (!edit.ConsumePhysicalKey)
        {
            return;
        }

        var inputs = new List<KeyboardInputEvent>(
            checked(edit.BackspaceCount * 2 + edit.InsertText.Length * 2));
        for (var index = 0; index < edit.BackspaceCount; index++)
        {
            inputs.Add(KeyboardInputEvent.VirtualKeyStroke(0x08, keyUp: false));
            inputs.Add(KeyboardInputEvent.VirtualKeyStroke(0x08, keyUp: true));
        }
        foreach (var codeUnit in edit.InsertText)
        {
            inputs.Add(KeyboardInputEvent.Unicode(codeUnit, keyUp: false));
            inputs.Add(KeyboardInputEvent.Unicode(codeUnit, keyUp: true));
        }
        if (inputs.Count != 0)
        {
            sender.Send(inputs);
        }
    }

    public interface IInputSender
    {
        void Send(IReadOnlyList<KeyboardInputEvent> inputs);
    }

    public readonly record struct KeyboardInputEvent(
        ushort VirtualKey,
        ushort ScanCode,
        uint Flags,
        nuint ExtraInfo)
    {
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventUnicode = 0x0004;

        public static KeyboardInputEvent VirtualKeyStroke(ushort key, bool keyUp) =>
            new(key, 0, keyUp ? KeyEventKeyUp : 0, InjectionMarker);

        public static KeyboardInputEvent Unicode(char codeUnit, bool keyUp) =>
            new(
                0,
                codeUnit,
                KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                InjectionMarker);
    }

    private sealed class WindowsInputSender : IInputSender
    {
        private const uint InputKeyboard = 1;

        public void Send(IReadOnlyList<KeyboardInputEvent> inputs)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            var native = new Input[inputs.Count];
            for (var index = 0; index < inputs.Count; index++)
            {
                var item = inputs[index];
                native[index] = new Input
                {
                    Type = InputKeyboard,
                    Union = new InputUnion
                    {
                        Keyboard = new KeybdInput
                        {
                            VirtualKey = item.VirtualKey,
                            ScanCode = item.ScanCode,
                            Flags = item.Flags,
                            ExtraInfo = item.ExtraInfo,
                        },
                    },
                };
            }

            var sent = SendInput(
                checked((uint)native.Length),
                native,
                Marshal.SizeOf<Input>());
            if (sent != native.Length)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows did not inject the complete Keyina edit.");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion Union;
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KeybdInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeybdInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public nuint ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(
            uint inputCount,
            [In] Input[] inputs,
            int inputSize);
    }
}
