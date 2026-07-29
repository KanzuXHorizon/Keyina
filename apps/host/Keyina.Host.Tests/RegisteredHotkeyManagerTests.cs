using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Windows.Hotkeys;

namespace Keyina.Host.Tests;

internal static class RegisteredHotkeyManagerTests
{
    [KeyinaTest("registered hotkey manager registers dispatches and unregisters configured commands")]
    private static void RegistrationLifecycleWorks()
    {
        var native = new FakeHotkeyNativeApi();
        var commands = new List<HotkeyCommand>();
        using var manager = new RegisteredHotkeyManager(native, windowHandle: 123);
        manager.CommandReceived += (_, command) => commands.Add(command);

        manager.Register(CreateRegisteredBindings());
        AssertEx.Equal(3, native.Registered.Count);
        AssertEx.True(
            native.Registered.Values.Any(value =>
                value.Modifiers == 0x4003 &&
                value.Key == (uint)VirtualKey.Space),
            "Push-to-talk chord was not registered.");

        AssertEx.True(manager.TryDispatch(2), "Known WM_HOTKEY id was not dispatched.");
        AssertEx.True(!manager.TryDispatch(999), "Unknown WM_HOTKEY id was consumed.");
        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleDictation]),
            "Wrong command was dispatched for hotkey id 2.");

        manager.Dispose();
        manager.Dispose();
        AssertEx.Equal(3, native.Unregistered.Count);
    }

    [KeyinaTest("registered hotkey conflict rolls back earlier registrations and reports the chord")]
    private static void ConflictRollsBack()
    {
        var native = new FakeHotkeyNativeApi { FailRegistrationId = 2, FailureCode = 1409 };
        using var manager = new RegisteredHotkeyManager(native, windowHandle: 456);

        try
        {
            manager.Register(CreateRegisteredBindings());
        }
        catch (HotkeyRegistrationException exception)
        {
            AssertEx.Equal(1409, exception.NativeErrorCode);
            AssertEx.Equal(2, exception.HotkeyId);
            AssertEx.Equal(new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VirtualKey.V), exception.Chord);
            AssertEx.True(native.Unregistered.SequenceEqual([1]),
                "Earlier registration was not rolled back.");
            AssertEx.Equal(0, manager.RegisteredCount);
            return;
        }

        throw new InvalidOperationException("Expected hotkey registration conflict.");
    }

    [KeyinaTest("registered hotkey manager rejects duplicate ids chords and modifier-only keys")]
    private static void InvalidBindingsAreRejected()
    {
        var native = new FakeHotkeyNativeApi();
        using var manager = new RegisteredHotkeyManager(native, windowHandle: 1);

        AssertThrows<ArgumentException>(() => manager.Register(
        [
            new RegisteredHotkeyBinding(1,
                new HotkeyChord(HotkeyModifiers.Control, VirtualKey.V),
                HotkeyCommand.ToggleDictation),
            new RegisteredHotkeyBinding(1,
                new HotkeyChord(HotkeyModifiers.Control, VirtualKey.Space),
                HotkeyCommand.PushToTalkPressed),
        ]));
        AssertThrows<ArgumentException>(() => manager.Register(
        [
            new RegisteredHotkeyBinding(1,
                new HotkeyChord(HotkeyModifiers.Control, VirtualKey.V),
                HotkeyCommand.ToggleDictation),
            new RegisteredHotkeyBinding(2,
                new HotkeyChord(HotkeyModifiers.Control, VirtualKey.V),
                HotkeyCommand.CancelDictation),
        ]));
        AssertThrows<ArgumentException>(() => manager.Register(
        [
            new RegisteredHotkeyBinding(1,
                new HotkeyChord(HotkeyModifiers.Control, VirtualKey.LeftShift),
                HotkeyCommand.ToggleVietnamese),
        ]));
    }

    [KeyinaTest("modifier keyboard hook delegates transitions without swallowing input")]
    private static void ModifierHookPostsOnlyCommands()
    {
        var native = new FakeKeyboardHookNativeApi();
        var commands = new List<HotkeyCommand>();
        using var hook = new ModifierKeyboardHook(native);
        hook.CommandReceived += (_, command) => commands.Add(command);
        hook.Start();

        AssertEx.True(native.Callback is not null, "Keyboard hook callback was not installed.");
        AssertEx.True(!native.Callback!(new RawKeyboardEvent(VirtualKey.LeftControl, true, false)),
            "Control key was swallowed.");
        AssertEx.True(!native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, true, false)),
            "Shift key was swallowed.");
        AssertEx.True(!native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, false, false)),
            "Shift release was swallowed.");
        AssertEx.True(!native.Callback(new RawKeyboardEvent(VirtualKey.LeftControl, false, false)),
            "Control release was swallowed.");
        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleVietnamese]),
            "Modifier hook did not post exactly one toggle.");

        native.Callback(new RawKeyboardEvent(VirtualKey.LeftControl, true, true));
        native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, true, true));
        native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, false, true));
        native.Callback(new RawKeyboardEvent(VirtualKey.LeftControl, false, true));
        AssertEx.Equal(1, commands.Count);

        hook.Dispose();
        hook.Dispose();
        AssertEx.Equal(1, native.UninstallCount);
    }

    private static RegisteredHotkeyBinding[] CreateRegisteredBindings() =>
        DefaultHotkeys.Create()
            .Select((binding, index) => new RegisteredHotkeyBinding(
                index + 1,
                binding.Chord,
                binding.Command))
            .ToArray();

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

    private sealed class FakeHotkeyNativeApi : IRegisteredHotkeyNativeApi
    {
        public Dictionary<int, (uint Modifiers, uint Key)> Registered { get; } = [];
        public List<int> Unregistered { get; } = [];
        public int? FailRegistrationId { get; init; }
        public int FailureCode { get; init; }

        public bool Register(nint windowHandle, int id, uint modifiers, uint virtualKey, out int errorCode)
        {
            if (id == FailRegistrationId)
            {
                errorCode = FailureCode;
                return false;
            }

            Registered.Add(id, (modifiers, virtualKey));
            errorCode = 0;
            return true;
        }

        public bool Unregister(nint windowHandle, int id, out int errorCode)
        {
            Unregistered.Add(id);
            Registered.Remove(id);
            errorCode = 0;
            return true;
        }
    }

    private sealed class FakeKeyboardHookNativeApi : IKeyboardHookNativeApi
    {
        public Func<RawKeyboardEvent, bool>? Callback { get; private set; }
        public int UninstallCount { get; private set; }

        public IDisposable Install(Func<RawKeyboardEvent, bool> callback)
        {
            Callback = callback;
            return new ActionDisposable(() => UninstallCount++);
        }
    }

    private sealed class ActionDisposable(Action action) : IDisposable
    {
        private Action? action = action;

        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
