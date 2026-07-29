using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Keyina.Speechmatics;

public enum SpeechmaticsSessionState
{
    Created,
    Starting,
    Started,
    Stopping,
    Stopped,
    Faulted,
    Disposed,
}

public sealed class SpeechmaticsSessionException : Exception
{
    public SpeechmaticsSessionException(string message)
        : base(message)
    {
    }

    public SpeechmaticsSessionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class SpeechmaticsRealtimeSession : ISpeechmaticsRealtimeSession
{
    public const int MaximumOutstandingAudioChunks = 500;

    private const int EventQueueCapacity = 256;

    private readonly SpeechmaticsOptions options;
    private readonly ISpeechmaticsTransport transport;
    private readonly string apiKey;
    private readonly SemaphoreSlim outstandingSlots = new(MaximumOutstandingAudioChunks);
    private readonly ConcurrentDictionary<int, byte> outstandingSequences = new();
    private readonly Channel<SpeechEvent> events = Channel.CreateBounded<SpeechEvent>(
        new BoundedChannelOptions(EventQueueCapacity)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    private readonly CancellationTokenSource receiveCancellation = new();
    private readonly TaskCompletionSource recognitionStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource endOfTranscript =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object stateGate = new();

    private Task? receiveTask;
    private int lastSequenceNumber;
    private SpeechmaticsSessionState state = SpeechmaticsSessionState.Created;
    private bool disposed;

    public SpeechmaticsRealtimeSession(
        SpeechmaticsOptions options,
        ISpeechmaticsTransport transport,
        string apiKey)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        this.apiKey = apiKey;
        options.Validate();
    }

    public SpeechmaticsSessionState State
    {
        get
        {
            lock (stateGate)
            {
                return state;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SetState(SpeechmaticsSessionState.Created, SpeechmaticsSessionState.Starting);

        try
        {
            await transport.ConnectAsync(
                options.Endpoint,
                $"Bearer {apiKey}",
                cancellationToken).ConfigureAwait(false);

            receiveTask = ReceiveLoopAsync(receiveCancellation.Token);
            await transport.SendTextAsync(
                SpeechmaticsProtocol.CreateStartRecognition(options),
                cancellationToken).ConfigureAwait(false);

            await recognitionStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Fault(new SpeechmaticsSessionException("Speechmatics start was cancelled."));
            throw;
        }
        catch (Exception exception) when (exception is not SpeechmaticsSessionException)
        {
            var wrapped = new SpeechmaticsSessionException(
                "Speechmatics session failed during startup.",
                exception);
            Fault(wrapped);
            throw wrapped;
        }
    }

    public async ValueTask SendAudioAsync(
        ReadOnlyMemory<byte> audio,
        CancellationToken cancellationToken)
    {
        EnsureState(SpeechmaticsSessionState.Started);
        if (audio.IsEmpty || (audio.Length & 1) != 0)
        {
            throw new ArgumentException(
                "Speechmatics audio must contain a non-empty even number of PCM 16-bit bytes.",
                nameof(audio));
        }

        if (audio.Length > options.ChunkSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audio),
                $"Audio chunk cannot exceed {options.ChunkSizeBytes} bytes.");
        }

        await outstandingSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var sequenceNumber = 0;
        try
        {
            EnsureState(SpeechmaticsSessionState.Started);
            sequenceNumber = Interlocked.Increment(ref lastSequenceNumber);
            if (!outstandingSequences.TryAdd(sequenceNumber, 0))
            {
                throw new SpeechmaticsSessionException("Duplicate audio sequence number detected.");
            }

            await transport.SendBinaryAsync(audio, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (sequenceNumber != 0 && outstandingSequences.TryRemove(sequenceNumber, out _))
            {
                outstandingSlots.Release();
            }
            else if (sequenceNumber == 0)
            {
                outstandingSlots.Release();
            }

            throw;
        }
    }

    public ValueTask<SpeechEvent> ReadEventAsync(CancellationToken cancellationToken) =>
        events.Reader.ReadAsync(cancellationToken);

    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        SetState(SpeechmaticsSessionState.Started, SpeechmaticsSessionState.Stopping);
        try
        {
            await transport.SendTextAsync(
                SpeechmaticsProtocol.CreateEndOfStream(Volatile.Read(ref lastSequenceNumber)),
                cancellationToken).ConfigureAwait(false);

            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
            try
            {
                await endOfTranscript.Task.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                var timeoutException = new TimeoutException(
                    "Speechmatics did not return EndOfTranscript before the finalization timeout.");
                Fault(timeoutException);
                throw timeoutException;
            }

            await transport.CloseAsync(cancellationToken).ConfigureAwait(false);
            lock (stateGate)
            {
                if (state != SpeechmaticsSessionState.Faulted)
                {
                    state = SpeechmaticsSessionState.Stopped;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Fault(new SpeechmaticsSessionException("Speechmatics stop was cancelled."));
            throw;
        }
        catch (Exception exception) when (
            exception is not SpeechmaticsSessionException and not TimeoutException)
        {
            var wrapped = new SpeechmaticsSessionException(
                "Speechmatics session failed during shutdown.",
                exception);
            Fault(wrapped);
            throw wrapped;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            state = SpeechmaticsSessionState.Disposed;
        }

        receiveCancellation.Cancel();
        try
        {
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Disposal is best-effort and must remain idempotent.
        }

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SpeechmaticsSessionException)
            {
            }
        }

        events.Writer.TryComplete();
        await transport.DisposeAsync().ConfigureAwait(false);
        receiveCancellation.Dispose();
        outstandingSlots.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var transportMessage in transport.ReceiveAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (transportMessage.Kind == SpeechTransportMessageKind.Closed)
                {
                    if (State is not SpeechmaticsSessionState.Stopping and
                        not SpeechmaticsSessionState.Stopped and
                        not SpeechmaticsSessionState.Disposed)
                    {
                        Fault(new SpeechmaticsSessionException(
                            $"Speechmatics connection closed unexpectedly: {transportMessage.Reason ?? "no reason"}."));
                    }
                    return;
                }

                var speechEvent = SpeechmaticsProtocol.ParseServerMessage(
                    transportMessage.Payload.Span);
                switch (speechEvent.Kind)
                {
                    case SpeechEventKind.RecognitionStarted:
                        lock (stateGate)
                        {
                            if (state != SpeechmaticsSessionState.Starting)
                            {
                                throw new SpeechmaticsSessionException(
                                    "RecognitionStarted arrived in an invalid session state.");
                            }
                            state = SpeechmaticsSessionState.Started;
                        }
                        recognitionStarted.TrySetResult();
                        break;

                    case SpeechEventKind.AudioAdded:
                        AcknowledgeAudio(speechEvent.SequenceNumber);
                        break;

                    case SpeechEventKind.EndOfTranscript:
                        endOfTranscript.TrySetResult();
                        break;

                    case SpeechEventKind.ProviderError:
                        await events.Writer.WriteAsync(speechEvent, cancellationToken)
                            .ConfigureAwait(false);
                        Fault(new SpeechmaticsSessionException(
                            $"Speechmatics provider error: {speechEvent.ProviderType ?? "unknown"}."));
                        return;

                    case SpeechEventKind.PartialTranscript:
                    case SpeechEventKind.FinalTranscript:
                    case SpeechEventKind.ProviderWarning:
                    case SpeechEventKind.ProviderInfo:
                    case SpeechEventKind.Unknown:
                        await events.Writer.WriteAsync(speechEvent, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    default:
                        throw new SpeechmaticsSessionException(
                            $"Unhandled Speechmatics event kind: {speechEvent.Kind}.");
                }
            }

            if (!cancellationToken.IsCancellationRequested &&
                State is SpeechmaticsSessionState.Starting or SpeechmaticsSessionState.Started)
            {
                Fault(new SpeechmaticsSessionException(
                    "Speechmatics receive stream ended unexpectedly."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var failure = exception as SpeechmaticsSessionException ??
                new SpeechmaticsSessionException("Speechmatics receive loop failed.", exception);
            Fault(failure);
        }
    }

    private void AcknowledgeAudio(int? sequenceNumber)
    {
        if (sequenceNumber is null ||
            !outstandingSequences.TryRemove(sequenceNumber.Value, out _))
        {
            throw new SpeechmaticsSessionException(
                "Speechmatics acknowledged an unknown audio sequence number.");
        }

        outstandingSlots.Release();
    }

    private void Fault(Exception exception)
    {
        lock (stateGate)
        {
            if (state == SpeechmaticsSessionState.Disposed)
            {
                return;
            }

            state = SpeechmaticsSessionState.Faulted;
        }

        recognitionStarted.TrySetException(exception);
        endOfTranscript.TrySetException(exception);
    }

    private void EnsureState(SpeechmaticsSessionState requiredState)
    {
        lock (stateGate)
        {
            if (state == SpeechmaticsSessionState.Faulted)
            {
                throw new SpeechmaticsSessionException("Speechmatics session is faulted.");
            }

            if (state != requiredState)
            {
                throw new InvalidOperationException(
                    $"Speechmatics session must be {requiredState}, but is {state}.");
            }
        }
    }

    private void SetState(
        SpeechmaticsSessionState expectedState,
        SpeechmaticsSessionState nextState)
    {
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (state != expectedState)
            {
                throw new InvalidOperationException(
                    $"Speechmatics session must be {expectedState}, but is {state}.");
            }

            state = nextState;
        }
    }
}
