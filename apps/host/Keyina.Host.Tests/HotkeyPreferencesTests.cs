using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Tests;

internal static class HotkeyPreferencesTests
{
    [KeyinaTest("hotkey preferences expose safe familiar defaults")]
    private static void DefaultsAreSafeAndFamiliar()
    {
        var preferences = HotkeyPreferences.Default;
        preferences.Validate();

        AssertEx.Equal(
            HotkeyGestureKind.ModifierGesture,
            preferences.ToggleVietnamese.GestureKind);
        AssertEx.Equal("Ctrl + Shift", HotkeyText.Format(preferences.ToggleVietnamese.Chord));
        AssertEx.Equal(
            HotkeyGestureKind.Hold,
            preferences.PushToTalk.GestureKind);
        AssertEx.Equal("Ctrl + Alt + Space", HotkeyText.Format(preferences.PushToTalk.Chord));
        AssertEx.Equal("Ctrl + Alt + V", HotkeyText.Format(preferences.ToggleDictation.Chord));
        AssertEx.Equal("Ctrl + Alt + T", HotkeyText.Format(preferences.TranslateSelection.Chord));
        AssertEx.Equal("Escape", HotkeyText.Format(preferences.CancelActiveCommand.Chord));
    }

    [KeyinaTest("hotkey text round trips supported chords deterministically")]
    private static void TextRoundTripsSupportedChords()
    {
        foreach (var text in new[]
                 {
                     "Ctrl + Shift",
                     "Ctrl + Alt + Space",
                     "Ctrl + Alt + V",
                     "Ctrl + Shift + F12",
                     "Escape",
                 })
        {
            AssertEx.True(
                HotkeyText.TryParse(text, out var chord),
                $"Could not parse supported chord: {text}.");
            AssertEx.Equal(text, HotkeyText.Format(chord));
        }

        AssertEx.False(
            HotkeyText.TryParse("Ctrl + Hyper + K", out _),
            "Unknown modifier was accepted.");
        AssertEx.False(
            HotkeyText.TryParse("Ctrl + Alt + DefinitelyNotAKey", out _),
            "Unknown key was accepted.");
    }

    [KeyinaTest("hotkey preferences reject unsafe and duplicate bindings")]
    private static void UnsafeAndDuplicateBindingsAreRejected()
    {
        var defaults = HotkeyPreferences.Default;

        AssertThrows<ArgumentException>(() => (defaults with
        {
            TranslateSelection = defaults.TranslateSelection with
            {
                Chord = new HotkeyChord(
                    HotkeyModifiers.Windows | HotkeyModifiers.Alt,
                    VirtualKey.T),
            },
        }).Validate());

        AssertThrows<ArgumentException>(() => (defaults with
        {
            TranslateSelection = defaults.TranslateSelection with
            {
                Chord = new HotkeyChord(HotkeyModifiers.None, VirtualKey.T),
            },
        }).Validate());

        AssertThrows<ArgumentException>(() => (defaults with
        {
            TranslateSelection = defaults.ToggleDictation with
            {
                GestureKind = HotkeyGestureKind.Press,
            },
        }).Validate());

        AssertThrows<ArgumentException>(() => (defaults with
        {
            ToggleVietnamese = defaults.ToggleVietnamese with
            {
                Chord = new HotkeyChord(HotkeyModifiers.Control, VirtualKey.None),
            },
        }).Validate());
    }

    [KeyinaTest("hotkey preferences map every configured command exactly once")]
    private static void EveryCommandIsMappedExactlyOnce()
    {
        var bindings = HotkeyPreferences.Default.ToBindings();

        AssertEx.Equal(5, bindings.Count);
        AssertEx.Equal(5, bindings.Select(binding => binding.Command).Distinct().Count());
        AssertEx.True(
            bindings.Any(binding => binding.Command == HotkeyCommand.ToggleVietnamese),
            "Toggle Vietnamese binding was missing.");
        AssertEx.True(
            bindings.Any(binding => binding.Command == HotkeyCommand.PushToTalkPressed),
            "Push-to-talk binding was missing.");
        AssertEx.True(
            bindings.Any(binding => binding.Command == HotkeyCommand.ToggleDictation),
            "Toggle dictation binding was missing.");
        AssertEx.True(
            bindings.Any(binding => binding.Command == HotkeyCommand.TranslateSelection),
            "Translation binding was missing.");
        AssertEx.True(
            bindings.Any(binding => binding.Command == HotkeyCommand.CancelDictation),
            "Cancel binding was missing.");
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}.");
    }
}
