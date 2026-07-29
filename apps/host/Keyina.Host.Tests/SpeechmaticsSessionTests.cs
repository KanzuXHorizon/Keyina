using System.Text;
using Keyina.Speechmatics;

namespace Keyina.Host.Tests;

internal static class SpeechmaticsSessionTests
{
    [KeyinaTest("Speechmatics session authenticates sends start once and waits for RecognitionStarted")]
    private static void StartHandshakeIsOrdered() => Run(async () =>
    {
        var transport = new FakeSpeechmaticsTransport();
        await using var session = CreateSession(transport);

        var startTask = session.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => transport.SentText.Count == 1);

        AssertEx.Equal(SpeechmaticsOptions.VietnameseDefault.Endpoint, transport.ConnectedEndpoint);
        AssertEx.Equal("Bearer test-token", transport.AuthorizationHeader);
        AssertEx.True(!startTask.IsCompleted, "Start completed before RecognitionStarted.");
        AssertEx.True(
            Encoding.UTF8.GetString(transport.SentText[0]).Contains("StartRecognition", StringComparison.Ordinal),
            "StartRecognition was not the first text frame.");

        transport.EnqueueJson("{\"message\":\"RecognitionStarted\",\"id\":\"session-1\"}");
        await startTask;
        AssertEx.Equal(SpeechmaticsSessionState.Started, session.State);
    });

    [KeyinaTest("Speechmatics session rejects audio before start and validates PCM chunks")]
    private static void AudioRequiresStartedSessionAndValidPcm() => Run(async () =>
    {
        var transport = new FakeSpeechmaticsTransport();
        await using var session = CreateSession(transport);

        await AssertThrowsAsync<InvalidOperationException>(() =>
            session.SendAudioAsync(new byte[2], CancellationToken.None));

        await StartAsync(session, transport);
        await AssertThrowsAsync<ArgumentException>(() =>
            session.SendAudioAsync(new byte[3], CancellationToken.None));
        await AssertThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.SendAudioAsync(
                new byte[SpeechmaticsOptions.VietnameseDefault.ChunkSizeBytes + 2],
                CancellationToken.None));

        await session.SendAudioAsync(new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
        AssertEx.Equal(1, transport.SentBinary.Count);
        AssertEx.True(transport.SentBinary[0].SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "Binary audio payload changed in transport.");
    });

    [KeyinaTest("Speechmatics outstanding audio is bounded to 500 chunks until AudioAdded")]
    private static void OutstandingAudioUsesBackpressure() => Run(async () =>
    {
        var transport = new FakeSpeechmaticsTransport();
        await using var session = CreateSession(transport);
        await StartAsync(session, transport);

        for (var index = 0; index < SpeechmaticsRealtimeSession.MaximumOutstandingAudioChunks; index++)
        {
            await session.SendAudioAsync(new byte[2], CancellationToken.None);
        }

        var blocked = session.SendAudioAsync(new byte[2], CancellationToken.None);
        await Task.Delay(30);
        AssertEx.True(!blocked.IsCompleted, "The 501st audio chunk bypassed backpressure.");

        transport.EnqueueJson("{\"message\":\"AudioAdded\",\"seq_no\":1}");
        await blocked.AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        AssertEx.Equal(501, transport.SentBinary.Count);
    });

    [KeyinaTest("Speechmatics stop sends exact EndOfStream and waits for final then end")]
    private static void StopHandshakePreservesFinalOrdering() => Run(async () =>
    {
        var transport = new FakeSpeechmaticsTransport();
        await using var session = CreateSession(transport);
        await StartAsync(session, transport);
        await session.SendAudioAsync(new byte[2], CancellationToken.None);
        await session.SendAudioAsync(new byte[2], CancellationToken.None);

        var finalEventTask = session.ReadEventAsync(CancellationToken.None).AsTask();
        var stopTask = session.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await WaitUntilAsync(() => transport.SentText.Count == 2);

        AssertEx.Equal(
            "{\"message\":\"EndOfStream\",\"last_seq_no\":2}",
            Encoding.UTF8.GetString(transport.SentText[1]));
        AssertEx.True(!stopTask.IsCompleted, "Stop completed before EndOfTranscript.");

        transport.EnqueueJson(
            "{\"message\":\"AddTranscript\",\"metadata\":{\"transcript\":\"xin chào\",\"start_time\":0.0,\"end_time\":0.8}}");
        var finalEvent = await finalEventTask.WaitAsync(TimeSpan.FromSeconds(1));
        AssertEx.Equal(SpeechEventKind.FinalTranscript, finalEvent.Kind);
        AssertEx.Equal("xin chào", finalEvent.Text);
        AssertEx.True(!stopTask.IsCompleted, "Stop completed before EndOfTranscript after final.");

        transport.EnqueueJson("{\"message\":\"EndOfTranscript\"}");
        await stopTask;
        AssertEx.True(transport.Closed, "Transport was not closed after EndOfTranscript.");
        AssertEx.Equal(SpeechmaticsSessionState.Stopped, session.State);
    });

    [KeyinaTest("Speechmatics provider errors fault the session without exposing transcript text")]
    private static void ProviderErrorFaultsSession() => Run(async () =>
    {
        var transport = new FakeSpeechmaticsTransport();
        await using var session = CreateSession(transport);
        await StartAsync(session, transport);

        transport.EnqueueJson(
            "{\"message\":\"Error\",\"type\":\"not_authorised\",\"reason\":\"invalid token\"}");
        var providerEvent = await session.ReadEventAsync(CancellationToken.None)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        AssertEx.Equal(SpeechEventKind.ProviderError, providerEvent.Kind);
        await WaitUntilAsync(() => session.State == SpeechmaticsSessionState.Faulted);
        await AssertThrowsAsync<SpeechmaticsSessionException>(() =>
            session.SendAudioAsync(new byte[2], CancellationToken.None));
    });

    [KeyinaTest("Speechmatics unexpected close and cancellation do not hang the caller")]
    private static void CloseAndCancellationAreBounded() => Run(async () =>
    {
        var closeTransport = new FakeSpeechmaticsTransport();
        await using (var closeSession = CreateSession(closeTransport))
        {
            await StartAsync(closeSession, closeTransport);
            closeTransport.EnqueueClosed("network lost");
            await WaitUntilAsync(() => closeSession.State == SpeechmaticsSessionState.Faulted);
            await AssertThrowsAsync<SpeechmaticsSessionException>(() =>
                closeSession.SendAudioAsync(new byte[2], CancellationToken.None));
        }

        var cancelTransport = new FakeSpeechmaticsTransport();
        await using var cancelSession = CreateSession(cancelTransport);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await AssertThrowsAsync<OperationCanceledException>(() =>
            cancelSession.StartAsync(cancellation.Token));
    });

    [KeyinaTest("Speechmatics disposal closes and disposes transport idempotently")]
    private static void DisposalReleasesTransport() => Run(async () =>
    {
        var transport = new FakeSpeechmaticsTransport();
        var session = CreateSession(transport);
        await StartAsync(session, transport);

        await session.DisposeAsync();
        await session.DisposeAsync();
        AssertEx.True(transport.Closed, "Dispose did not close the transport.");
        AssertEx.True(transport.Disposed, "Dispose did not dispose the transport.");
        AssertEx.Equal(SpeechmaticsSessionState.Disposed, session.State);
    });

    private static SpeechmaticsRealtimeSession CreateSession(FakeSpeechmaticsTransport transport) =>
        new(SpeechmaticsOptions.VietnameseDefault, transport, "test-token");

    private static async Task StartAsync(
        SpeechmaticsRealtimeSession session,
        FakeSpeechmaticsTransport transport)
    {
        var startTask = session.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => transport.SentText.Count == 1);
        transport.EnqueueJson("{\"message\":\"RecognitionStarted\",\"id\":\"session-1\"}");
        await startTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<ValueTask> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();
}
