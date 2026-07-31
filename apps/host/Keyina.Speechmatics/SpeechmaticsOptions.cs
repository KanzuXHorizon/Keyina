namespace Keyina.Speechmatics;

public sealed record SpeechmaticsOptions
{
    public static SpeechmaticsOptions VietnameseDefault { get; } = new()
    {
        Endpoint = new Uri("wss://global.rt.speechmatics.com/v2", UriKind.Absolute),
        Language = "vi",
        Model = "enhanced",
        MaxDelaySeconds = 2.0,
        MaxDelayMode = "flexible",
        EndOfUtteranceSilenceTriggerSeconds = 0.0,
        EnablePartials = true,
        SampleRate = 16_000,
        ChunkSizeBytes = 4_096,
    };

    public required Uri Endpoint { get; init; }

    public required string Language { get; init; }

    public required string Model { get; init; }

    public double MaxDelaySeconds { get; init; }

    public string MaxDelayMode { get; init; } = "flexible";

    public double EndOfUtteranceSilenceTriggerSeconds { get; init; }

    public bool EnablePartials { get; init; }

    public int SampleRate { get; init; }

    public int ChunkSizeBytes { get; init; }

    public void Validate()
    {
        if (!Endpoint.IsAbsoluteUri || !string.Equals(Endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Speechmatics endpoint must use secure WebSocket (wss).", nameof(Endpoint));
        }

        if (string.IsNullOrWhiteSpace(Language))
        {
            throw new ArgumentException("Speechmatics language is required.", nameof(Language));
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new ArgumentException("Speechmatics model is required.", nameof(Model));
        }

        if (!double.IsFinite(MaxDelaySeconds) || MaxDelaySeconds < 0.7 || MaxDelaySeconds > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxDelaySeconds),
                "Max delay must be between 0.7 and 4 seconds.");
        }

        if (!string.Equals(MaxDelayMode, "flexible", StringComparison.Ordinal) &&
            !string.Equals(MaxDelayMode, "fixed", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Max delay mode must be either 'flexible' or 'fixed'.",
                nameof(MaxDelayMode));
        }

        if (!double.IsFinite(EndOfUtteranceSilenceTriggerSeconds) ||
            EndOfUtteranceSilenceTriggerSeconds < 0 ||
            EndOfUtteranceSilenceTriggerSeconds > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EndOfUtteranceSilenceTriggerSeconds),
                "End-of-utterance silence trigger must be between 0 and 2 seconds.");
        }

        if (SampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SampleRate), "Sample rate must be positive.");
        }

        if (ChunkSizeBytes <= 0 || (ChunkSizeBytes & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ChunkSizeBytes), "PCM 16-bit chunks must have a positive even byte count.");
        }
    }
}
