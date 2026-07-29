namespace Keyina.Host.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8,
}

public enum VirtualKey : ushort
{
    Escape = 0x1B,
    Space = 0x20,
    A = 0x41,
    C = 0x43,
    V = 0x56,
    LeftWindows = 0x5B,
    RightWindows = 0x5C,
    LeftShift = 0xA0,
    RightShift = 0xA1,
    LeftControl = 0xA2,
    RightControl = 0xA3,
    LeftAlt = 0xA4,
    RightAlt = 0xA5,
}

public readonly record struct HotkeyChord(HotkeyModifiers Modifiers, VirtualKey Key)
{
    public bool IsModifierOnly => Key.IsModifier();
}

public static class VirtualKeyExtensions
{
    public static bool IsModifier(this VirtualKey key) => key is
        VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.LeftAlt or VirtualKey.RightAlt or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;
}
