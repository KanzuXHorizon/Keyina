using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class NativeHookBackendTests
{
    [KeyinaTest("native hook engine composes Telex without TSF")]
    private static void NativeEngineComposesVietnamese()
    {
        using var engine = new NativeEngineClient();
        var visible = string.Empty;
        foreach (var character in "tieengs")
        {
            var edit = engine.Process(
                NativeEngineKeyKind.Character,
                new Rune(character));
            AssertEx.True(edit.ConsumePhysicalKey, "Engine did not consume a Telex character.");
            visible = Apply(visible, edit);
        }
        AssertEx.Equal("tiếng", visible);

        var space = engine.Process(
            NativeEngineKeyKind.CommitBoundary,
            new Rune(' '));
        visible = space.ConsumePhysicalKey
            ? Apply(visible, space)
            : visible + " ";
        AssertEx.Equal("tiếng ", visible);

        foreach (var character in "Vieetj")
        {
            visible = Apply(
                visible,
                engine.Process(
                    NativeEngineKeyKind.Character,
                    new Rune(character)));
        }
        AssertEx.Equal("tiếng Việt", visible);
    }

    [KeyinaTest("native hook engine never rewrites visible text at a boundary")]
    private static void NativeEngineKeepsVisibleTextAtBoundary()
    {
        using var engine = new NativeEngineClient();
        engine.Configure(restoreInvalidWord: true);

        var visible = string.Empty;
        foreach (var character in "haahhaahhaahh")
        {
            visible = Apply(
                visible,
                engine.Process(NativeEngineKeyKind.Character, new Rune(character)));
        }
        var beforeBoundary = visible;
        AssertEx.True(
            beforeBoundary.Length > 0,
            "The engine produced no visible composition before the boundary.");

        var boundary = engine.Process(
            NativeEngineKeyKind.CommitBoundary,
            new Rune(' '));
        AssertEx.False(
            boundary.ConsumePhysicalKey,
            "A commit boundary must remain owned by the target application.");
        AssertEx.Equal(beforeBoundary + " ", visible + " ");
    }

    [KeyinaTest("unicode injector emits minimal backspaces and UTF16 events")]
    private static void UnicodeInjectorBuildsMinimalSequence()
    {
        var sender = new RecordingInputSender();
        var injector = new UnicodeInputInjector(sender);

        injector.Apply(new HookEdit(2, "ệ", ConsumePhysicalKey: true));

        AssertEx.Equal(6, sender.Events.Count);
        AssertEx.Equal((ushort)0x08, sender.Events[0].VirtualKey);
        AssertEx.Equal((ushort)0x08, sender.Events[2].VirtualKey);
        AssertEx.Equal((ushort)'ệ', sender.Events[4].ScanCode);
        AssertEx.True(
            sender.Events.All(item =>
                item.ExtraInfo == UnicodeInputInjector.InjectionMarker),
            "Injected events were not marked for hook bypass.");
    }

    private static string Apply(string current, HookEdit edit)
    {
        var prefixLength = current.Length;
        for (var index = 0; index < edit.BackspaceCount; index++)
        {
            prefixLength = PreviousRuneStart(current, prefixLength);
        }
        return string.Concat(current.AsSpan(0, prefixLength), edit.InsertText);
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

    private sealed class RecordingInputSender : UnicodeInputInjector.IInputSender
    {
        public List<UnicodeInputInjector.KeyboardInputEvent> Events { get; } = [];

        public void Send(
            ReadOnlySpan<UnicodeInputInjector.KeyboardInputEvent> inputs)
        {
            foreach (var input in inputs)
            {
                Events.Add(input);
            }
        }
    }
}
