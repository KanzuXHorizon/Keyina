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
    private byte _controlMask;
    private byte _shiftMask;
    private bool _candidate;
    private bool _contaminated;
    private bool _fired;

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
            case VirtualKey.RightAlt:
            case VirtualKey.LeftWindows:
            case VirtualKey.RightWindows:
                if (transition.Kind == KeyboardTransitionKind.KeyDown && HasTrackedModifier)
                {
                    _contaminated = true;
                }
                break;
            default:
                if (transition.Kind == KeyboardTransitionKind.KeyDown && HasTrackedModifier)
                {
                    _contaminated = true;
                }
                break;
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

    private bool HasTrackedModifier => _controlMask != 0 || _shiftMask != 0;

    private bool IsCompleteChord => _controlMask != 0 && _shiftMask != 0;

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

    private void Clear()
    {
        _controlMask = 0;
        _shiftMask = 0;
        _candidate = false;
        _contaminated = false;
        _fired = false;
    }
}
