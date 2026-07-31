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
    private TaskCompletionSource? transcriptCompleted;
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
            if (overlay.State.Status is DictationStatus.Error or
                DictationStatus.Cancelled or DictationStatus.Inserted)
            {
                overlay.Apply(new DictationEvent.Reset());
            }

            aggregator.Reset();
            overlay.Apply(new DictationEvent.StartRequested());
            this.sessionId = sessionId;

            try
            {
                session = sessionFactory.Create(apiKey);
                audioCancellation = new CancellationTokenSource();
                eventCancellation = new CancellationTokenSource();
                transcriptCompleted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
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

            var completion = transcriptCompleted ??
                throw new InvalidOperationException(
                    "Dictation transcript completion signal is unavailable.");
            await completion.Task.WaitAsync(finalizationTimeout, cancellationToken)
                .ConfigureAwait(false);
            await AwaitCancellationAsync(eventPump).ConfigureAwait(false);

            var finalEnvelope = aggregator.Complete(
                sessionId,
                focusGenerationProvider());
            if (finalEnvelope is not null)
            {
                await envelopeWriter.WriteAsync(finalEnvelope, cancellationToken)
                    .ConfigureAwait(false);
            }

            eventCancellation?.Cancel();
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
            ScheduleFailureCleanup();
        }
        catch (Exception)
        {
            PublishFailure("audio_unexpected");
            eventCancellation?.Cancel();
            ScheduleFailureCleanup();
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
                        if (!string.IsNullOrWhiteSpace(speechEvent.Text))
                        {
                            HandlePartial(speechEvent);
                        }
                        break;

                    case SpeechEventKind.FinalTranscript:
                        if (!string.IsNullOrWhiteSpace(speechEvent.Text))
                        {
                            HandleFinal(speechEvent);
                        }
                        break;

                    case SpeechEventKind.EndOfTranscript:
                        transcriptCompleted?.TrySetResult();
                        return;

                    case SpeechEventKind.ProviderError:
                        PublishFailure(
                            SpeechmaticsSessionException.IsAuthenticationProviderType(
                                speechEvent.ProviderType)
                                ? "speech_credential_invalid"
                                : "speech_provider_error");
                        audioCancellation?.Cancel();
                        ScheduleFailureCleanup();
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
            ScheduleFailureCleanup();
        }
    }

    private void HandlePartial(SpeechEvent speechEvent)
    {
        var update = aggregator.Apply(ToTranscriptEvent(speechEvent), sessionId, 0);
        overlay.Apply(new DictationEvent.PartialUpdated(update.PartialText));
    }

    private void HandleFinal(SpeechEvent speechEvent)
    {
        var update = aggregator.Apply(
            ToTranscriptEvent(speechEvent),
            sessionId,
            focusGeneration: 0);
        if (update.FinalOrdinal > overlay.State.FinalSegments)
        {
            overlay.Apply(new DictationEvent.FinalReceived());
        }
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
            SpeechmaticsSessionException speech when
                speech.IsAuthenticationFailure =>
                "speech_credential_invalid",
            _ => "speech_start_failed",
        };
        PublishFailure(code);
        await CleanupActiveSessionAsync(cancelled: false).ConfigureAwait(false);
    }

    private void ScheduleFailureCleanup() => _ = CleanupFailedSessionAsync();

    private async Task CleanupFailedSessionAsync()
    {
        try
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!disposed && active && overlay.State.Status == DictationStatus.Error)
                {
                    await CleanupActiveSessionAsync(cancelled: false)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }
        catch (Exception)
        {
            // Failure cleanup is best effort and must not fault a background pump.
        }
    }

    private async Task CleanupActiveSessionAsync(bool cancelled)
    {
        var currentAudioCancellation = audioCancellation;
        var currentEventCancellation = eventCancellation;
        var currentAudioPump = audioPump;
        var currentEventPump = eventPump;
        var currentTranscriptCompleted = transcriptCompleted;
        var currentSession = session;

        currentAudioCancellation?.Cancel();
        currentEventCancellation?.Cancel();
        try
        {
            await AwaitCancellationAsync(currentAudioPump).ConfigureAwait(false);
            await AwaitCancellationAsync(currentEventPump).ConfigureAwait(false);
            if (currentSession is not null)
            {
                await currentSession.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            currentAudioCancellation?.Dispose();
            currentEventCancellation?.Dispose();
            if (ReferenceEquals(audioCancellation, currentAudioCancellation))
            {
                audioCancellation = null;
            }
            if (ReferenceEquals(eventCancellation, currentEventCancellation))
            {
                eventCancellation = null;
            }
            if (ReferenceEquals(audioPump, currentAudioPump))
            {
                audioPump = null;
            }
            if (ReferenceEquals(eventPump, currentEventPump))
            {
                eventPump = null;
            }
            if (ReferenceEquals(transcriptCompleted, currentTranscriptCompleted))
            {
                transcriptCompleted = null;
            }
            if (ReferenceEquals(session, currentSession))
            {
                session = null;
                active = false;
            }
        }

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
