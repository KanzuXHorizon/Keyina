using Keyina.Host.Core.Ipc;
using Keyina.Host.Runtime;
using Keyina.Host.Windows.Ipc;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class ActivePipeEnvelopeWriterTests
{
    [KeyinaTest("speech final text falls back to the focused application when TSF is unavailable")]
    private static void SpeechFallsBackToFocusedApplication() => Run(async () =>
    {
        var pipeName = $"Keyina.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeEnvelopeServer(pipeName);
        await server.StartAsync(CancellationToken.None);
        var injector = new RecordingInjector();
        var context = new VietnameseTypingContext(42, (nint)420, ShouldBypassTyping: false);
        var writer = new ActivePipeEnvelopeWriter(server, injector, () => context);

        var target = await writer.CaptureTargetAsync(TimeSpan.Zero, CancellationToken.None);
        AssertEx.NotNull(target, "A safe foreground text target was not captured.");

        await writer.WriteAsync(
            new IpcEnvelope(
                IpcMessageType.FinalTranscript,
                0,
                target!.SessionId,
                target.FocusGeneration,
                "xin chào"),
            CancellationToken.None);

        AssertEx.Equal(1, injector.Edits.Count);
        AssertEx.Equal("xin chào", injector.Edits[0].InsertText);
        AssertEx.Equal(0, injector.Edits[0].BackspaceCount);
    });

    [KeyinaTest("speech fallback refuses to insert after the focused application changes")]
    private static void SpeechFallbackRejectsFocusChange() => Run(async () =>
    {
        var pipeName = $"Keyina.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeEnvelopeServer(pipeName);
        await server.StartAsync(CancellationToken.None);
        var injector = new RecordingInjector();
        var context = new VietnameseTypingContext(42, (nint)420, ShouldBypassTyping: false);
        var writer = new ActivePipeEnvelopeWriter(server, injector, () => context);
        var target = await writer.CaptureTargetAsync(TimeSpan.Zero, CancellationToken.None);
        AssertEx.NotNull(target, "A safe foreground text target was not captured.");

        context = new VietnameseTypingContext(42, (nint)421, ShouldBypassTyping: false);
        Exception? failure = null;
        try
        {
            await writer.WriteAsync(
                new IpcEnvelope(
                    IpcMessageType.FinalTranscript,
                    0,
                    target!.SessionId,
                    target.FocusGeneration,
                    "không được chèn"),
                CancellationToken.None);
        }
        catch (IpcDeliveryException exception)
        {
            failure = exception;
        }

        AssertEx.NotNull(failure, "Focus change did not invalidate the fallback target.");
        AssertEx.Equal(0, injector.Edits.Count);
    });

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();

    private sealed class RecordingInjector : IUnicodeInputInjector
    {
        public List<HookEdit> Edits { get; } = [];

        public void Apply(HookEdit edit) => Edits.Add(edit);
    }
}
