using Keyina.Host.Core.Ipc;
using Keyina.Host.Runtime;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class FocusedUnicodeEnvelopeWriterTests
{
    [KeyinaTest("focused Unicode writer delivers multiple final segments to unchanged focus")]
    private static void DeliversMultipleSegmentsToUnchangedFocus()
    {
        var context = new VietnameseTypingContext(42, (nint)100, ShouldBypassTyping: false);
        var inserted = new List<string>();
        var writer = new FocusedUnicodeEnvelopeWriter(() => context, inserted.Add);

        AssertEx.True(writer.TryBindToCurrentFocus(), "Writer did not bind ordinary text focus.");
        var envelope = new IpcEnvelope(
            IpcMessageType.FinalTranscript,
            Flags: 0,
            writer.SessionId,
            writer.FocusGeneration,
            "xin chào");

        writer.WriteAsync(envelope, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

        writer.WriteAsync(
                envelope with { Payload = " thế giới" },
                CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

        AssertEx.True(inserted.SequenceEqual(["xin chào", " thế giới"]),
            "Focused writer did not preserve multiple final segments in one session.");
    }

    [KeyinaTest("focused Unicode writer rejects stale session and focus generation")]
    private static void RejectsStaleEnvelope()
    {
        var context = new VietnameseTypingContext(42, (nint)100, ShouldBypassTyping: false);
        var insertCount = 0;
        var writer = new FocusedUnicodeEnvelopeWriter(
            () => context,
            _ => insertCount++);
        AssertEx.True(
            writer.TryBindToCurrentFocus(),
            "Writer did not bind the initial focus target.");

        AssertThrows<FocusedUnicodeDeliveryException>(() =>
            writer.WriteAsync(
                    new IpcEnvelope(
                        IpcMessageType.FinalTranscript,
                        0,
                        IpcSessionId.New(),
                        writer.FocusGeneration,
                        "text"),
                    CancellationToken.None)
                .AsTask().GetAwaiter().GetResult());
        AssertThrows<FocusedUnicodeDeliveryException>(() =>
            writer.WriteAsync(
                    new IpcEnvelope(
                        IpcMessageType.FinalTranscript,
                        0,
                        writer.SessionId,
                        writer.FocusGeneration + 1,
                        "text"),
                    CancellationToken.None)
                .AsTask().GetAwaiter().GetResult());
        AssertEx.Equal(0, insertCount);
    }

    [KeyinaTest("focused Unicode writer fails open after PID or focused control changes")]
    private static void RejectsFocusChanges()
    {
        var context = new VietnameseTypingContext(42, (nint)100, ShouldBypassTyping: false);
        var insertCount = 0;
        var writer = new FocusedUnicodeEnvelopeWriter(
            () => context,
            _ => insertCount++);
        AssertEx.True(
            writer.TryBindToCurrentFocus(),
            "Writer did not bind the focus-change target.");
        var envelope = new IpcEnvelope(
            IpcMessageType.FinalTranscript,
            0,
            writer.SessionId,
            writer.FocusGeneration,
            "text");

        context = context with { FocusWindow = (nint)101 };
        AssertThrows<FocusedUnicodeDeliveryException>(() =>
            writer.WriteAsync(envelope, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult());
        AssertEx.Equal(0, insertCount);

        context = new VietnameseTypingContext(42, (nint)100, ShouldBypassTyping: false);
        AssertEx.True(
            writer.TryBindToCurrentFocus(),
            "Writer did not rebind the PID-change target.");
        envelope = envelope with
        {
            SessionId = writer.SessionId,
            FocusGeneration = writer.FocusGeneration,
        };
        context = context with { ForegroundProcessId = 43 };
        AssertThrows<FocusedUnicodeDeliveryException>(() =>
            writer.WriteAsync(envelope, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult());
        AssertEx.Equal(0, insertCount);
    }

    [KeyinaTest("focused Unicode writer refuses secure or unavailable focus")]
    private static void RefusesUnsafeTargets()
    {
        var context = new VietnameseTypingContext(0, 0, ShouldBypassTyping: true);
        var writer = new FocusedUnicodeEnvelopeWriter(() => context, _ => { });

        AssertEx.False(writer.TryBindToCurrentFocus(), "Writer bound unavailable focus.");

        context = new VietnameseTypingContext(42, (nint)100, ShouldBypassTyping: true);
        AssertEx.False(writer.TryBindToCurrentFocus(), "Writer bound secure input.");
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

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
