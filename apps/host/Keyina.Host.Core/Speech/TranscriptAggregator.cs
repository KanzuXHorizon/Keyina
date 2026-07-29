using System.Text;
using Keyina.Host.Core.Ipc;

namespace Keyina.Host.Core.Speech;

public enum TranscriptEventKind
{
    Unknown,
    Partial,
    Final,
}

public sealed record TranscriptEvent(
    TranscriptEventKind Kind,
    string? Text,
    double? StartTimeSeconds,
    double? EndTimeSeconds);

public sealed record TranscriptUpdate(
    string PartialText,
    IpcEnvelope? FinalEnvelope,
    int FinalOrdinal);

public sealed class TranscriptAggregator
{
    private readonly HashSet<FinalSegmentKey> committedSegments = [];
    private readonly StringBuilder committedText = new();
    private string partialText = string.Empty;
    private int finalOrdinal;

    public string PartialText => partialText;

    public string CommittedText => committedText.ToString();

    public TranscriptUpdate Apply(
        TranscriptEvent transcriptEvent,
        IpcSessionId sessionId,
        ulong focusGeneration)
    {
        ArgumentNullException.ThrowIfNull(transcriptEvent);
        return transcriptEvent.Kind switch
        {
            TranscriptEventKind.Partial => ApplyPartial(transcriptEvent),
            TranscriptEventKind.Final => ApplyFinal(
                transcriptEvent,
                sessionId,
                focusGeneration),
            _ => throw new ArgumentException(
                "Transcript aggregator accepts only partial or final events.",
                nameof(transcriptEvent)),
        };
    }

    public void Reset()
    {
        committedSegments.Clear();
        committedText.Clear();
        partialText = string.Empty;
        finalOrdinal = 0;
    }

    private TranscriptUpdate ApplyPartial(TranscriptEvent transcriptEvent)
    {
        var text = ValidateTextAndTiming(transcriptEvent);
        partialText = text;
        return new TranscriptUpdate(partialText, null, finalOrdinal);
    }

    private TranscriptUpdate ApplyFinal(
        TranscriptEvent transcriptEvent,
        IpcSessionId sessionId,
        ulong focusGeneration)
    {
        var text = ValidateTextAndTiming(transcriptEvent);
        var key = new FinalSegmentKey(
            text,
            BitConverter.DoubleToInt64Bits(transcriptEvent.StartTimeSeconds!.Value),
            BitConverter.DoubleToInt64Bits(transcriptEvent.EndTimeSeconds!.Value));

        partialText = string.Empty;
        if (!committedSegments.Add(key))
        {
            return new TranscriptUpdate(partialText, null, finalOrdinal);
        }

        AppendCommittedText(text);
        finalOrdinal = checked(finalOrdinal + 1);
        var envelope = new IpcEnvelope(
            IpcMessageType.FinalTranscript,
            Flags: 0,
            sessionId,
            focusGeneration,
            text);
        return new TranscriptUpdate(partialText, envelope, finalOrdinal);
    }

    private static string ValidateTextAndTiming(TranscriptEvent transcriptEvent)
    {
        if (string.IsNullOrWhiteSpace(transcriptEvent.Text))
        {
            throw new ArgumentException("Transcript text cannot be empty.", nameof(transcriptEvent));
        }

        if (transcriptEvent.StartTimeSeconds is not double start ||
            transcriptEvent.EndTimeSeconds is not double end ||
            !double.IsFinite(start) ||
            !double.IsFinite(end) ||
            start < 0 ||
            end < start)
        {
            throw new ArgumentException(
                "Transcript timing must be finite, non-negative, and ordered.",
                nameof(transcriptEvent));
        }

        return transcriptEvent.Text;
    }

    private void AppendCommittedText(string text)
    {
        if (committedText.Length != 0 &&
            !char.IsWhiteSpace(committedText[^1]) &&
            !StartsWithClosingPunctuation(text))
        {
            committedText.Append(' ');
        }

        committedText.Append(text);
    }

    private static bool StartsWithClosingPunctuation(string text) =>
        text[0] is '.' or ',' or '!' or '?' or ';' or ':' or '%' or ')';

    private readonly record struct FinalSegmentKey(
        string Text,
        long StartTimeBits,
        long EndTimeBits);
}
