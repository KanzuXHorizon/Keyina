using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Speech;

namespace Keyina.Host.Tests;

internal static class TranscriptAggregatorTests
{
    [KeyinaTest("partial transcripts replace overlay text without producing IPC")]
    private static void PartialsReplaceOverlayOnly()
    {
        var aggregator = new TranscriptAggregator();
        var session = new IpcSessionId(1, 2);

        var first = aggregator.Apply(Partial("xin"), session, 7);
        AssertEx.Equal("xin", first.PartialText);
        AssertEx.Equal<IpcEnvelope?>(null, first.FinalEnvelope);

        var revised = aggregator.Apply(Partial("xin chao"), session, 7);
        AssertEx.Equal("xin chao", revised.PartialText);
        AssertEx.Equal<IpcEnvelope?>(null, revised.FinalEnvelope);
    }

    [KeyinaTest("final transcript clears partial and creates one focused IPC envelope")]
    private static void FinalCreatesOneEnvelope()
    {
        var aggregator = new TranscriptAggregator();
        var session = new IpcSessionId(11, 22);
        aggregator.Apply(Partial("xin chao"), session, 9);

        var update = aggregator.Apply(Final("xin chào", 0.0, 0.8), session, 9);
        AssertEx.Equal("", update.PartialText);
        AssertEx.NotNull(update.FinalEnvelope, "Final transcript did not create IPC.");
        AssertEx.Equal(IpcMessageType.FinalTranscript, update.FinalEnvelope!.MessageType);
        AssertEx.Equal(session, update.FinalEnvelope.SessionId);
        AssertEx.Equal<ulong>(9, update.FinalEnvelope.FocusGeneration);
        AssertEx.Equal("xin chào", update.FinalEnvelope.Payload);
        AssertEx.Equal(1, update.FinalOrdinal);
    }

    [KeyinaTest("duplicate provider finals are ignored but distinct segments remain ordered")]
    private static void FinalsAreDeduplicatedAndOrdered()
    {
        var aggregator = new TranscriptAggregator();
        var session = new IpcSessionId(3, 4);
        var first = Final("xin chào", 0.0, 0.8);

        var firstUpdate = aggregator.Apply(first, session, 1);
        var duplicate = aggregator.Apply(first, session, 1);
        var second = aggregator.Apply(Final("mọi người", 0.8, 1.4), session, 1);

        AssertEx.NotNull(firstUpdate.FinalEnvelope, "First final was lost.");
        AssertEx.Equal<IpcEnvelope?>(null, duplicate.FinalEnvelope);
        AssertEx.NotNull(second.FinalEnvelope, "Second final was lost.");
        AssertEx.Equal(2, second.FinalOrdinal);
        AssertEx.Equal("xin chào mọi người", aggregator.CommittedText);
    }

    [KeyinaTest("aggregator rejects non transcript events and empty final text")]
    private static void InvalidEventsAreRejected()
    {
        var aggregator = new TranscriptAggregator();
        var session = new IpcSessionId(5, 6);

        AssertThrows<ArgumentException>(() => aggregator.Apply(
            new TranscriptEvent(TranscriptEventKind.Unknown, null, null, null),
            session,
            0));
        AssertThrows<ArgumentException>(() => aggregator.Apply(
            Final("", 0.0, 0.1),
            session,
            0));
    }

    private static TranscriptEvent Partial(string text) =>
        new(TranscriptEventKind.Partial, text, 0, 0.5);

    private static TranscriptEvent Final(string text, double start, double end) =>
        new(TranscriptEventKind.Final, text, start, end);

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
