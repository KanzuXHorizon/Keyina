using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Speech;

namespace Keyina.Host.Tests;

internal static class FeedbackPolicyTests
{
    [KeyinaTest("automatic feedback uses visual and audio for ordinary windows")]
    private static void AutomaticUsesBothChannelsForWindowedApps()
    {
        var presentation = FeedbackPresentationPolicy.Resolve(
            FeedbackPreferences.Default,
            ForegroundPresentationState.Windowed);

        AssertEx.Equal(
            new FeedbackPresentation(ShowOverlay: true, PlaySound: true),
            presentation);
    }

    [KeyinaTest("automatic feedback suppresses overlay for fullscreen applications")]
    private static void AutomaticUsesAudioOnlyForFullscreenApps()
    {
        var presentation = FeedbackPresentationPolicy.Resolve(
            FeedbackPreferences.Default,
            ForegroundPresentationState.FullscreenLike);

        AssertEx.Equal(
            new FeedbackPresentation(ShowOverlay: false, PlaySound: true),
            presentation);
    }

    [KeyinaTest("semantic feedback maps input and dictation states without transcript content")]
    private static void SemanticEventsContainOnlySafeStatusCopy()
    {
        var disabled = FeedbackEvents.ForVietnamese(enabled: false);
        AssertEx.Equal(FeedbackEventKind.VietnameseDisabled, disabled.Kind);
        AssertEx.Equal(FeedbackSoundCue.Disabled, disabled.SoundCue);
        AssertEx.Equal("Tiếng Việt đã tắt", disabled.Message);

        var listening = FeedbackEvents.ForDictation(DictationStatus.Listening);
        AssertEx.NotNull(listening, "Listening state did not create feedback.");
        AssertEx.Equal(FeedbackEventKind.DictationListening, listening!.Kind);
        AssertEx.Equal("Đang nghe", listening.Message);
        AssertEx.Equal(FeedbackSoundCue.None, listening.SoundCue);
        AssertEx.Equal(Timeout.InfiniteTimeSpan, listening.Duration);

        AssertEx.Equal(null, FeedbackEvents.ForDictation(DictationStatus.Idle));
    }

    [KeyinaTest("explicit feedback modes override foreground presentation")]
    private static void ExplicitModesOverrideForegroundPresentation()
    {
        AssertEx.Equal(
            new FeedbackPresentation(ShowOverlay: true, PlaySound: false),
            FeedbackPresentationPolicy.Resolve(
                new FeedbackPreferences(FeedbackMode.VisualOnly),
                ForegroundPresentationState.FullscreenLike));
        AssertEx.Equal(
            new FeedbackPresentation(ShowOverlay: false, PlaySound: true),
            FeedbackPresentationPolicy.Resolve(
                new FeedbackPreferences(FeedbackMode.AudioOnly),
                ForegroundPresentationState.Windowed));
        AssertEx.Equal(
            new FeedbackPresentation(ShowOverlay: false, PlaySound: false),
            FeedbackPresentationPolicy.Resolve(
                new FeedbackPreferences(FeedbackMode.Off),
                ForegroundPresentationState.Windowed));
    }
}
