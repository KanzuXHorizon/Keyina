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
            "Đang nghe",
            ((Label)form.Controls.Find("dictationOverlayStatus", true).Single()).Text);
        AssertEx.Equal(
            "Ctrl + Alt + V · Hoàn tất",
            ((Label)form.Controls.Find("dictationOverlayShortcut", true).Single()).Text);
        AssertEx.Equal(
            "xin chào thế giới",
            ((Label)form.Controls.Find("dictationOverlayTranscript", true).Single()).Text);
    }

    [KeyinaTest("dictation overlay keeps the latest words visible for long transcripts")]
    private static void OverlayKeepsLatestTranscriptVisible()
    {
        using var form = new DictationOverlayForm();
        var latest = "đây là phần mới nhất cần luôn nhìn thấy";
        var transcript = string.Join(' ', Enumerable.Repeat(
            "nội dung nhận dạng trước đó đang tiếp tục kéo dài",
            20)) + " " + latest;

        form.Present(new DictationState(
            DictationStatus.Listening,
            PartialText: string.Empty,
            CommittedText: transcript,
            FinalSegments: 20,
            ErrorCode: null));

        var label = (Label)form.Controls.Find(
            "dictationOverlayTranscript",
            searchAllChildren: true).Single();
        AssertEx.True(
            label.Text.StartsWith("… ", StringComparison.Ordinal),
            "Long transcript did not indicate that earlier content was collapsed.");
        AssertEx.True(
            label.Text.EndsWith(latest, StringComparison.Ordinal),
            "Long transcript hid the latest recognized words.");
        AssertEx.True(
            label.Text.Length <= DictationOverlayForm.MaximumVisibleTranscriptCharacters + 2,
            "Overlay transcript exceeded its bounded presentation budget.");
        AssertEx.False(label.AutoEllipsis,
            "The OS ellipsis path can hide the newest words and must remain disabled.");
    }

    [KeyinaTest("large Fluent surfaces use square corners")]
    private static void LargeSurfacesAreSquare()
    {
        using var card = new FluentCard();
        AssertEx.Equal(0, card.CornerRadius);
        AssertEx.Equal(0, FluentMetrics.ControlCornerRadius);
    }
}
