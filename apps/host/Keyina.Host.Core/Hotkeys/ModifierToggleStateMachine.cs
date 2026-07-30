namespace Keyina.Host.Core.Hotkeys;

public enum KeyboardTransitionKind
{
    KeyDown,
    KeyUp,
    Reset,
}

public readonly record struct KeyboardTransition(
    VirtualKey Key,
    KeyboardTransitionKind Kind,
    bool IsRepeat)
{
    public static KeyboardTransition Reset { get; } =
        new(default, KeyboardTransitionKind.Reset, IsRepeat: false);
}

public sealed class ModifierToggleStateMachine
{
    private const HotkeyModifiers SupportedModifiers =
        HotkeyModifiers.Control |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Alt;

    private readonly HotkeyModifiers requiredModifiers;
    private byte _controlMask;
    private byte _shiftMask;
    private byte _altMask;
    private byte _windowsMask;
    private bool _candidate;
    private bool _contaminated;
    private bool _fired;

    public ModifierToggleStateMachine(
        HotkeyModifiers requiredModifiers =
            HotkeyModifiers.Control | HotkeyModifiers.Shift)
    {
        if ((requiredModifiers & ~SupportedModifiers) != 0 ||
            CountModifiers(requiredModifiers) < 2)
        {
            throw new ArgumentException(
                "Modifier toggle gestures require at least two supported modifiers.",
                nameof(requiredModifiers));
        }
        this.requiredModifiers = requiredModifiers;
    }

    public HotkeyCommand Process(in KeyboardTransition transition)
    {
        if (transition.Kind == KeyboardTransitionKind.Reset)
        {
            Clear();
            return HotkeyCommand.None;
        }

        if (transition.IsRepeat && transition.Kind == KeyboardTransitionKind.KeyDown)
        {
            return HotkeyCommand.None;
        }

        var wasComplete = IsCompleteChord;
        var modifier = ModifierForKey(transition.Key);
        switch (transition.Key)
        {
            case VirtualKey.LeftControl:
                SetMask(ref _controlMask, 0b01, transition.Kind);
                break;
            case VirtualKey.RightControl:
                SetMask(ref _controlMask, 0b10, transition.Kind);
                break;
            case VirtualKey.LeftShift:
                SetMask(ref _shiftMask, 0b01, transition.Kind);
                break;
            case VirtualKey.RightShift:
                SetMask(ref _shiftMask, 0b10, transition.Kind);
                break;
            case VirtualKey.LeftAlt:
                SetMask(ref _altMask, 0b01, transition.Kind);
                break;
            case VirtualKey.RightAlt:
                SetMask(ref _altMask, 0b10, transition.Kind);
                break;
            case VirtualKey.LeftWindows:
                SetMask(ref _windowsMask, 0b01, transition.Kind);
                break;
            case VirtualKey.RightWindows:
                SetMask(ref _windowsMask, 0b10, transition.Kind);
                break;
        }

        if (transition.Kind == KeyboardTransitionKind.KeyDown &&
            ((modifier != HotkeyModifiers.None &&
              (requiredModifiers & modifier) == 0) ||
             (modifier == HotkeyModifiers.None && HasTrackedModifier)))
        {
            _contaminated = true;
        }

        if (IsCompleteChord && !_contaminated && !_fired)
        {
            _candidate = true;
        }

        var command = HotkeyCommand.None;
        if (wasComplete && !IsCompleteChord && _candidate && !_contaminated && !_fired)
        {
            _fired = true;
            command = HotkeyCommand.ToggleVietnamese;
        }

        if (!HasTrackedModifier)
        {
            Clear();
        }

        return command;
    }

    private bool HasTrackedModifier =>
        _controlMask != 0 || _shiftMask != 0 || _altMask != 0 || _windowsMask != 0;

    private HotkeyModifiers PressedModifiers
    {
        get
        {
            var modifiers = HotkeyModifiers.None;
            if (_controlMask != 0)
            {
                modifiers |= HotkeyModifiers.Control;
            }
            if (_shiftMask != 0)
            {
                modifiers |= HotkeyModifiers.Shift;
            }
            if (_altMask != 0)
            {
                modifiers |= HotkeyModifiers.Alt;
            }
            if (_windowsMask != 0)
            {
                modifiers |= HotkeyModifiers.Windows;
            }
            return modifiers;
        }
    }

    private bool IsCompleteChord => PressedModifiers == requiredModifiers;

    private static void SetMask(
        ref byte mask,
        byte bit,
        KeyboardTransitionKind transitionKind)
    {
        if (transitionKind == KeyboardTransitionKind.KeyDown)
        {
            mask |= bit;
        }
        else if (transitionKind == KeyboardTransitionKind.KeyUp)
        {
            mask &= (byte)~bit;
        }
    }

    private static HotkeyModifiers ModifierForKey(VirtualKey key) => key switch
    {
        VirtualKey.LeftControl or VirtualKey.RightControl => HotkeyModifiers.Control,
        VirtualKey.LeftShift or VirtualKey.RightShift => HotkeyModifiers.Shift,
        VirtualKey.LeftAlt or VirtualKey.RightAlt => HotkeyModifiers.Alt,
        VirtualKey.LeftWindows or VirtualKey.RightWindows => HotkeyModifiers.Windows,
        _ => HotkeyModifiers.None,
    };

    private static int CountModifiers(HotkeyModifiers modifiers)
    {
        var count = 0;
        foreach (var flag in new[]
                 {
                     HotkeyModifiers.Control,
                     HotkeyModifiers.Shift,
                     HotkeyModifiers.Alt,
                 })
        {
            if ((modifiers & flag) != 0)
            {
                count++;
            }
        }
        return count;
    }

    private void Clear()
    {
        _controlMask = 0;
        _shiftMask = 0;
        _altMask = 0;
        _windowsMask = 0;
        _candidate = false;
        _contaminated = false;
        _fired = false;
    }
}
