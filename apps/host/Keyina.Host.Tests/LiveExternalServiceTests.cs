using Keyina.Host.Core.Translation;
using Keyina.Host.Speech;
using Keyina.Host.Translation;
using Keyina.Host.Windows.Audio;
using Keyina.Host.Windows.Credentials;

namespace Keyina.Host.Tests;

internal static class LiveExternalServiceTests
{
    [KeyinaTest("live DeepL credential translates Vietnamese through the production provider")]
    private static void LiveDeepLTranslatesVietnamese()
    {
        if (!IsEnabled("KEYINA_RUN_LIVE_DEEPL_TEST"))
        {
            return;
        }

        var apiKey = new WindowsCredentialVault().Read(CredentialTargets.DeepLApiKey);
        AssertEx.True(
            !string.IsNullOrWhiteSpace(apiKey),
            "The saved DeepL credential was not available.");

        using var client = new HttpClient();
        var result = new DeepLTranslationProvider(client)
            .TranslateAsync(
                apiKey!,
                new TranslationRequest("Xin chào", "EN-US"),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.Equal("DeepL", result.Provider);
        AssertEx.Equal("VI", result.DetectedSourceLanguage);
        AssertEx.True(
            !string.IsNullOrWhiteSpace(result.Text),
            "DeepL returned an empty translation.");
    }

    [KeyinaTest("live Speechmatics credential and microphone stream audio through production adapters")]
    private static void LiveSpeechmaticsStreamsMicrophoneAudio()
    {
        if (!IsEnabled("KEYINA_RUN_LIVE_SPEECHMATICS_TEST"))
        {
            return;
        }

        var apiKey = new WindowsCredentialVault().Read(
            CredentialTargets.SpeechmaticsApiKey);
        AssertEx.True(
            !string.IsNullOrWhiteSpace(apiKey),
            "The saved Speechmatics credential was not available.");

        var session = new SpeechmaticsSessionFactory().Create(apiKey!);
        try
        {
            using var startTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(15));
            session.StartAsync(startTimeout.Token).GetAwaiter().GetResult();

            var capture = new WasapiMicrophoneCapture();
            using var captureTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(8));
            var enumerator = capture.CaptureAsync(captureTimeout.Token)
                .GetAsyncEnumerator(captureTimeout.Token);
            try
            {
                var chunks = 0;
                while (chunks < 10 &&
                       enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                {
                    session.SendAudioAsync(
                            enumerator.Current,
                            captureTimeout.Token)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                    chunks++;
                }

                AssertEx.Equal(
                    10,
                    chunks,
                    "The default microphone did not produce enough audio chunks.");
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static bool IsEnabled(string variable) =>
        string.Equals(
            Environment.GetEnvironmentVariable(variable),
            "1",
            StringComparison.Ordinal);
}
