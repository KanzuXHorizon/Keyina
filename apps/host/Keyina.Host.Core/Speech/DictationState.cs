namespace Keyina.Host.Core.Speech;

public enum DictationStatus
{
    Idle,
    Connecting,
    Listening,
    Finalizing,
    Inserted,
    Error,
    Cancelled,
}

public sealed record DictationState(
    DictationStatus Status,
    string PartialText,
    string CommittedText,
    int FinalSegments,
    string? ErrorCode)
{
    public static DictationState Initial { get; } =
        new(DictationStatus.Idle, string.Empty, string.Empty, 0, null);

    public string DisplayText => string.IsNullOrWhiteSpace(PartialText)
        ? CommittedText
        : string.IsNullOrWhiteSpace(CommittedText)
            ? PartialText
            : $"{CommittedText} {PartialText}";
}

public abstract record DictationEvent
{
    private DictationEvent()
    {
    }

    public sealed record StartRequested : DictationEvent;

    public sealed record RecognitionStarted : DictationEvent;

    public sealed record PartialUpdated(string Text) : DictationEvent;

    public sealed record FinalReceived(string CommittedText) : DictationEvent;

    public sealed record StopRequested : DictationEvent;

    public sealed record FinalInserted : DictationEvent;

    public sealed record Failed(string ErrorCode) : DictationEvent;

    public sealed record Cancelled : DictationEvent;

    public sealed record Reset : DictationEvent;
}
