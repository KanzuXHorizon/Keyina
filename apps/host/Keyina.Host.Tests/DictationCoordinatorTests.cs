using System.Threading.Channels;
using Keyina.Host.Core;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Speech;
using Keyina.Host.Windows.Audio;
using Keyina.Speechmatics;
using Keyina.Host.Speech;

namespace Keyina.Host.Tests;

internal static class DictationCoordinatorTests
{
    [KeyinaTest("dictation coordinator keeps partials overlay-only and emits one IPC per final")]
    private static void PartialsAndFinalsAreSeparated() => Run(async () =>
    {
        var session = new FakeRealtimeSession();
        var audio = new FakeAudioCapture();
        var writer = new FakeEnvelopeWriter();
        var overlay = new DictationOverlayModel();
        var hostState = HostState.Initial;
        var coordinator = CreateCoordinator(
            session,
            audio,
            writer,
            overlay,
            hostEvent => hostState = HostReducer.Reduce(hostState, hostEvent));
        var sessionId = new IpcSessionId(10, 20);

        await coordinator.StartAsync("test-key", sessionId, CancellationToken.None);
        AssertEx.Equal(DictationStatus.Listening, overlay.State.Status);
        AssertEx.True(hostState.Listening, "Host did not enter listening state.");

        audio.Emit(new byte[] { 1, 2, 3, 4 });
        await WaitUntilAsync(() => session.AudioChunks.Count == 1);

        session.Emit(Partial("xin chao"));
        await WaitUntilAsync(() => overlay.State.PartialText == "xin chao");
        AssertEx.Equal(0, writer.Envelopes.Count);

        var final = Final("xin chào", 0.0, 0.8);
        session.Emit(final);
        session.Emit(final);
        await WaitUntilAsync(() => writer.Envelopes.Count == 1);
        AssertEx.Equal(IpcMessageType.FinalTranscript, writer.Envelopes[0].MessageType);
        AssertEx.Equal("xin chào", writer.Envelopes[0].Payload);
        AssertEx.Equal<ulong>(77, writer.Envelopes[0].FocusGeneration);

        var stopTask = coordinator.StopAsync(CancellationToken.None);
        await WaitUntilAsync(() => session.StopCalls == 1);
        await stopTask;
        AssertEx.Equal(DictationStatus.Inserted, overlay.State.Status);
        AssertEx.True(!hostState.Listening, "Host remained in listening state after stop.");
    });

    [KeyinaTest("dictation coordinator speech failures do not disable Vietnamese input")]
    private static void SpeechFailureIsIsolatedFromInputMode() => Run(async () =>
    {
        var session = new FakeRealtimeSession();
        var hostState = HostState.Initial;
        var coordinator = CreateCoordinator(
            session,
            new FakeAudioCapture(),
            new FakeEnvelopeWriter(),
            new DictationOverlayModel(),
            hostEvent => hostState = HostReducer.Reduce(hostState, hostEvent));

        await coordinator.StartAsync(
            "test-key",
            new IpcSessionId(1, 2),
            CancellationToken.None);
        session.Emit(new SpeechEvent
        {
            Kind = SpeechEventKind.ProviderError,
            ProviderType = "not_authorised",
            Reason = "redacted",
        });

        await WaitUntilAsync(() => coordinator.State.Status == DictationStatus.Error);
        AssertEx.True(hostState.VietnameseEnabled, "Speech failure disabled native Vietnamese input.");
        AssertEx.True(!hostState.Listening, "Speech failure left listening state active.");
        AssertEx.Equal("speech_provider_error", hostState.ErrorCode);
    });

    [KeyinaTest("dictation coordinator disposal is idempotent and releases active work")]
    private static void DisposalIsIdempotent() => Run(async () =>
    {
        var session = new FakeRealtimeSession();
        var audio = new FakeAudioCapture();
        var coordinator = CreateCoordinator(
            session,
            audio,
            new FakeEnvelopeWriter(),
            new DictationOverlayModel(),
            _ => { });

        await coordinator.StartAsync(
            "test-key",
            new IpcSessionId(8, 9),
            CancellationToken.None);
        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();

        AssertEx.True(audio.Cancelled, "Disposal did not cancel audio capture.");
        AssertEx.Equal(SpeechmaticsSessionState.Disposed, session.State);
    });

    [KeyinaTest("dictation coordinator cancellation stops audio and does not emit transcript IPC")]
    private static void CancellationIsBounded() => Run(async () =>
    {
        var session = new FakeRealtimeSession();
        var audio = new FakeAudioCapture();
        var writer = new FakeEnvelopeWriter();
        var overlay = new DictationOverlayModel();
        var coordinator = CreateCoordinator(session, audio, writer, overlay, _ => { });

        await coordinator.StartAsync(
            "test-key",
            new IpcSessionId(3, 4),
            CancellationToken.None);
        await coordinator.CancelAsync();

        AssertEx.Equal(DictationStatus.Cancelled, overlay.State.Status);
        AssertEx.True(audio.Cancelled, "Audio capture was not cancelled.");
        AssertEx.Equal(0, writer.Envelopes.Count);
    });

    private static DictationCoordinator CreateCoordinator(
        FakeRealtimeSession session,
        FakeAudioCapture audio,
        FakeEnvelopeWriter writer,
        DictationOverlayModel overlay,
        Action<HostEvent> hostEvent) =>
        new(
            new FakeSessionFactory(session),
            audio,
            writer,
            overlay,
            hostEvent,
            focusGenerationProvider: () => 77,
            finalizationTimeout: TimeSpan.FromSeconds(1));

    private static SpeechEvent Partial(string text) => new()
    {
        Kind = SpeechEventKind.PartialTranscript,
        Text = text,
        StartTimeSeconds = 0,
        EndTimeSeconds = 0.5,
    };

    private static SpeechEvent Final(string text, double start, double end) => new()
    {
        Kind = SpeechEventKind.FinalTranscript,
        Text = text,
        StartTimeSeconds = start,
        EndTimeSeconds = end,
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();

    private sealed class FakeSessionFactory(FakeRealtimeSession session)
        : ISpeechmaticsSessionFactory
    {
        public ISpeechmaticsRealtimeSession Create(string apiKey)
        {
            AssertEx.Equal("test-key", apiKey);
            return session;
        }
    }

    private sealed class FakeEnvelopeWriter : IIpcEnvelopeWriter
    {
        public List<IpcEnvelope> Envelopes { get; } = [];

        public ValueTask WriteAsync(IpcEnvelope envelope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Envelopes.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAudioCapture : IAudioCapture
    {
        private readonly Channel<ReadOnlyMemory<byte>> channel =
            Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

        public bool Cancelled { get; private set; }

        public IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
            CancellationToken cancellationToken) => ReadAsync(cancellationToken);

        public void Emit(byte[] audio) => channel.Writer.TryWrite(audio);

        private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var audio in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return audio;
                }
            }
            finally
            {
                Cancelled = cancellationToken.IsCancellationRequested;
            }
        }
    }

    private sealed class FakeRealtimeSession : ISpeechmaticsRealtimeSession
    {
        private readonly Channel<SpeechEvent> events = Channel.CreateUnbounded<SpeechEvent>();

        public List<byte[]> AudioChunks { get; } = [];

        public SpeechmaticsSessionState State { get; private set; } =
            SpeechmaticsSessionState.Created;

        public int StopCalls { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = SpeechmaticsSessionState.Started;
            return Task.CompletedTask;
        }

        public ValueTask SendAudioAsync(
            ReadOnlyMemory<byte> audio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioChunks.Add(audio.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask<SpeechEvent> ReadEventAsync(CancellationToken cancellationToken) =>
            events.Reader.ReadAsync(cancellationToken);

        public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCalls++;
            State = SpeechmaticsSessionState.Stopped;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            State = SpeechmaticsSessionState.Disposed;
            events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public void Emit(SpeechEvent speechEvent) => events.Writer.TryWrite(speechEvent);
    }
}
