using System.Text;
using Keyina.Speechmatics;

namespace Keyina.Host.Tests;

internal static class SpeechmaticsProtocolTests
{
    private const string ExpectedStartJson =
        "{\"message\":\"StartRecognition\",\"audio_format\":{\"type\":\"raw\",\"encoding\":\"pcm_s16le\",\"sample_rate\":16000},\"transcription_config\":{\"language\":\"vi\",\"model\":\"enhanced\",\"max_delay\":2,\"max_delay_mode\":\"flexible\",\"enable_partials\":true,\"conversation_config\":{\"end_of_utterance_silence_trigger\":0}}}";

    [KeyinaTest("Speechmatics Vietnamese defaults match the production realtime contract")]
    private static void DefaultsAreVietnameseAndLowLatency()
    {
        var options = SpeechmaticsOptions.VietnameseDefault;
        AssertEx.Equal(new Uri("wss://global.rt.speechmatics.com/v2"), options.Endpoint);
        AssertEx.Equal("vi", options.Language);
        AssertEx.Equal("enhanced", options.Model);
        AssertEx.Equal(2.0, options.MaxDelaySeconds);
        AssertEx.Equal("flexible", options.MaxDelayMode);
        AssertEx.Equal(0.0, options.EndOfUtteranceSilenceTriggerSeconds);
        AssertEx.True(options.EnablePartials, "Partials should be enabled for overlay feedback.");
        AssertEx.Equal(16_000, options.SampleRate);
        AssertEx.Equal(4_096, options.ChunkSizeBytes);
        options.Validate();
    }

    [KeyinaTest("Speechmatics StartRecognition JSON is deterministic and exact")]
    private static void StartRecognitionJsonIsExact()
    {
        var bytes = SpeechmaticsProtocol.CreateStartRecognition(
            SpeechmaticsOptions.VietnameseDefault);
        AssertEx.Equal(ExpectedStartJson, Encoding.UTF8.GetString(bytes));
    }

    [KeyinaTest("Speechmatics EndOfStream JSON carries the exact last sequence")]
    private static void EndOfStreamJsonIsExact()
    {
        var bytes = SpeechmaticsProtocol.CreateEndOfStream(lastSequenceNumber: 37);
        AssertEx.Equal(
            "{\"message\":\"EndOfStream\",\"last_seq_no\":37}",
            Encoding.UTF8.GetString(bytes));
    }

    [KeyinaTest("Speechmatics options reject unsafe endpoints and invalid audio configuration")]
    private static void InvalidOptionsAreRejected()
    {
        AssertThrows<ArgumentException>(() =>
            (SpeechmaticsOptions.VietnameseDefault with
            {
                Endpoint = new Uri("ws://global.rt.speechmatics.com/v2"),
            }).Validate());
        AssertThrows<ArgumentException>(() =>
            (SpeechmaticsOptions.VietnameseDefault with { Language = "" }).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() =>
            (SpeechmaticsOptions.VietnameseDefault with { MaxDelaySeconds = 0 }).Validate());
        AssertThrows<ArgumentException>(() =>
            (SpeechmaticsOptions.VietnameseDefault with { MaxDelayMode = "fast" }).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() =>
            (SpeechmaticsOptions.VietnameseDefault with
            {
                EndOfUtteranceSilenceTriggerSeconds = 2.1,
            }).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() =>
            (SpeechmaticsOptions.VietnameseDefault with { SampleRate = 0 }).Validate());
        AssertThrows<ArgumentOutOfRangeException>(() =>
            (SpeechmaticsOptions.VietnameseDefault with { ChunkSizeBytes = 3 }).Validate());
    }

    [KeyinaTest("Speechmatics parser distinguishes revised partials and immutable finals")]
    private static void TranscriptMessagesAreParsed()
    {
        var partial = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"AddPartialTranscript\",\"metadata\":{\"transcript\":\"xin chao\",\"start_time\":0.1,\"end_time\":0.5}}"u8);
        AssertEx.Equal(SpeechEventKind.PartialTranscript, partial.Kind);
        AssertEx.Equal("xin chao", partial.Text);
        AssertEx.Equal(0.1, partial.StartTimeSeconds);
        AssertEx.Equal(0.5, partial.EndTimeSeconds);

        var final = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"AddTranscript\",\"metadata\":{\"transcript\":\"xin chào\",\"start_time\":0.1,\"end_time\":0.8}}"u8);
        AssertEx.Equal(SpeechEventKind.FinalTranscript, final.Kind);
        AssertEx.Equal("xin chào", final.Text);
        AssertEx.Equal(0.8, final.EndTimeSeconds);
    }

    [KeyinaTest("Speechmatics parser accepts empty transcript fragments during warm-up and final flush")]
    private static void EmptyTranscriptFragmentsAreAccepted()
    {
        var partial = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"AddPartialTranscript\",\"metadata\":{\"transcript\":\"\",\"start_time\":0.0,\"end_time\":0.62}}"u8);

        AssertEx.Equal(SpeechEventKind.PartialTranscript, partial.Kind);
        AssertEx.Equal(string.Empty, partial.Text);
        AssertEx.Equal(0.0, partial.StartTimeSeconds);
        AssertEx.Equal(0.62, partial.EndTimeSeconds);

        var final = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"AddTranscript\",\"metadata\":{\"transcript\":\"\",\"start_time\":0.0,\"end_time\":0.62}}"u8);
        AssertEx.Equal(SpeechEventKind.FinalTranscript, final.Kind);
        AssertEx.Equal(string.Empty, final.Text);
    }

    [KeyinaTest("Speechmatics parser handles session acknowledgement audio acknowledgement and end")]
    private static void LifecycleMessagesAreParsed()
    {
        var started = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"RecognitionStarted\",\"id\":\"session-1\"}"u8);
        AssertEx.Equal(SpeechEventKind.RecognitionStarted, started.Kind);
        AssertEx.Equal("session-1", started.SessionId);

        var audio = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"AudioAdded\",\"seq_no\":12}"u8);
        AssertEx.Equal(SpeechEventKind.AudioAdded, audio.Kind);
        AssertEx.Equal(12, audio.SequenceNumber);

        var ended = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"EndOfTranscript\"}"u8);
        AssertEx.Equal(SpeechEventKind.EndOfTranscript, ended.Kind);
    }

    [KeyinaTest("Speechmatics parser preserves provider error type and reason without transcript logging")]
    private static void ProviderErrorsAreParsed()
    {
        var error = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"Error\",\"type\":\"not_authorised\",\"reason\":\"invalid token\"}"u8);
        AssertEx.Equal(SpeechEventKind.ProviderError, error.Kind);
        AssertEx.Equal("not_authorised", error.ProviderType);
        AssertEx.Equal("invalid token", error.Reason);
        AssertEx.Equal<string?>(null, error.Text);
    }

    [KeyinaTest("Speechmatics parser rejects malformed messages and returns unknown for future message types")]
    private static void MalformedAndUnknownMessagesAreHandled()
    {
        AssertThrows<SpeechmaticsProtocolException>(() =>
            SpeechmaticsProtocol.ParseServerMessage("{"u8));
        AssertThrows<SpeechmaticsProtocolException>(() =>
            SpeechmaticsProtocol.ParseServerMessage("{\"metadata\":{}}"u8));
        AssertThrows<SpeechmaticsProtocolException>(() =>
            SpeechmaticsProtocol.ParseServerMessage(
                "{\"message\":\"AddTranscript\",\"metadata\":{}}"u8));

        var unknown = SpeechmaticsProtocol.ParseServerMessage(
            "{\"message\":\"FutureMessage\",\"value\":1}"u8);
        AssertEx.Equal(SpeechEventKind.Unknown, unknown.Kind);
        AssertEx.Equal("FutureMessage", unknown.ProviderType);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
