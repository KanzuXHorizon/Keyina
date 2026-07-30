using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Windows.Hotkeys;
using Keyina.Host.Windows.Typing;

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
        AssertEx.Equal(4, native.Registered.Count);
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
        AssertEx.Equal(4, native.Unregistered.Count);
    }

    [KeyinaTest("optional hotkey conflict preserves existing registrations")]
    private static void OptionalConflictPreservesExistingRegistrations()
    {
        var native = new FakeHotkeyNativeApi { FailRegistrationId = 4, FailureCode = 1409 };
        var commands = new List<HotkeyCommand>();
        using var manager = new RegisteredHotkeyManager(native, windowHandle: 456);
        manager.CommandReceived += (_, command) => commands.Add(command);
        manager.Register(
        [
            new RegisteredHotkeyBinding(
                1,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.Space),
                HotkeyCommand.PushToTalkPressed),
            new RegisteredHotkeyBinding(
                2,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.V),
                HotkeyCommand.ToggleDictation),
            new RegisteredHotkeyBinding(
                3,
                new HotkeyChord(HotkeyModifiers.None, VirtualKey.Escape),
                HotkeyCommand.CancelDictation),
        ]);

        var registered = manager.TryRegister(
            new RegisteredHotkeyBinding(
                4,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.T),
                HotkeyCommand.TranslateSelection),
            out var failure);

        AssertEx.False(registered, "Conflicting optional hotkey unexpectedly registered.");
        AssertEx.NotNull(failure, "Optional conflict did not expose registration details.");
        AssertEx.Equal(4, failure!.HotkeyId);
        AssertEx.Equal(3, manager.RegisteredCount);
        AssertEx.Equal(0, native.Unregistered.Count);
        AssertEx.True(manager.TryDispatch(2), "Existing hotkey stopped dispatching after optional conflict.");
        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleDictation]),
            "Optional conflict changed an existing command binding.");
    }

    [KeyinaTest("optional hotkey can be released without affecting existing registrations")]
    private static void OptionalHotkeyCanBeReleased()
    {
        var native = new FakeHotkeyNativeApi();
        var commands = new List<HotkeyCommand>();
        using var manager = new RegisteredHotkeyManager(native, windowHandle: 456);
        manager.CommandReceived += (_, command) => commands.Add(command);
        manager.Register(
        [
            new RegisteredHotkeyBinding(
                1,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.Space),
                HotkeyCommand.PushToTalkPressed),
            new RegisteredHotkeyBinding(
                2,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.V),
                HotkeyCommand.ToggleDictation),
            new RegisteredHotkeyBinding(
                3,
                new HotkeyChord(HotkeyModifiers.None, VirtualKey.Escape),
                HotkeyCommand.CancelDictation),
        ]);
        AssertEx.True(
            manager.TryRegister(
                new RegisteredHotkeyBinding(
                    4,
                    new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.T),
                    HotkeyCommand.TranslateSelection),
                out _),
            "Optional translation hotkey was not registered.");

        var released = manager.TryUnregister(4, out var failureCode);

        AssertEx.True(released, "Optional translation hotkey was not released.");
        AssertEx.Equal(0, failureCode);
        AssertEx.Equal(3, manager.RegisteredCount);
        AssertEx.True(native.Unregistered.SequenceEqual([4]),
            "Optional hotkey release unregistered an unexpected binding.");
        AssertEx.True(manager.TryDispatch(2),
            "Existing hotkey stopped dispatching after optional release.");
        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleDictation]),
            "Optional hotkey release changed an existing command binding.");
    }

    [KeyinaTest("registered hotkey replacement swaps the complete binding set")]
    private static void ReplacementSwapsCompleteSet()
    {
        var native = new FakeHotkeyNativeApi();
        var commands = new List<HotkeyCommand>();
        using var manager = new RegisteredHotkeyManager(native, windowHandle: 456);
        manager.CommandReceived += (_, command) => commands.Add(command);
        manager.Register(CreateRegisteredBindings());
        var replacement = new[]
        {
            new RegisteredHotkeyBinding(
                1,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey.K),
                HotkeyCommand.PushToTalkPressed),
            new RegisteredHotkeyBinding(
                2,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey.D),
                HotkeyCommand.ToggleDictation),
            new RegisteredHotkeyBinding(
                3,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey.Escape),
                HotkeyCommand.CancelDictation),
            new RegisteredHotkeyBinding(
                4,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKey.T),
                HotkeyCommand.TranslateSelection),
        };

        var replaced = manager.TryReplaceAll(replacement, out var failure);

        AssertEx.True(replaced, "Complete hotkey replacement failed.");
        AssertEx.Equal(null, failure);
        AssertEx.Equal(4, manager.RegisteredCount);
        AssertEx.True(native.Unregistered.Order().SequenceEqual([1, 2, 3, 4]),
            "Old registered set was not fully released.");
        AssertEx.True(manager.TryDispatch(2), "Replacement binding did not dispatch.");
        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleDictation]),
            "Replacement dispatched the wrong command.");
        AssertEx.Equal((uint)VirtualKey.D, native.Registered[2].Key);
    }

    [KeyinaTest("registered hotkey replacement restores the previous set after conflict")]
    private static void ReplacementConflictRestoresPreviousSet()
    {
        var native = new FakeHotkeyNativeApi();
        var commands = new List<HotkeyCommand>();
        using var manager = new RegisteredHotkeyManager(native, windowHandle: 456);
        manager.CommandReceived += (_, command) => commands.Add(command);
        var original = CreateRegisteredBindings();
        manager.Register(original);
        native.FailNextRegistrationId = 2;
        var replacement = original
            .Select(binding => binding with
            {
                Chord = binding.Chord with
                {
                    Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
                },
            })
            .ToArray();

        var replaced = manager.TryReplaceAll(replacement, out var failure);

        AssertEx.False(replaced, "Conflicting replacement unexpectedly succeeded.");
        AssertEx.NotNull(failure, "Replacement conflict did not expose failure details.");
        AssertEx.Equal(2, failure!.HotkeyId);
        AssertEx.Equal(original.Length, manager.RegisteredCount);
        AssertEx.Equal((uint)VirtualKey.V, native.Registered[2].Key);
        AssertEx.Equal(0x4003u, native.Registered[2].Modifiers);
        AssertEx.True(manager.TryDispatch(2), "Previous binding was not restored.");
        AssertEx.True(commands.SequenceEqual([HotkeyCommand.ToggleDictation]),
            "Restored binding dispatched the wrong command.");
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

    [KeyinaTest("modifier keyboard hook shares the resident typing hook without a second native hook")]
    private static void ModifierHookSharesResidentTypingHook()
    {
        var typingNative = new FakeVietnameseKeyboardHookNativeApi();
        using var typingHook = new VietnameseKeyboardHook(nativeApi: typingNative);
        using var modifierHook = new ModifierKeyboardHook(
            new SharedTypingKeyboardHookNativeApi(typingHook));
        var commands = new List<HotkeyCommand>();
        modifierHook.CommandReceived += (_, command) => commands.Add(command);

        modifierHook.Start();
        typingHook.Start(enabledInitially: false);
        AssertEx.Equal(1, typingNative.InstallCount);

        _ = typingNative.Callback!(CreateTypingEvent(VirtualKey.LeftControl, true));
        _ = typingNative.Callback(CreateTypingEvent(VirtualKey.LeftShift, true));
        _ = typingNative.Callback(CreateTypingEvent(VirtualKey.LeftShift, false));
        _ = typingNative.Callback(CreateTypingEvent(VirtualKey.LeftControl, false));

        AssertEx.True(
            commands.SequenceEqual([HotkeyCommand.ToggleVietnamese]),
            "The shared modifier processor did not emit exactly one toggle.");
    }

    [KeyinaTest("modifier keyboard hook emits one push-to-talk release without swallowing input")]
    private static void ModifierHookEmitsPushToTalkRelease()
    {
        var native = new FakeKeyboardHookNativeApi();
        var commands = new List<HotkeyCommand>();
        using var hook = new ModifierKeyboardHook(native);
        hook.CommandReceived += (_, command) => commands.Add(command);
        hook.Start();

        _ = native.Callback!(new RawKeyboardEvent(VirtualKey.LeftControl, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftAlt, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.Space, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.Space, true, false));
        AssertEx.Equal(0, commands.Count);

        AssertEx.True(
            !native.Callback(new RawKeyboardEvent(VirtualKey.Space, false, false)),
            "Push-to-talk release was swallowed.");
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftAlt, false, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftControl, false, false));
        AssertEx.True(
            commands.SequenceEqual([HotkeyCommand.PushToTalkReleased]),
            "Push-to-talk release was not emitted exactly once.");
    }

    [KeyinaTest("modifier keyboard hook releases push-to-talk when a chord modifier is released first")]
    private static void ModifierHookReleasesOnModifierUp()
    {
        var native = new FakeKeyboardHookNativeApi();
        var commands = new List<HotkeyCommand>();
        using var hook = new ModifierKeyboardHook(native);
        hook.CommandReceived += (_, command) => commands.Add(command);
        hook.Start();

        _ = native.Callback!(new RawKeyboardEvent(VirtualKey.LeftControl, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftAlt, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.Space, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftAlt, false, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.Space, false, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftControl, false, false));

        AssertEx.True(
            commands.SequenceEqual([HotkeyCommand.PushToTalkReleased]),
            "Modifier-first release did not emit exactly one stop command.");
    }

    [KeyinaTest("modifier keyboard hook applies custom modifier and hold gestures")]
    private static void ModifierHookAppliesCustomGestures()
    {
        var native = new FakeKeyboardHookNativeApi();
        var commands = new List<HotkeyCommand>();
        using var hook = new ModifierKeyboardHook(native);
        hook.CommandReceived += (_, command) => commands.Add(command);
        hook.Configure(
            new HotkeyPreference(
                HotkeyGestureKind.ModifierGesture,
                new HotkeyChord(
                    HotkeyModifiers.Alt | HotkeyModifiers.Shift,
                    VirtualKey.None)),
            new HotkeyPreference(
                HotkeyGestureKind.Hold,
                new HotkeyChord(
                    HotkeyModifiers.Control | HotkeyModifiers.Shift,
                    VirtualKey.K)));
        hook.Start();

        _ = native.Callback!(new RawKeyboardEvent(VirtualKey.LeftAlt, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, false, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftAlt, false, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftControl, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.K, true, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.K, false, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftShift, false, false));
        _ = native.Callback(new RawKeyboardEvent(VirtualKey.LeftControl, false, false));

        AssertEx.True(
            commands.SequenceEqual(
            [
                HotkeyCommand.ToggleVietnamese,
                HotkeyCommand.PushToTalkReleased,
            ]),
            "Custom modifier or hold gesture was not applied.");
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

    private static VietnameseKeyboardEvent CreateTypingEvent(
        VirtualKey key,
        bool isKeyDown) => new(
        VirtualKey: (int)key,
        IsKeyDown: isKeyDown,
        IsInjected: false,
        ExtraInfo: 0,
        Shift: false,
        Control: false,
        Alt: false,
        Windows: false,
        Character: default);

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
        public int? FailNextRegistrationId { get; set; }
        public int FailureCode { get; init; }

        public bool Register(nint windowHandle, int id, uint modifiers, uint virtualKey, out int errorCode)
        {
            if (id == FailRegistrationId || id == FailNextRegistrationId)
            {
                if (id == FailNextRegistrationId)
                {
                    FailNextRegistrationId = null;
                }
                errorCode = FailureCode == 0 ? 1409 : FailureCode;
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

    private sealed class FakeVietnameseKeyboardHookNativeApi :
        IVietnameseKeyboardHookNativeApi
    {
        public Func<VietnameseKeyboardEvent, bool>? Callback { get; private set; }
        public int InstallCount { get; private set; }

        public IDisposable Install(Func<VietnameseKeyboardEvent, bool> keyboardCallback)
        {
            Callback = keyboardCallback;
            InstallCount++;
            return new ActionDisposable(() => Callback = null);
        }

        public IDisposable InstallPointerReset(Action pointerResetCallback) =>
            new ActionDisposable(static () => { });

        public VietnameseTypingContext GetTypingContext() =>
            new(ForegroundProcessId: 1, FocusWindow: 1, ShouldBypassTyping: false);
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
