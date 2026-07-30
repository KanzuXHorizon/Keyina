using Keyina.Host.Core.Feedback;

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
