using Keyina.Host.Core;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Speech;
using Keyina.Host.Windows.Audio;
using Keyina.Speechmatics;

namespace Keyina.Host.Speech;

public sealed class DictationCoordinator : IAsyncDisposable
{
    private readonly ISpeechmaticsSessionFactory sessionFactory;
    private readonly IAudioCapture audioCapture;
    private readonly IIpcEnvelopeWriter envelopeWriter;
    private readonly DictationOverlayModel overlay;
    private readonly Action<HostEvent> publishHostEvent;
    private readonly Func<ulong> focusGenerationProvider;
    private readonly TimeSpan finalizationTimeout;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly TranscriptAggregator aggregator = new();

    private ISpeechmaticsRealtimeSession? session;
    private CancellationTokenSource? audioCancellation;
    private CancellationTokenSource? eventCancellation;
    private Task? audioPump;
    private Task? eventPump;
    private IpcSessionId sessionId;
    private bool active;
    private bool disposed;

    public DictationCoordinator(
        ISpeechmaticsSessionFactory sessionFactory,
        IAudioCapture audioCapture,
        IIpcEnvelopeWriter envelopeWriter,
        DictationOverlayModel overlay,
        Action<HostEvent> publishHostEvent,
        Func<ulong> focusGenerationProvider,
        TimeSpan finalizationTimeout)
    {
        this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        this.audioCapture = audioCapture ?? throw new ArgumentNullException(nameof(audioCapture));
        this.envelopeWriter = envelopeWriter ?? throw new ArgumentNullException(nameof(envelopeWriter));
        this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        this.publishHostEvent = publishHostEvent ?? throw new ArgumentNullException(nameof(publishHostEvent));
        this.focusGenerationProvider = focusGenerationProvider ??
            throw new ArgumentNullException(nameof(focusGenerationProvider));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            finalizationTimeout,
            TimeSpan.Zero);
        this.finalizationTimeout = finalizationTimeout;
    }

    public DictationState State => overlay.State;

    public async Task StartAsync(
        string apiKey,
        IpcSessionId sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active)
            {
                throw new InvalidOperationException("Dictation is already active.");
            }

            aggregator.Reset();
            overlay.Apply(new DictationEvent.StartRequested());
            this.sessionId = sessionId;
            session = sessionFactory.Create(apiKey);
            audioCancellation = new CancellationTokenSource();
            eventCancellation = new CancellationTokenSource();

            try
            {
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                overlay.Apply(new DictationEvent.RecognitionStarted());
                publishHostEvent(new ListeningStarted());
                active = true;
                eventPump = PumpEventsAsync(session, eventCancellation.Token);
                audioPump = PumpAudioAsync(session, audioCancellation.Token);
            }
            catch (Exception exception)
            {
                await FailStartAsync(exception).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!active || session is null)
            {
                throw new InvalidOperationException("Dictation is not active.");
            }

            if (overlay.State.Status == DictationStatus.Error)
            {
                await CleanupActiveSessionAsync(cancelled: false).ConfigureAwait(false);
                return;
            }

            overlay.Apply(new DictationEvent.StopRequested());
            audioCancellation?.Cancel();
            await AwaitCancellationAsync(audioPump).ConfigureAwait(false);

            await session.StopAsync(finalizationTimeout, cancellationToken)
                .ConfigureAwait(false);

            eventCancellation?.Cancel();
            await AwaitCancellationAsync(eventPump).ConfigureAwait(false);
            overlay.Apply(new DictationEvent.FinalInserted());
            publishHostEvent(new ListeningStopped());
            await CleanupActiveSessionAsync(cancelled: false).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and
            not InvalidOperationException)
        {
            PublishFailure("speech_stop_failed");
            await CleanupActiveSessionAsync(cancelled: false).ConfigureAwait(false);
            throw;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task CancelAsync()
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!active)
            {
                return;
            }

            if (overlay.State.Status is DictationStatus.Connecting or
                DictationStatus.Listening or DictationStatus.Finalizing)
            {
                overlay.Apply(new DictationEvent.Cancelled());
            }

            publishHostEvent(new ListeningStopped());
            await CleanupActiveSessionAsync(cancelled: true).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref disposed))
        {
            return;
        }

        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (active)
            {
                await CleanupActiveSessionAsync(cancelled: true).ConfigureAwait(false);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task PumpAudioAsync(
        ISpeechmaticsRealtimeSession currentSession,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var audio in audioCapture.CaptureAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                await currentSession.SendAudioAsync(audio, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (AudioCaptureException exception)
        {
            PublishFailure($"audio_{exception.Error.ToString().ToLowerInvariant()}");
            eventCancellation?.Cancel();
        }
        catch (Exception)
        {
            PublishFailure("audio_unexpected");
            eventCancellation?.Cancel();
        }
    }

    private async Task PumpEventsAsync(
        ISpeechmaticsRealtimeSession currentSession,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var speechEvent = await currentSession.ReadEventAsync(cancellationToken)
                    .ConfigureAwait(false);
                switch (speechEvent.Kind)
                {
                    case SpeechEventKind.PartialTranscript:
                        HandlePartial(speechEvent);
                        break;

                    case SpeechEventKind.FinalTranscript:
                        await HandleFinalAsync(speechEvent, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case SpeechEventKind.ProviderError:
                        PublishFailure("speech_provider_error");
                        audioCancellation?.Cancel();
                        return;

                    case SpeechEventKind.ProviderWarning:
                    case SpeechEventKind.ProviderInfo:
                    case SpeechEventKind.Unknown:
                        break;

                    default:
                        throw new SpeechmaticsSessionException(
                            $"Unexpected coordinator event: {speechEvent.Kind}.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            PublishFailure("speech_receive_failed");
            audioCancellation?.Cancel();
        }
    }

    private void HandlePartial(SpeechEvent speechEvent)
    {
        var update = aggregator.Apply(ToTranscriptEvent(speechEvent), sessionId, 0);
        overlay.Apply(new DictationEvent.PartialUpdated(update.PartialText));
    }

    private async ValueTask HandleFinalAsync(
        SpeechEvent speechEvent,
        CancellationToken cancellationToken)
    {
        var update = aggregator.Apply(
            ToTranscriptEvent(speechEvent),
            sessionId,
            focusGenerationProvider());
        if (update.FinalEnvelope is null)
        {
            return;
        }

        await envelopeWriter.WriteAsync(update.FinalEnvelope, cancellationToken)
            .ConfigureAwait(false);
        overlay.Apply(new DictationEvent.FinalReceived());
    }

    private static TranscriptEvent ToTranscriptEvent(SpeechEvent speechEvent) =>
        new(
            speechEvent.Kind == SpeechEventKind.PartialTranscript
                ? TranscriptEventKind.Partial
                : TranscriptEventKind.Final,
            speechEvent.Text,
            speechEvent.StartTimeSeconds,
            speechEvent.EndTimeSeconds);

    private void PublishFailure(string errorCode)
    {
        var status = overlay.State.Status;
        if (status is DictationStatus.Connecting or
            DictationStatus.Listening or
            DictationStatus.Finalizing)
        {
            overlay.Apply(new DictationEvent.Failed(errorCode));
        }

        publishHostEvent(new ListeningStopped());
        publishHostEvent(new HostFailed(errorCode));
    }

    private async Task FailStartAsync(Exception exception)
    {
        var code = exception switch
        {
            AudioCaptureException audio =>
                $"audio_{audio.Error.ToString().ToLowerInvariant()}",
            _ => "speech_start_failed",
        };
        PublishFailure(code);
        await CleanupActiveSessionAsync(cancelled: false).ConfigureAwait(false);
    }

    private async Task CleanupActiveSessionAsync(bool cancelled)
    {
        audioCancellation?.Cancel();
        eventCancellation?.Cancel();
        await AwaitCancellationAsync(audioPump).ConfigureAwait(false);
        await AwaitCancellationAsync(eventPump).ConfigureAwait(false);

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        audioCancellation?.Dispose();
        eventCancellation?.Dispose();
        audioCancellation = null;
        eventCancellation = null;
        audioPump = null;
        eventPump = null;
        session = null;
        active = false;

        if (cancelled && overlay.State.Status is DictationStatus.Listening or DictationStatus.Finalizing)
        {
            overlay.Apply(new DictationEvent.Cancelled());
        }
    }

    private static async Task AwaitCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
