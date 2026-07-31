using Keyina.Host.Core.Speech;
using Keyina.Host.UI;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.Tests;

internal static class DictationOverlayFormTests
{
    [KeyinaTest("dictation overlay shows live transcript without taking focus")]
    private static void OverlayPresentsLiveSessionState()
    {
        using var form = new DictationOverlayForm();
        var state = new DictationState(
            DictationStatus.Listening,
            "thế giới",
            "xin chào",
            FinalSegments: 1,
            ErrorCode: null);

        form.Present(state);

        AssertEx.True(form.TopMost, "Dictation overlay should remain visible while listening.");
        AssertEx.False(form.ShowInTaskbar, "Dictation overlay should not create a taskbar item.");
        AssertEx.Equal(FormBorderStyle.None, form.FormBorderStyle);
        AssertEx.True(
            form.UsesNoActivateWindowStyle,
            "Dictation overlay could activate and steal foreground focus.");
        AssertEx.True(
            form.UsesClickThroughWindowStyle,
            "Dictation overlay should not intercept fullscreen mouse input.");
        AssertEx.Equal(
            "Đang nghe · Ctrl + Alt + V để hoàn tất",
            ((Label)form.Controls.Find("dictationOverlayStatus", true).Single()).Text);
        AssertEx.Equal(
            "xin chào thế giới",
            ((Label)form.Controls.Find("dictationOverlayTranscript", true).Single()).Text);
    }

    [KeyinaTest("large Fluent surfaces use square corners")]
    private static void LargeSurfacesAreSquare()
    {
        using var card = new FluentCard();
        AssertEx.Equal(0, card.CornerRadius);
        AssertEx.Equal(0, FluentMetrics.ControlCornerRadius);
    }
}
