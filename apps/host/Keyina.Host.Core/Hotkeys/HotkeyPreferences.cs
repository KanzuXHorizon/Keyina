namespace Keyina.Host.Core.Hotkeys;

public enum HotkeyGestureKind
{
    Press,
    Hold,
    ModifierGesture,
}

public sealed record HotkeyPreference(
    HotkeyGestureKind GestureKind,
    HotkeyChord Chord);

public sealed record ConfiguredHotkeyBinding(
    HotkeyCommand Command,
    HotkeyGestureKind GestureKind,
    HotkeyChord Chord);

public sealed record HotkeyPreferences(
    HotkeyPreference ToggleVietnamese,
    HotkeyPreference PushToTalk,
    HotkeyPreference ToggleDictation,
    HotkeyPreference TranslateSelection,
    HotkeyPreference CancelActiveCommand)
{
    public HotkeyPreference UndoTranslation { get; init; } = new(
        HotkeyGestureKind.Press,
        new HotkeyChord(
            HotkeyModifiers.Control | HotkeyModifiers.Alt,
            VirtualKey.Z));

    private const HotkeyModifiers SupportedModifiers =
        HotkeyModifiers.Control |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Alt |
        HotkeyModifiers.Windows;

    public static HotkeyPreferences Default { get; } = new(
        new HotkeyPreference(
            HotkeyGestureKind.ModifierGesture,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Shift,
                VirtualKey.None)),
        new HotkeyPreference(
            HotkeyGestureKind.Hold,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VirtualKey.Space)),
        new HotkeyPreference(
            HotkeyGestureKind.Press,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VirtualKey.V)),
        new HotkeyPreference(
            HotkeyGestureKind.Press,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VirtualKey.T)),
        new HotkeyPreference(
            HotkeyGestureKind.Press,
            new HotkeyChord(HotkeyModifiers.None, VirtualKey.Escape)));

    public HotkeyPreference GetPreference(HotkeyCommand command) => command switch
    {
        HotkeyCommand.ToggleVietnamese => ToggleVietnamese,
        HotkeyCommand.PushToTalkPressed => PushToTalk,
        HotkeyCommand.ToggleDictation => ToggleDictation,
        HotkeyCommand.TranslateSelection => TranslateSelection,
        HotkeyCommand.UndoTranslation => UndoTranslation,
        HotkeyCommand.CancelDictation => CancelActiveCommand,
        _ => throw new ArgumentOutOfRangeException(
            nameof(command),
            command,
            "The command does not expose a configurable shortcut."),
    };

    public HotkeyPreferences WithChord(
        HotkeyCommand command,
        HotkeyChord chord) => command switch
    {
        HotkeyCommand.ToggleVietnamese => this with
        {
            ToggleVietnamese = ToggleVietnamese with { Chord = chord },
        },
        HotkeyCommand.PushToTalkPressed => this with
        {
            PushToTalk = PushToTalk with { Chord = chord },
        },
        HotkeyCommand.ToggleDictation => this with
        {
            ToggleDictation = ToggleDictation with { Chord = chord },
        },
        HotkeyCommand.TranslateSelection => this with
        {
            TranslateSelection = TranslateSelection with { Chord = chord },
        },
        HotkeyCommand.UndoTranslation => this with
        {
            UndoTranslation = UndoTranslation with { Chord = chord },
        },
        HotkeyCommand.CancelDictation => this with
        {
            CancelActiveCommand = CancelActiveCommand with { Chord = chord },
        },
        _ => throw new ArgumentOutOfRangeException(
            nameof(command),
            command,
            "The command does not expose a configurable shortcut."),
    };

    public IReadOnlyList<ConfiguredHotkeyBinding> ToBindings() =>
    [
        new(
            HotkeyCommand.ToggleVietnamese,
            ToggleVietnamese.GestureKind,
            ToggleVietnamese.Chord),
        new(
            HotkeyCommand.PushToTalkPressed,
            PushToTalk.GestureKind,
            PushToTalk.Chord),
        new(
            HotkeyCommand.ToggleDictation,
            ToggleDictation.GestureKind,
            ToggleDictation.Chord),
        new(
            HotkeyCommand.TranslateSelection,
            TranslateSelection.GestureKind,
            TranslateSelection.Chord),
        new(
            HotkeyCommand.UndoTranslation,
            UndoTranslation.GestureKind,
            UndoTranslation.Chord),
    ];

    public void Validate()
    {
        ValidatePreference(
            ToggleVietnamese,
            HotkeyCommand.ToggleVietnamese,
            HotkeyGestureKind.ModifierGesture);
        ValidatePreference(
            PushToTalk,
            HotkeyCommand.PushToTalkPressed,
            HotkeyGestureKind.Hold);
        ValidatePreference(
            ToggleDictation,
            HotkeyCommand.ToggleDictation,
            HotkeyGestureKind.Press);
        ValidatePreference(
            TranslateSelection,
            HotkeyCommand.TranslateSelection,
            HotkeyGestureKind.Press);
        ValidatePreference(
            UndoTranslation,
            HotkeyCommand.UndoTranslation,
            HotkeyGestureKind.Press);
        var chords = new HashSet<HotkeyChord>();
        foreach (var binding in ToBindings())
        {
            if (!chords.Add(binding.Chord))
            {
                throw new ArgumentException(
                    $"Duplicate hotkey chord: {HotkeyText.Format(binding.Chord)}.");
            }
        }
    }

    private static void ValidatePreference(
        HotkeyPreference preference,
        HotkeyCommand command,
        HotkeyGestureKind requiredGestureKind)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (preference.GestureKind != requiredGestureKind)
        {
            throw new ArgumentException(
                $"{command} requires a {requiredGestureKind} gesture.");
        }

        var chord = preference.Chord;
        if ((chord.Modifiers & ~SupportedModifiers) != 0)
        {
            throw new ArgumentException("A shortcut contains unsupported modifiers.");
        }
        if ((chord.Modifiers & HotkeyModifiers.Windows) != 0)
        {
            throw new ArgumentException(
                "Windows-key shortcuts are reserved for the operating system.");
        }

        if (requiredGestureKind == HotkeyGestureKind.ModifierGesture)
        {
            if (chord.Key != VirtualKey.None)
            {
                throw new ArgumentException(
                    "A modifier gesture cannot contain a primary key.");
            }
            if (CountModifiers(chord.Modifiers) < 2)
            {
                throw new ArgumentException(
                    "A modifier gesture requires at least two modifiers.");
            }
            return;
        }

        if (!chord.HasPrimaryKey)
        {
            throw new ArgumentException(
                "A press or hold shortcut requires a non-modifier key.");
        }
        if (requiredGestureKind == HotkeyGestureKind.Hold &&
            chord.Modifiers == HotkeyModifiers.None)
        {
            throw new ArgumentException(
                "A hold shortcut requires at least one modifier.");
        }
        if (chord.Key == VirtualKey.Escape)
        {
            throw new ArgumentException(
                "Escape is reserved for local dialog and overlay cancellation.");
        }
        if (chord.Modifiers == HotkeyModifiers.None &&
            RequiresModifier(chord.Key))
        {
            throw new ArgumentException(
                "Letter, number, punctuation, and Space shortcuts require a modifier.");
        }
    }

    private static int CountModifiers(HotkeyModifiers modifiers)
    {
        var count = 0;
        foreach (var modifier in new[]
                 {
                     HotkeyModifiers.Control,
                     HotkeyModifiers.Shift,
                     HotkeyModifiers.Alt,
                     HotkeyModifiers.Windows,
                 })
        {
            if ((modifiers & modifier) != 0)
            {
                count++;
            }
        }
        return count;
    }

    private static bool RequiresModifier(VirtualKey key) =>
        key is >= VirtualKey.D0 and <= VirtualKey.Z or
            VirtualKey.Space or
            VirtualKey.Semicolon or
            VirtualKey.Plus or
            VirtualKey.Comma or
            VirtualKey.Minus or
            VirtualKey.Period or
            VirtualKey.Slash or
            VirtualKey.Backtick or
            VirtualKey.LeftBracket or
            VirtualKey.Backslash or
            VirtualKey.RightBracket or
            VirtualKey.Quote;
}

public static class HotkeyText
{
    private static readonly Dictionary<string, HotkeyModifiers> ModifierTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = HotkeyModifiers.Control,
            ["control"] = HotkeyModifiers.Control,
            ["shift"] = HotkeyModifiers.Shift,
            ["alt"] = HotkeyModifiers.Alt,
            ["win"] = HotkeyModifiers.Windows,
            ["windows"] = HotkeyModifiers.Windows,
        };

    private static readonly Dictionary<string, VirtualKey> NamedKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["backspace"] = VirtualKey.Backspace,
            ["tab"] = VirtualKey.Tab,
            ["enter"] = VirtualKey.Enter,
            ["pause"] = VirtualKey.Pause,
            ["caps lock"] = VirtualKey.CapsLock,
            ["capslock"] = VirtualKey.CapsLock,
            ["escape"] = VirtualKey.Escape,
            ["esc"] = VirtualKey.Escape,
            ["space"] = VirtualKey.Space,
            ["page up"] = VirtualKey.PageUp,
            ["pageup"] = VirtualKey.PageUp,
            ["page down"] = VirtualKey.PageDown,
            ["pagedown"] = VirtualKey.PageDown,
            ["end"] = VirtualKey.End,
            ["home"] = VirtualKey.Home,
            ["left"] = VirtualKey.Left,
            ["up"] = VirtualKey.Up,
            ["right"] = VirtualKey.Right,
            ["down"] = VirtualKey.Down,
            ["insert"] = VirtualKey.Insert,
            ["delete"] = VirtualKey.Delete,
            [";"] = VirtualKey.Semicolon,
            ["+"] = VirtualKey.Plus,
            [","] = VirtualKey.Comma,
            ["-"] = VirtualKey.Minus,
            ["."] = VirtualKey.Period,
            ["/"] = VirtualKey.Slash,
            ["`"] = VirtualKey.Backtick,
            ["["] = VirtualKey.LeftBracket,
            ["\\"] = VirtualKey.Backslash,
            ["]"] = VirtualKey.RightBracket,
            ["'"] = VirtualKey.Quote,
        };

    public static string Format(HotkeyChord chord)
    {
        var parts = new List<string>(5);
        AddModifier(parts, chord.Modifiers, HotkeyModifiers.Control, "Ctrl");
        AddModifier(parts, chord.Modifiers, HotkeyModifiers.Shift, "Shift");
        AddModifier(parts, chord.Modifiers, HotkeyModifiers.Alt, "Alt");
        AddModifier(parts, chord.Modifiers, HotkeyModifiers.Windows, "Win");
        if (chord.Key != VirtualKey.None)
        {
            parts.Add(FormatKey(chord.Key));
        }
        return string.Join(" + ", parts);
    }

    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        var key = VirtualKey.None;
        foreach (var rawToken in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (rawToken.Length == 0)
            {
                return false;
            }
            if (ModifierTokens.TryGetValue(rawToken, out var modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    return false;
                }
                modifiers |= modifier;
                continue;
            }
            if (key != VirtualKey.None || !TryParseKey(rawToken, out key))
            {
                return false;
            }
        }

        if (modifiers == HotkeyModifiers.None && key == VirtualKey.None)
        {
            return false;
        }
        chord = new HotkeyChord(modifiers, key);
        return true;
    }

    private static void AddModifier(
        List<string> parts,
        HotkeyModifiers modifiers,
        HotkeyModifiers flag,
        string displayName)
    {
        if ((modifiers & flag) != 0)
        {
            parts.Add(displayName);
        }
    }

    private static string FormatKey(VirtualKey key)
    {
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return ((char)(ushort)key).ToString();
        }
        if (key is >= VirtualKey.D0 and <= VirtualKey.D9)
        {
            return ((char)(ushort)key).ToString();
        }
        if (key is >= VirtualKey.F1 and <= VirtualKey.F24)
        {
            return $"F{(ushort)key - (ushort)VirtualKey.F1 + 1}";
        }
        return key switch
        {
            VirtualKey.Backspace => "Backspace",
            VirtualKey.Tab => "Tab",
            VirtualKey.Enter => "Enter",
            VirtualKey.Pause => "Pause",
            VirtualKey.CapsLock => "Caps Lock",
            VirtualKey.Escape => "Escape",
            VirtualKey.Space => "Space",
            VirtualKey.PageUp => "Page Up",
            VirtualKey.PageDown => "Page Down",
            VirtualKey.End => "End",
            VirtualKey.Home => "Home",
            VirtualKey.Left => "Left",
            VirtualKey.Up => "Up",
            VirtualKey.Right => "Right",
            VirtualKey.Down => "Down",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Semicolon => ";",
            VirtualKey.Plus => "+",
            VirtualKey.Comma => ",",
            VirtualKey.Minus => "-",
            VirtualKey.Period => ".",
            VirtualKey.Slash => "/",
            VirtualKey.Backtick => "`",
            VirtualKey.LeftBracket => "[",
            VirtualKey.Backslash => "\\",
            VirtualKey.RightBracket => "]",
            VirtualKey.Quote => "'",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported shortcut key."),
        };
    }

    private static bool TryParseKey(string token, out VirtualKey key)
    {
        if (NamedKeys.TryGetValue(token, out key))
        {
            return true;
        }
        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z')
            {
                key = (VirtualKey)character;
                return true;
            }
            if (character is >= '0' and <= '9')
            {
                key = (VirtualKey)character;
                return true;
            }
        }
        if (token.Length is 2 or 3 &&
            (token[0] == 'F' || token[0] == 'f') &&
            int.TryParse(token.AsSpan(1), out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            key = (VirtualKey)((ushort)VirtualKey.F1 + functionNumber - 1);
            return true;
        }

        key = VirtualKey.None;
        return false;
    }
}
