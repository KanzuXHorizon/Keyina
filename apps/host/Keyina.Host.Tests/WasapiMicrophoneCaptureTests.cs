using Keyina.Host.Windows.Audio;

namespace Keyina.Host.Tests;

internal static class WasapiMicrophoneCaptureTests
{
    [KeyinaTest("microphone capture maps missing device without hanging")]
    private static void MissingDeviceIsReported() => Run(async () =>
    {
        var capture = new WasapiMicrophoneCapture(
            new FakeAudioBackendFactory(() => throw new AudioCaptureException(
                AudioCaptureError.NoDevice,
                "No microphone.")));

        await AssertCaptureErrorAsync(capture, AudioCaptureError.NoDevice);
    });

    [KeyinaTest("microphone capture maps permission denial and disposes backend")]
    private static void PermissionDenialIsReported() => Run(async () =>
    {
        var backend = new FakeAudioBackend
        {
            StartException = new UnauthorizedAccessException("denied"),
        };
        var capture = new WasapiMicrophoneCapture(new FakeAudioBackendFactory(() => backend));

        await AssertCaptureErrorAsync(capture, AudioCaptureError.PermissionDenied);
        AssertEx.True(backend.Disposed, "Backend was not disposed after Start failed.");
    });

    [KeyinaTest("microphone capture converts data and reports device removal")]
    private static void DeviceRemovalIsReportedAfterQueuedAudio() => Run(async () =>
    {
        var backend = new FakeAudioBackend();
        backend.OnStart = instance =>
        {
            instance.EmitData(Pcm16Bytes(1_000, 2_000, 3_000));
            instance.EmitStopped(new InvalidOperationException("device removed"));
        };
        var capture = new WasapiMicrophoneCapture(new FakeAudioBackendFactory(() => backend));

        await using var enumerator = capture.CaptureAsync(CancellationToken.None).GetAsyncEnumerator();
        AssertEx.True(await enumerator.MoveNextAsync(), "Capture did not yield queued audio.");
        AssertEx.True(enumerator.Current.Span.SequenceEqual(Pcm16Bytes(1_000, 2_000)),
            "Capture changed PCM output before device removal.");

        try
        {
            await enumerator.MoveNextAsync();
        }
        catch (AudioCaptureException exception)
        {
            AssertEx.Equal(AudioCaptureError.DeviceRemoved, exception.Error);
            return;
        }

        throw new InvalidOperationException("Expected device removal error.");
    });

    [KeyinaTest("microphone capture cancels on two second queue overflow without dropping queued chunks")]
    private static void OverflowCancelsCapture() => Run(async () =>
    {
        var backend = new FakeAudioBackend();
        backend.OnStart = instance =>
        {
            instance.EmitData(new byte[32_000]);
            instance.EmitData(new byte[32_000]);
            instance.EmitData(new byte[4]);
        };
        var capture = new WasapiMicrophoneCapture(
            new FakeAudioBackendFactory(() => backend),
            maxBufferedBytes: 64_000);

        await using var enumerator = capture.CaptureAsync(CancellationToken.None).GetAsyncEnumerator();
        AssertEx.True(await enumerator.MoveNextAsync(), "First queued second was lost.");
        AssertEx.True(await enumerator.MoveNextAsync(), "Second queued second was lost.");

        try
        {
            await enumerator.MoveNextAsync();
        }
        catch (AudioCaptureException exception)
        {
            AssertEx.Equal(AudioCaptureError.BufferOverflow, exception.Error);
            return;
        }

        throw new InvalidOperationException("Expected buffer overflow error.");
    });

    [KeyinaTest("microphone capture cancellation stops and disposes the backend")]
    private static void CancellationStopsBackend() => Run(async () =>
    {
        var backend = new FakeAudioBackend();
        var capture = new WasapiMicrophoneCapture(new FakeAudioBackendFactory(() => backend));
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = capture.CaptureAsync(cancellation.Token).GetAsyncEnumerator();

        var pending = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => backend.Started);
        cancellation.Cancel();
        await AssertThrowsAsync<OperationCanceledException>(() => pending);
        AssertEx.True(backend.Stopped, "Cancellation did not stop the backend.");
        AssertEx.True(backend.Disposed, "Cancellation did not dispose the backend.");
    });

    private static async Task AssertCaptureErrorAsync(
        WasapiMicrophoneCapture capture,
        AudioCaptureError expected)
    {
        await using var enumerator = capture.CaptureAsync(CancellationToken.None).GetAsyncEnumerator();
        try
        {
            await enumerator.MoveNextAsync();
        }
        catch (AudioCaptureException exception)
        {
            AssertEx.Equal(expected, exception.Error);
            return;
        }

        throw new InvalidOperationException($"Expected {expected}.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
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

    private static byte[] Pcm16Bytes(params short[] values)
    {
        var bytes = new byte[values.Length * sizeof(short)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();

    private sealed class FakeAudioBackendFactory(Func<IAudioCaptureBackend> create)
        : IAudioCaptureBackendFactory
    {
        public IAudioCaptureBackend Create() => create();
    }

    private sealed class FakeAudioBackend : IAudioCaptureBackend
    {
        public AudioSourceFormat Format { get; } =
            new(16_000, 1, AudioSampleEncoding.Pcm16);

        public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

        public event EventHandler<AudioCaptureStoppedEventArgs>? StoppedEvent;

        public Exception? StartException { get; init; }

        public Action<FakeAudioBackend>? OnStart { get; set; }

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            if (StartException is not null)
            {
                throw StartException;
            }

            Started = true;
            OnStart?.Invoke(this);
        }

        public void StopCapture() => Stopped = true;

        public void Dispose() => Disposed = true;

        public void EmitData(byte[] data) =>
            DataAvailable?.Invoke(this, new AudioDataAvailableEventArgs(data));

        public void EmitStopped(Exception? error) =>
            StoppedEvent?.Invoke(this, new AudioCaptureStoppedEventArgs(error));
    }
}
