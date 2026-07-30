using Keyina.Host.Core.Speech;

namespace Keyina.Host.Core.Feedback;

public static class FeedbackEvents
{
    private static readonly TimeSpan StandardDuration = TimeSpan.FromMilliseconds(900);

    public static FeedbackEvent ForVietnamese(bool enabled) => enabled
        ? new FeedbackEvent(
            FeedbackEventKind.VietnameseEnabled,
            "Tiếng Việt đã bật",
            FeedbackTone.Success,
            FeedbackSoundCue.Enabled,
            StandardDuration)
        : new FeedbackEvent(
            FeedbackEventKind.VietnameseDisabled,
            "Tiếng Việt đã tắt",
            FeedbackTone.Neutral,
            FeedbackSoundCue.Disabled,
            StandardDuration);

    public static FeedbackEvent? ForDictation(DictationStatus status) => status switch
    {
        DictationStatus.Idle => null,
        DictationStatus.Connecting => new(
            FeedbackEventKind.DictationConnecting,
            "Đang kết nối",
            FeedbackTone.Accent,
            FeedbackSoundCue.Start,
            Timeout.InfiniteTimeSpan),
        DictationStatus.Listening => new(
            FeedbackEventKind.DictationListening,
            "Đang nghe",
            FeedbackTone.Accent,
            FeedbackSoundCue.None,
            Timeout.InfiniteTimeSpan),
        DictationStatus.Finalizing => new(
            FeedbackEventKind.DictationFinalizing,
            "Đang hoàn tất",
            FeedbackTone.Neutral,
            FeedbackSoundCue.None,
            Timeout.InfiniteTimeSpan),
        DictationStatus.Inserted => new(
            FeedbackEventKind.DictationInserted,
            "Đã chèn nội dung",
            FeedbackTone.Success,
            FeedbackSoundCue.Success,
            StandardDuration),
        DictationStatus.Cancelled => new(
            FeedbackEventKind.DictationCancelled,
            "Đã hủy",
            FeedbackTone.Neutral,
            FeedbackSoundCue.Cancel,
            StandardDuration),
        DictationStatus.Error => Error("Không thể nhập bằng giọng nói"),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static FeedbackEvent TranslationStarted(string targetDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDisplayName);
        return new FeedbackEvent(
            FeedbackEventKind.TranslationStarted,
            $"Đang dịch sang {targetDisplayName}",
            FeedbackTone.Accent,
            FeedbackSoundCue.Start,
            Timeout.InfiniteTimeSpan);
    }

    public static FeedbackEvent TranslationCompleted(string targetDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDisplayName);
        return new FeedbackEvent(
            FeedbackEventKind.TranslationCompleted,
            $"Đã dịch sang {targetDisplayName}",
            FeedbackTone.Success,
            FeedbackSoundCue.Success,
            StandardDuration);
    }

    public static FeedbackEvent TranslationCancelled() => new(
        FeedbackEventKind.TranslationCancelled,
        "Đã hủy dịch",
        FeedbackTone.Neutral,
        FeedbackSoundCue.Cancel,
        StandardDuration);

    public static FeedbackEvent Error(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new FeedbackEvent(
            FeedbackEventKind.Error,
            message,
            FeedbackTone.Error,
            FeedbackSoundCue.Error,
            TimeSpan.FromMilliseconds(2_400));
    }

    public static FeedbackEvent Preview() => new(
        FeedbackEventKind.Preview,
        "Phản hồi Keyina đang hoạt động",
        FeedbackTone.Accent,
        FeedbackSoundCue.Success,
        TimeSpan.FromMilliseconds(1_200));
}
