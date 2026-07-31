using Keyina.Host.Core.Feedback;
using Keyina.Host.Windows.Feedback;

namespace Keyina.Host.Tests;

internal static class FeedbackSoundPlayerTests
{
    [KeyinaTest("feedback sound player ignores the no-sound sentinel")]
    private static void NoneDoesNotCallNativePlayback()
    {
        var calls = 0;
        var player = new WindowsFeedbackSoundPlayer(_ =>
        {
            calls++;
            return true;
        });

        player.Play(FeedbackSoundCue.None);

        AssertEx.Equal(0, calls);
    }

    [KeyinaTest("feedback sound player prefers the configured Windows system sound")]
    private static void SystemSoundIsPreferred()
    {
        var systemCalls = 0;
        var fallbackCalls = 0;
        var player = new WindowsFeedbackSoundPlayer(
            _ =>
            {
                systemCalls++;
                return true;
            },
            _ =>
            {
                fallbackCalls++;
                return true;
            });

        player.Play(FeedbackSoundCue.Start);

        AssertEx.Equal(1, systemCalls);
        AssertEx.Equal(0, fallbackCalls);
    }

    [KeyinaTest("feedback sound player falls back to the generated wave when Windows playback fails")]
    private static void SystemSoundFailureUsesFallback()
    {
        var fallbackCalls = 0;
        var player = new WindowsFeedbackSoundPlayer(
            _ => false,
            _ =>
            {
                fallbackCalls++;
                return true;
            });

        player.Play(FeedbackSoundCue.Error);

        AssertEx.Equal(1, fallbackCalls);
    }

    [KeyinaTest("feedback sound player is best effort when native playback fails")]
    private static void NativeFailureDoesNotEscape()
    {
        byte[]? played = null;
        var player = new WindowsFeedbackSoundPlayer(wave =>
        {
            played = wave;
            return false;
        });

        player.Play(FeedbackSoundCue.Success);

        AssertEx.NotNull(played, "Feedback player did not provide wave data to native playback.");
        AssertEx.Equal("RIFF", System.Text.Encoding.ASCII.GetString(played!, 0, 4));
    }
}
