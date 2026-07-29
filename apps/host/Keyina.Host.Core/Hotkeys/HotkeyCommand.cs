namespace Keyina.Host.Core.Hotkeys;

public enum HotkeyCommand
{
    None,
    ToggleVietnamese,
    PushToTalkPressed,
    PushToTalkReleased,
    ToggleDictation,
    CancelDictation,
}

public sealed record HotkeyBinding(HotkeyChord Chord, HotkeyCommand Command);

public static class DefaultHotkeys
{
    public static IReadOnlyList<HotkeyBinding> Create() =>
    [
        new(
            new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.Space),
            HotkeyCommand.PushToTalkPressed),
        new(
            new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.V),
            HotkeyCommand.ToggleDictation),
        new(
            new HotkeyChord(HotkeyModifiers.None, VirtualKey.Escape),
            HotkeyCommand.CancelDictation),
    ];
}

public static class HotkeyBindingValidator
{
    private const HotkeyModifiers SupportedModifiers =
        HotkeyModifiers.Control |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Alt |
        HotkeyModifiers.Windows;

    public static void Validate(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var chords = new HashSet<HotkeyChord>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (binding.Command == HotkeyCommand.None)
            {
                throw new ArgumentException("A binding must produce a command.", nameof(bindings));
            }
            if ((binding.Chord.Modifiers & ~SupportedModifiers) != 0)
            {
                throw new ArgumentException("A binding contains unsupported modifiers.", nameof(bindings));
            }
            if (binding.Chord.IsModifierOnly)
            {
                throw new ArgumentException(
                    "Registered hotkeys require a non-modifier virtual key.",
                    nameof(bindings));
            }
            if (!chords.Add(binding.Chord))
            {
                throw new ArgumentException(
                    $"Duplicate hotkey chord: {binding.Chord}.",
                    nameof(bindings));
            }
        }
    }
}
