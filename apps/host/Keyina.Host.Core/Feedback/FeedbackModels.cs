namespace Keyina.Host.Core.Feedback;

public enum FeedbackMode
{
    Automatic,
    VisualOnly,
    AudioOnly,
    Off,
}

public sealed record FeedbackPreferences(FeedbackMode Mode)
{
    public static FeedbackPreferences Default { get; } = new(FeedbackMode.Automatic);
}

public enum FeedbackEventKind
{
    VietnameseEnabled,
    VietnameseDisabled,
    DictationConnecting,
    DictationListening,
    DictationFinalizing,
    DictationInserted,
    DictationCancelled,
    Error,
    Preview,
}

public enum FeedbackTone
{
    Neutral,
    Accent,
    Success,
    Warning,
    Error,
}

public enum FeedbackSoundCue
{
    None,
    Enabled,
    Disabled,
    Start,
    Success,
    Cancel,
    Error,
}

public sealed record FeedbackEvent(
    FeedbackEventKind Kind,
    string Message,
    FeedbackTone Tone,
    FeedbackSoundCue SoundCue,
    TimeSpan Duration);

public enum ForegroundPresentationState
{
    Unknown,
    Windowed,
    FullscreenLike,
}

public readonly record struct FeedbackPresentation(
    bool ShowOverlay,
    bool PlaySound);

public static class FeedbackPresentationPolicy
{
    public static FeedbackPresentation Resolve(
        FeedbackPreferences preferences,
        ForegroundPresentationState foregroundState)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return preferences.Mode switch
        {
            FeedbackMode.Automatic => new(
                ShowOverlay: foregroundState == ForegroundPresentationState.Windowed,
                PlaySound: true),
            FeedbackMode.VisualOnly => new(ShowOverlay: true, PlaySound: false),
            FeedbackMode.AudioOnly => new(ShowOverlay: false, PlaySound: true),
            FeedbackMode.Off => new(ShowOverlay: false, PlaySound: false),
            _ => throw new ArgumentOutOfRangeException(nameof(preferences)),
        };
    }
}
