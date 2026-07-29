namespace Keyina.Speechmatics;

public enum SpeechEventKind
{
    Unknown,
    RecognitionStarted,
    AudioAdded,
    PartialTranscript,
    FinalTranscript,
    EndOfTranscript,
    ProviderError,
    ProviderWarning,
    ProviderInfo,
}

public sealed record SpeechEvent
{
    public required SpeechEventKind Kind { get; init; }

    public string? Text { get; init; }

    public string? SessionId { get; init; }

    public string? ProviderType { get; init; }

    public string? Reason { get; init; }

    public int? SequenceNumber { get; init; }

    public double? StartTimeSeconds { get; init; }

    public double? EndTimeSeconds { get; init; }
}

public sealed class SpeechmaticsProtocolException : Exception
{
    public SpeechmaticsProtocolException(string message)
        : base(message)
    {
    }

    public SpeechmaticsProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
