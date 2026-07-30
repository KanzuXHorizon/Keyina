using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class VietnameseKeyboardHookTests
{
    [KeyinaTest("resident hook releases the keyboard hook when pointer observation cannot start")]
    private static void HookCleansUpPartialStartup()
    {
        var native = new FakeHookNativeApi { ThrowOnPointerInstall = true };
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            new TextModelInjector(),
            native);

        Exception? failure = null;
        try
        {
            hook.Start(enabledInitially: true);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        AssertEx.NotNull(failure, "Pointer startup failure was not propagated.");
        AssertEx.True(
            native.KeyboardInstallationDisposed,
            "The keyboard hook remained installed after pointer startup failed.");
        AssertEx.False(hook.IsRunning, "A partially started hook reported itself as running.");
    }

    [KeyinaTest("resident hook composes Telex without a Windows language profile")]
    private static void HookComposesVietnamese()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "tieengs Vieetj");

        AssertEx.Equal("tiếng Việt", injector.Text);
    }

    [KeyinaTest("resident hook supports literal tone-key escape and flexible late vowel shape")]
    private static void HookHandlesLiteralUxAndLateShapeToneOrder()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "uxx loixo ");

        AssertEx.Equal("ux lỗi ", injector.Text);
    }

    [KeyinaTest("resident hook restores Latin words without requiring manual engine configuration")]
    private static void HookRestoresLatinWordsByDefault()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "user research tele fix hardcode harrdcode guitarrist ");

        AssertEx.Equal(
            "user research tele fix hardcode hardcode guitarist ",
            injector.Text);
    }

    [KeyinaTest("resident hook isolates physical-event observer failures from Vietnamese typing")]
    private static void PhysicalObserverFailuresAreIsolated()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        using var subscription = hook.SubscribePhysicalEvents(
            static _ => throw new InvalidOperationException("observer failure"));
        hook.Start(enabledInitially: true);

        Type(native, "as");

        AssertEx.Equal("á", injector.Text);
    }

    [KeyinaTest("disabled resident hook bypasses foreground probing")]
    private static void DisabledHookBypassesForegroundProbe()
    {
        var native = new FakeHookNativeApi();
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            new TextModelInjector(),
            native);
        hook.Start(enabledInitially: false);
        var probesAfterStartup = native.TypingContextProbeCount;

        _ = native.SendLetter('A', 'a');

        AssertEx.Equal(
            probesAfterStartup,
            native.TypingContextProbeCount,
            "Disabled typing performed foreground Win32 work for an ordinary key.");
    }

    [KeyinaTest("resident hook bypasses excluded applications without swallowing input")]
    private static void ExcludedApplicationBypassesTyping()
    {
        var native = new FakeHookNativeApi { ForegroundProcessId = 742 };
        var injector = new TextModelInjector();
        native.Target = injector;
        var observedProcessIds = new List<int>();
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native,
            processId =>
            {
                observedProcessIds.Add(processId);
                return processId == 742;
            });
        hook.Start(enabledInitially: true);

        var handled = native.SendLetter('A', 'a');

        AssertEx.False(handled, "Excluded application input was swallowed.");
        AssertEx.Equal(string.Empty, injector.Text);
        AssertEx.True(observedProcessIds.Contains(742),
            "Typing exclusion did not receive the foreground process id.");
    }

    [KeyinaTest("Keyina injected events bypass physical hotkey observers")]
    private static void InjectedEventsBypassPhysicalObservers()
    {
        var native = new FakeHookNativeApi();
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            new TextModelInjector(),
            native);
        var observed = 0;
        using var subscription = hook.SubscribePhysicalEvents(_ => observed++);
        hook.Start(enabledInitially: true);

        var handled = native.KeyboardCallback!(new VietnameseKeyboardEvent(
            VirtualKey: 'A',
            IsKeyDown: true,
            IsInjected: true,
            UnicodeInputInjector.InjectionMarker,
            Shift: false,
            Control: false,
            Alt: false,
            Windows: false,
            new Rune('a')));

        AssertEx.False(handled, "Keyina injected events must fail open.");
        AssertEx.Equal(0, observed, "Injected edits reached the physical hotkey observer.");
    }

    [KeyinaTest("pointer observation is active only while a composition exists")]
    private static void PointerObservationFollowsCompositionLifetime()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        AssertEx.False(
            native.PointerObservationActive,
            "Pointer observation started before a composition existed.");
        _ = native.SendLetter('A', 'a');
        AssertEx.True(
            native.PointerObservationActive,
            "Typing the first composition character did not arm pointer observation.");

        var injectionsBeforePointer = injector.ApplyCount;
        native.PointerResetCallback!();
        AssertEx.False(
            native.PointerObservationActive,
            "A pointer reset left raw mouse observation armed.");
        AssertEx.Equal(
            injectionsBeforePointer,
            injector.ApplyCount,
            "A pointer reset injected keyboard or pointer input.");

        _ = native.SendLetter('S', 's');
        AssertEx.True(
            native.PointerObservationActive,
            "A new composition did not re-arm pointer observation.");
        _ = native.SendSpace();
        AssertEx.False(
            native.PointerObservationActive,
            "A commit boundary left pointer observation active.");
    }

    [KeyinaTest("resident hook ignores injected edits and resets on asynchronous pointer interaction")]
    private static void HookAvoidsLoopsAndResetsOnPointerInteraction()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "a");
        AssertEx.Equal("a", injector.Text);

        var injectedWasSuppressed = native.KeyboardCallback!(new VietnameseKeyboardEvent(
            VirtualKey: 'A',
            IsKeyDown: true,
            IsInjected: true,
            UnicodeInputInjector.InjectionMarker,
            Shift: false,
            Control: false,
            Alt: false,
            Windows: false,
            new Rune('a')));
        AssertEx.False(injectedWasSuppressed, "Injected Keyina events must bypass the hook.");

        native.PointerResetCallback!();
        Type(native, "s");
        AssertEx.Equal("as", injector.Text);
    }

    [KeyinaTest("resident hook resets when the focused control changes")]
    private static void HookResetsWhenFocusedControlChanges()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "a");
        native.TypingContext = native.TypingContext with { FocusWindow = (nint)2 };
        Type(native, "s");

        AssertEx.Equal("as", injector.Text);
    }

    [KeyinaTest("resident hook keeps visible text stable at punctuation boundaries")]
    private static void HookKeepsVisibleTextAtPunctuation()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var engine = new NativeEngineClient();
        engine.Configure(restoreInvalidWord: true);
        using var hook = new VietnameseKeyboardHook(engine, injector, native);
        hook.Start(enabledInitially: true);

        Type(native, "haahhaahhaahh");
        AssertEx.Equal("hâhhâhhâhh", injector.Text);
        var handled = native.SendCharacter('.', '.');
        if (!handled)
        {
            injector.Text += ".";
        }

        AssertEx.Equal("hâhhâhhâhh.", injector.Text);
    }

    [KeyinaTest("resident hook preserves Caps Lock casing while applying Telex")]
    private static void CapsLockCasingIsStable()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        foreach (var character in "TIEENGS")
        {
            var handled = native.SendLetter(character, character, shift: false);
            if (!handled)
            {
                injector.Text += character;
            }
        }

        AssertEx.Equal("TIẾNG", injector.Text);
    }

    [KeyinaTest("resident hook reconstructs Telex state after Backspace")]
    private static void BackspaceReconstructsComposition()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "nguyenx");
        AssertEx.Equal("nguyẽn", injector.Text);
        AssertEx.True(native.SendBackspace(), "Keyina did not own composition Backspace.");
        AssertEx.Equal("nguyen", injector.Text);
        Type(native, "e");
        AssertEx.Equal("nguyên", injector.Text);
    }

    [KeyinaTest("resident hook fails open for shortcuts disabled mode and focus changes")]
    private static void HookFailsOpenSafely()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: false);

        AssertEx.False(native.SendLetter('A', 'a'), "Disabled hook swallowed a key.");
        hook.SetEnabled(true);
        AssertEx.False(native.SendLetter('C', 'c', control: true), "Ctrl+C was swallowed.");
        AssertEx.False(native.SendLetter('V', 'v', control: true), "Ctrl+V was swallowed.");

        Type(native, "as");
        AssertEx.Equal("á", injector.Text);
        native.ForegroundProcessId++;
        injector.Text += " ";
        Type(native, "as");
        AssertEx.Equal("á á", injector.Text);
    }

    [KeyinaTest("resident hook profiles literal and transformed typing stages")]
    private static void HookProfilesTypingStages()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);
        TypingLatencyProfiler.Clear();
        TypingLatencyProfiler.SetEnabled(true);
        try
        {
            _ = native.SendLetter('B', 'b');

            AssertEx.Equal(1L, Samples(TypingLatencyStage.CallbackTotal));
            AssertEx.Equal(1L, Samples(TypingLatencyStage.ForegroundContext));
            AssertEx.Equal(1L, Samples(TypingLatencyStage.SafetyGuard));
            AssertEx.Equal(1L, Samples(TypingLatencyStage.EngineProcess));
            AssertEx.Equal(0L, Samples(TypingLatencyStage.InputInjection));

            TypingLatencyProfiler.Clear();
            _ = native.SendLetter('A', 'a');
            _ = native.SendLetter('S', 's');

            AssertEx.Equal(2L, Samples(TypingLatencyStage.CallbackTotal));
            AssertEx.Equal(2L, Samples(TypingLatencyStage.ForegroundContext));
            AssertEx.Equal(2L, Samples(TypingLatencyStage.SafetyGuard));
            AssertEx.Equal(2L, Samples(TypingLatencyStage.EngineProcess));
            AssertEx.Equal(1L, Samples(TypingLatencyStage.InputInjection));
        }
        finally
        {
            TypingLatencyProfiler.SetEnabled(false);
            TypingLatencyProfiler.Clear();
        }
    }

    [KeyinaTest("resident hook profiling is inert when disabled")]
    private static void HookProfilingIsInertWhenDisabled()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);
        TypingLatencyProfiler.SetEnabled(false);
        TypingLatencyProfiler.Clear();

        _ = native.SendLetter('A', 'a');
        _ = native.SendLetter('S', 's');

        AssertEx.True(
            TypingLatencyProfiler.Snapshot().All(item => item.SampleCount == 0),
            "Disabled profiling must not record hook samples.");
    }

    private static long Samples(TypingLatencyStage stage) =>
        TypingLatencyProfiler.Snapshot().Single(item => item.Stage == stage).SampleCount;

    private static void Type(FakeHookNativeApi native, string text)
    {
        foreach (var character in text)
        {
            if (character == ' ')
            {
                var suppressed = native.SendSpace();
                if (!suppressed)
                {
                    native.Target!.Text += " ";
                }
                continue;
            }

            var upper = char.IsUpper(character);
            var virtualKey = char.ToUpperInvariant(character);
            var handled = native.SendLetter(virtualKey, character, shift: upper);
            if (!handled)
            {
                native.Target!.Text += character;
            }
        }
    }

    private sealed class TextModelInjector : IUnicodeInputInjector
    {
        public string Text { get; set; } = string.Empty;
        public int ApplyCount { get; private set; }

        public void Apply(HookEdit edit)
        {
            ApplyCount++;
            var end = Text.Length;
            for (var index = 0; index < edit.BackspaceCount; index++)
            {
                end = PreviousRuneStart(Text, end);
            }
            Text = string.Concat(Text.AsSpan(0, end), edit.InsertText);
        }

        private static int PreviousRuneStart(string value, int end)
        {
            if (end == 0)
            {
                return 0;
            }
            var index = end - 1;
            if (char.IsLowSurrogate(value[index]) &&
                index > 0 &&
                char.IsHighSurrogate(value[index - 1]))
            {
                index--;
            }
            return index;
        }
    }

    private sealed class FakeHookNativeApi : IVietnameseKeyboardHookNativeApi
    {
        public Func<VietnameseKeyboardEvent, bool>? KeyboardCallback { get; private set; }
        public Action? PointerResetCallback { get; private set; }
        public VietnameseTypingContext TypingContext { get; set; } = new(
            ForegroundProcessId: 100,
            FocusWindow: (nint)1,
            ShouldBypassTyping: false);
        public int ForegroundProcessId
        {
            get => TypingContext.ForegroundProcessId;
            set => TypingContext = TypingContext with { ForegroundProcessId = value };
        }
        public bool ThrowOnPointerInstall { get; set; }
        public bool KeyboardInstallationDisposed { get; private set; }
        public bool PointerObservationActive { get; private set; }
        public int TypingContextProbeCount { get; private set; }
        public TextModelInjector? Target { get; set; }

        public IDisposable Install(Func<VietnameseKeyboardEvent, bool> callback)
        {
            KeyboardCallback = callback;
            return new DelegateDisposable(() =>
            {
                KeyboardCallback = null;
                KeyboardInstallationDisposed = true;
            });
        }

        public IPointerResetLease InstallPointerReset(Action callback)
        {
            if (ThrowOnPointerInstall)
            {
                throw new InvalidOperationException("Pointer observer unavailable.");
            }
            PointerResetCallback = () =>
            {
                PointerObservationActive = false;
                callback();
            };
            return new FakePointerResetLease(
                active => PointerObservationActive = active,
                () => PointerResetCallback = null);
        }

        public VietnameseTypingContext GetTypingContext()
        {
            TypingContextProbeCount++;
            return TypingContext;
        }

        public bool SendCharacter(
            char virtualKey,
            char character,
            bool shift = false,
            bool control = false)
        {
            var callback = KeyboardCallback
                ?? throw new InvalidOperationException("Hook was not installed.");
            var down = callback(new VietnameseKeyboardEvent(
                virtualKey,
                IsKeyDown: true,
                IsInjected: false,
                ExtraInfo: 0,
                shift,
                control,
                Alt: false,
                Windows: false,
                new Rune(character)));
            var up = callback(new VietnameseKeyboardEvent(
                virtualKey,
                IsKeyDown: false,
                IsInjected: false,
                ExtraInfo: 0,
                shift,
                control,
                Alt: false,
                Windows: false,
                new Rune(character)));
            AssertEx.Equal(down, up);
            return down;
        }

        public bool SendLetter(
            char virtualKey,
            char character,
            bool shift = false,
            bool control = false) =>
            SendCharacter(virtualKey, character, shift, control);

        public bool SendBackspace()
        {
            var callback = KeyboardCallback
                ?? throw new InvalidOperationException("Hook was not installed.");
            var down = callback(new VietnameseKeyboardEvent(
                0x08,
                IsKeyDown: true,
                IsInjected: false,
                ExtraInfo: 0,
                Shift: false,
                Control: false,
                Alt: false,
                Windows: false,
                default));
            var up = callback(new VietnameseKeyboardEvent(
                0x08,
                IsKeyDown: false,
                IsInjected: false,
                ExtraInfo: 0,
                Shift: false,
                Control: false,
                Alt: false,
                Windows: false,
                default));
            AssertEx.Equal(down, up);
            return down;
        }

        public bool SendSpace()
        {
            var callback = KeyboardCallback
                ?? throw new InvalidOperationException("Hook was not installed.");
            var down = callback(new VietnameseKeyboardEvent(
                0x20,
                IsKeyDown: true,
                IsInjected: false,
                ExtraInfo: 0,
                Shift: false,
                Control: false,
                Alt: false,
                Windows: false,
                new Rune(' ')));
            var up = callback(new VietnameseKeyboardEvent(
                0x20,
                IsKeyDown: false,
                IsInjected: false,
                ExtraInfo: 0,
                Shift: false,
                Control: false,
                Alt: false,
                Windows: false,
                new Rune(' ')));
            AssertEx.Equal(down, up);
            return down;
        }
    }

    private sealed class FakePointerResetLease(
        Action<bool> setActive,
        Action dispose) : IPointerResetLease
    {
        private Action<bool>? setActiveAction = setActive;
        private Action? disposeAction = dispose;

        public void SetActive(bool active) =>
            Volatile.Read(ref setActiveAction)?.Invoke(active);

        public void Dispose()
        {
            Interlocked.Exchange(ref setActiveAction, null);
            Interlocked.Exchange(ref disposeAction, null)?.Invoke();
        }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? action = dispose;

        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
