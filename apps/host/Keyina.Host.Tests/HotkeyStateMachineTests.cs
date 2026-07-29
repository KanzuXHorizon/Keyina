using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Tests;

internal static class HotkeyStateMachineTests
{
    [KeyinaTest("Ctrl Shift toggles once regardless of press and release order")]
    private static void CtrlShiftTogglesOnceInEitherOrder()
    {
        foreach (var sequence in new[]
        {
            new[]
            {
                Down(VirtualKey.LeftControl), Down(VirtualKey.LeftShift),
                Up(VirtualKey.LeftControl), Up(VirtualKey.LeftShift),
            },
            new[]
            {
                Down(VirtualKey.RightShift), Down(VirtualKey.RightControl),
                Up(VirtualKey.RightShift), Up(VirtualKey.RightControl),
            },
        })
        {
            var machine = new ModifierToggleStateMachine();
            var commands = sequence.Select(transition => machine.Process(transition)).Where(command => command != HotkeyCommand.None).ToArray();
            AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleVietnamese]),
                $"Unexpected command sequence: {string.Join(',', commands)}");
        }
    }

    [KeyinaTest("left and right modifier variants share one chord without double toggle")]
    private static void LeftAndRightVariantsDoNotDoubleToggle()
    {
        var machine = new ModifierToggleStateMachine();
        var commands = new[]
        {
            Down(VirtualKey.LeftControl),
            Down(VirtualKey.RightControl),
            Down(VirtualKey.LeftShift),
            Up(VirtualKey.LeftControl),
            Up(VirtualKey.RightControl),
            Up(VirtualKey.LeftShift),
        }.Select(transition => machine.Process(transition)).Where(command => command != HotkeyCommand.None).ToArray();

        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleVietnamese]),
            "Mixed left/right modifiers toggled more than once.");
    }

    [KeyinaTest("modifier auto repeat does not emit additional commands")]
    private static void AutoRepeatIsIgnored()
    {
        var machine = new ModifierToggleStateMachine();
        var commands = new[]
        {
            Down(VirtualKey.LeftControl),
            Down(VirtualKey.LeftShift),
            Repeat(VirtualKey.LeftShift),
            Repeat(VirtualKey.LeftControl),
            Up(VirtualKey.LeftShift),
            Up(VirtualKey.LeftControl),
        }.Select(transition => machine.Process(transition)).Where(command => command != HotkeyCommand.None).ToArray();

        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleVietnamese]),
            "Repeated modifier key-down emitted extra toggles.");
    }

    [KeyinaTest("a non modifier key cancels the Ctrl Shift toggle")]
    private static void OtherKeyCancelsChord()
    {
        var machine = new ModifierToggleStateMachine();
        var commands = new[]
        {
            Down(VirtualKey.LeftControl),
            Down(VirtualKey.LeftShift),
            Down(VirtualKey.C),
            Up(VirtualKey.C),
            Up(VirtualKey.LeftShift),
            Up(VirtualKey.LeftControl),
        }.Select(transition => machine.Process(transition)).Where(command => command != HotkeyCommand.None).ToArray();

        AssertEx.Equal(0, commands.Length, "Ctrl+Shift+C must not toggle Vietnamese input.");
    }

    [KeyinaTest("Alt or Windows key contamination cancels the Ctrl Shift toggle")]
    private static void AltAndWindowsCancelChord()
    {
        foreach (var contaminatingKey in new[] { VirtualKey.LeftAlt, VirtualKey.LeftWindows })
        {
            var machine = new ModifierToggleStateMachine();
            var commands = new[]
            {
                Down(VirtualKey.LeftControl),
                Down(VirtualKey.LeftShift),
                Down(contaminatingKey),
                Up(contaminatingKey),
                Up(VirtualKey.LeftShift),
                Up(VirtualKey.LeftControl),
            }.Select(transition => machine.Process(transition)).Where(command => command != HotkeyCommand.None).ToArray();
            AssertEx.Equal(0, commands.Length, $"{contaminatingKey} did not cancel Ctrl+Shift.");
        }
    }

    [KeyinaTest("reset clears lost keyboard state without emitting a toggle")]
    private static void ResetClearsState()
    {
        var machine = new ModifierToggleStateMachine();
        AssertEx.Equal(HotkeyCommand.None, machine.Process(Down(VirtualKey.LeftControl)));
        AssertEx.Equal(HotkeyCommand.None, machine.Process(Down(VirtualKey.LeftShift)));
        AssertEx.Equal(HotkeyCommand.None, machine.Process(KeyboardTransition.Reset));
        AssertEx.Equal(HotkeyCommand.None, machine.Process(Up(VirtualKey.LeftShift)));
        AssertEx.Equal(HotkeyCommand.None, machine.Process(Up(VirtualKey.LeftControl)));
    }

    [KeyinaTest("default hotkey bindings are valid unique and familiar")]
    private static void DefaultBindingsAreValid()
    {
        var bindings = DefaultHotkeys.Create();
        AssertEx.Equal(3, bindings.Count);
        AssertEx.Equal(
            new HotkeyBinding(
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.Space),
                HotkeyCommand.PushToTalkPressed),
            bindings[0]);
        AssertEx.Equal(
            new HotkeyBinding(
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.V),
                HotkeyCommand.ToggleDictation),
            bindings[1]);
        AssertEx.Equal(
            new HotkeyBinding(new HotkeyChord(HotkeyModifiers.None, VirtualKey.Escape),
                HotkeyCommand.CancelDictation),
            bindings[2]);

        HotkeyBindingValidator.Validate(bindings);
    }

    [KeyinaTest("registered hotkey validation rejects modifier keys and duplicates")]
    private static void InvalidBindingsAreRejected()
    {
        AssertThrows<ArgumentException>(() => HotkeyBindingValidator.Validate(
        [
            new HotkeyBinding(
                new HotkeyChord(HotkeyModifiers.Control, VirtualKey.LeftShift),
                HotkeyCommand.ToggleVietnamese),
        ]));

        var duplicate = new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.V);
        AssertThrows<ArgumentException>(() => HotkeyBindingValidator.Validate(
        [
            new HotkeyBinding(duplicate, HotkeyCommand.ToggleDictation),
            new HotkeyBinding(duplicate, HotkeyCommand.PushToTalkPressed),
        ]));
    }

    private static KeyboardTransition Down(VirtualKey key) =>
        new(key, KeyboardTransitionKind.KeyDown, IsRepeat: false);

    private static KeyboardTransition Repeat(VirtualKey key) =>
        new(key, KeyboardTransitionKind.KeyDown, IsRepeat: true);

    private static KeyboardTransition Up(VirtualKey key) =>
        new(key, KeyboardTransitionKind.KeyUp, IsRepeat: false);

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
