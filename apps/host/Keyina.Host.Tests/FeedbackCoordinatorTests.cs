using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Speech;
using Keyina.Host.UI.Feedback;
using Keyina.Host.Windows.Feedback;

namespace Keyina.Host.Tests;

internal static class FeedbackCoordinatorTests
{
    [KeyinaTest("feedback coordinator routes automatic windowed events to both channels")]
    private static void AutomaticWindowedUsesBothChannels()
    {
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        using var coordinator = new FeedbackCoordinator(
            FeedbackPreferences.Default,
            new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            overlay,
            sound);

        coordinator.Publish(CreateEnabledEvent());

        AssertEx.Equal(1, overlay.Events.Count);
        AssertEx.Equal(1, sound.Cues.Count);
        AssertEx.Equal(FeedbackSoundCue.Enabled, sound.Cues[0]);
    }

    [KeyinaTest("feedback coordinator suppresses automatic overlay in fullscreen")]
    private static void AutomaticFullscreenUsesAudioOnly()
    {
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        using var coordinator = new FeedbackCoordinator(
            FeedbackPreferences.Default,
            new FixedForegroundProbe(ForegroundPresentationState.FullscreenLike),
            overlay,
            sound);

        coordinator.Publish(CreateEnabledEvent());

        AssertEx.Equal(0, overlay.Events.Count);
        AssertEx.Equal(1, sound.Cues.Count);
    }

    [KeyinaTest("dictation start cue remains audible when fullscreen visual feedback is suppressed")]
    private static void FullscreenDictationStartKeepsAudioCue()
    {
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        using var coordinator = new FeedbackCoordinator(
            FeedbackPreferences.Default,
            new FixedForegroundProbe(ForegroundPresentationState.FullscreenLike),
            overlay,
            sound);

        coordinator.Publish(
            FeedbackEvents.ForDictation(DictationStatus.Connecting)!,
            suppressVisual: true);
        coordinator.Publish(
            FeedbackEvents.ForDictation(DictationStatus.Listening)!,
            suppressVisual: true);

        AssertEx.Equal(0, overlay.Events.Count);
        AssertEx.Equal(1, sound.Cues.Count);
        AssertEx.Equal(FeedbackSoundCue.Start, sound.Cues[0]);
    }

    [KeyinaTest("feedback coordinator honors disabled mode")]
    private static void DisabledModeUsesNoChannels()
    {
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        using var coordinator = new FeedbackCoordinator(
            new FeedbackPreferences(FeedbackMode.Off),
            new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            overlay,
            sound);

        coordinator.Publish(CreateEnabledEvent());

        AssertEx.Equal(0, overlay.Events.Count);
        AssertEx.Equal(0, sound.Cues.Count);
    }

    [KeyinaTest("feedback coordinator coalesces duplicate events for 150 milliseconds")]
    private static void DuplicateEventsAreCoalesced()
    {
        var now = new DateTimeOffset(2026, 7, 30, 5, 0, 0, TimeSpan.Zero);
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        using var coordinator = new FeedbackCoordinator(
            FeedbackPreferences.Default,
            new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            overlay,
            sound,
            () => now);

        coordinator.Publish(CreateEnabledEvent());
        now += TimeSpan.FromMilliseconds(100);
        coordinator.Publish(CreateEnabledEvent());
        now += TimeSpan.FromMilliseconds(51);
        coordinator.Publish(CreateEnabledEvent());

        AssertEx.Equal(2, overlay.Events.Count);
        AssertEx.Equal(2, sound.Cues.Count);
    }

    [KeyinaTest("feedback channel failures are isolated from each other and callers")]
    private static void ChannelFailuresAreIsolated()
    {
        var sound = new RecordingSoundPlayer();
        using (var coordinator = new FeedbackCoordinator(
                   FeedbackPreferences.Default,
                   new FixedForegroundProbe(ForegroundPresentationState.Windowed),
                   new ThrowingOverlay(),
                   sound))
        {
            coordinator.Publish(CreateEnabledEvent());
        }
        AssertEx.Equal(1, sound.Cues.Count);

        var overlay = new RecordingOverlay();
        using (var coordinator = new FeedbackCoordinator(
                   FeedbackPreferences.Default,
                   new FixedForegroundProbe(ForegroundPresentationState.Windowed),
                   overlay,
                   new ThrowingSoundPlayer()))
        {
            coordinator.Publish(CreateEnabledEvent());
        }
        AssertEx.Equal(1, overlay.Events.Count);
    }

    private static FeedbackEvent CreateEnabledEvent() => new(
        FeedbackEventKind.VietnameseEnabled,
        "Tiếng Việt đã bật",
        FeedbackTone.Success,
        FeedbackSoundCue.Enabled,
        TimeSpan.FromMilliseconds(900));

    private sealed class FixedForegroundProbe(ForegroundPresentationState state)
        : IForegroundPresentationProbe
    {
        public ForegroundPresentationState GetState() => state;
    }

    private sealed class RecordingOverlay : IFeedbackOverlay
    {
        public List<FeedbackEvent> Events { get; } = [];

        public void Present(FeedbackEvent feedbackEvent) => Events.Add(feedbackEvent);

        public void HideFeedback()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingOverlay : IFeedbackOverlay
    {
        public void Present(FeedbackEvent feedbackEvent) =>
            throw new InvalidOperationException("overlay failed");

        public void HideFeedback()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSoundPlayer : IFeedbackSoundPlayer
    {
        public List<FeedbackSoundCue> Cues { get; } = [];

        public void Play(FeedbackSoundCue cue) => Cues.Add(cue);
    }

    private sealed class ThrowingSoundPlayer : IFeedbackSoundPlayer
    {
        public void Play(FeedbackSoundCue cue) =>
            throw new InvalidOperationException("sound failed");
    }
}
