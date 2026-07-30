using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class VietnameseKeyboardHookTests
{
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

    [KeyinaTest("resident hook ignores injected edits and resets on mouse interaction")]
    private static void HookAvoidsLoopsAndResetsOnMouse()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "tieengs");
        AssertEx.Equal("tiếng", injector.Text);

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

        native.MouseResetCallback!();
        injector.Text += " ";
        Type(native, "as");
        AssertEx.Equal("tiếng á", injector.Text);
    }

    [KeyinaTest("resident hook restores invalid Telex before punctuation boundaries")]
    private static void HookRestoresBeforePunctuation()
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

        AssertEx.Equal("haahhaahhaahh.", injector.Text);
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

    [KeyinaTest("resident hook lets Backspace delete visible text without undoing Telex history")]
    private static void BackspaceIsOrdinaryDeletion()
    {
        var native = new FakeHookNativeApi();
        var injector = new TextModelInjector();
        native.Target = injector;
        using var hook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            injector,
            native);
        hook.Start(enabledInitially: true);

        Type(native, "as");
        AssertEx.Equal("á", injector.Text);
        AssertEx.False(native.SendBackspace(), "Backspace was swallowed by Keyina.");
        injector.Text = string.Empty;
        Type(native, "s");
        AssertEx.Equal("s", injector.Text);
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

        public void Apply(HookEdit edit)
        {
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
        public Action? MouseResetCallback { get; private set; }
        public int ForegroundProcessId { get; set; } = 100;
        public TextModelInjector? Target { get; set; }

        public IDisposable Install(Func<VietnameseKeyboardEvent, bool> callback)
        {
            KeyboardCallback = callback;
            return new DelegateDisposable(() => KeyboardCallback = null);
        }

        public IDisposable InstallMouseReset(Action callback)
        {
            MouseResetCallback = callback;
            return new DelegateDisposable(() => MouseResetCallback = null);
        }

        public int GetForegroundProcessId() => ForegroundProcessId;

        public bool ShouldBypassTyping() => false;

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

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? action = dispose;

        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
