using Keyina.Host.Speech;

namespace Keyina.Host.Tests;

internal static class SpeechSelfTestTests
{
    [KeyinaTest("speech self test uses no network microphone or credential and completes deterministically")]
    private static void SelfTestCompletesOffline()
    {
        var result = SpeechSelfTest.RunAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.True(result.Success, $"Speech self-test failed: {result.Code}");
        AssertEx.Equal("speech_self_test_ok", result.Code);
        AssertEx.Equal(1, result.FinalTranscriptCount);
        AssertEx.True(result.TransportClosed, "Self-test transport was not closed.");
    }
}
