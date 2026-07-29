using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Keyina.Host.Windows.Audio;

public sealed class WasapiMicrophoneCapture : IAudioCapture
{
    public const int DefaultMaximumBufferedBytes = 64_000;

    private readonly IAudioCaptureBackendFactory backendFactory;
    private readonly int maxBufferedBytes;

    public WasapiMicrophoneCapture()
        : this(new NAudioCaptureBackendFactory(), DefaultMaximumBufferedBytes)
    {
    }

    public WasapiMicrophoneCapture(
        IAudioCaptureBackendFactory backendFactory,
        int maxBufferedBytes = DefaultMaximumBufferedBytes)
    {
        this.backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBufferedBytes, 0);
        this.maxBufferedBytes = maxBufferedBytes;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAudioCaptureBackend backend;
        try
        {
            backend = backendFactory.Create();
        }
        catch (AudioCaptureException)
        {
            throw;
        }
        catch (COMException exception)
        {
            throw new AudioCaptureException(
                AudioCaptureError.NoDevice,
                "Windows did not provide a usable microphone device.",
                exception);
        }
        catch (Exception exception)
        {
            throw new AudioCaptureException(
                AudioCaptureError.Unexpected,
                "Microphone initialization failed.",
                exception);
        }

        using (backend)
        await using (var buffer = new BoundedAudioBuffer(maxBufferedBytes))
        {
            Pcm16MonoConverter converter;
            try
            {
                converter = new Pcm16MonoConverter(backend.Format);
            }
            catch (Exception exception) when (
                exception is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new AudioCaptureException(
                    AudioCaptureError.UnsupportedFormat,
                    "The default microphone format is not supported.",
                    exception);
            }

            void HandleData(object? sender, AudioDataAvailableEventArgs eventArgs)
            {
                try
                {
                    var converted = converter.Convert(eventArgs.Data.Span);
                    if (converted.Length != 0 && !buffer.TryWrite(converted))
                    {
                        buffer.Complete(new AudioCaptureException(
                            AudioCaptureError.BufferOverflow,
                            "Microphone capture exceeded the two-second buffer budget."));
                    }
                }
                catch (Exception exception)
                {
                    buffer.Complete(exception is AudioCaptureException
                        ? exception
                        : new AudioCaptureException(
                            AudioCaptureError.Unexpected,
                            "Microphone audio conversion failed.",
                            exception));
                }
            }

            void HandleStopped(object? sender, AudioCaptureStoppedEventArgs eventArgs)
            {
                buffer.Complete(eventArgs.Error is null
                    ? null
                    : new AudioCaptureException(
                        AudioCaptureError.DeviceRemoved,
                        "The active microphone stopped unexpectedly.",
                        eventArgs.Error));
            }

            backend.DataAvailable += HandleData;
            backend.StoppedEvent += HandleStopped;
            try
            {
                try
                {
                    backend.Start();
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new AudioCaptureException(
                        AudioCaptureError.PermissionDenied,
                        "Microphone access was denied by Windows privacy settings.",
                        exception);
                }
                catch (AudioCaptureException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new AudioCaptureException(
                        AudioCaptureError.Unexpected,
                        "Microphone capture could not start.",
                        exception);
                }

                while (true)
                {
                    ReadOnlyMemory<byte> audio;
                    try
                    {
                        audio = await buffer.ReadAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException exception)
                    {
                        if (exception.InnerException is AudioCaptureException captureException)
                        {
                            throw captureException;
                        }

                        if (exception.InnerException is AudioBufferOverflowException overflowException)
                        {
                            throw new AudioCaptureException(
                                AudioCaptureError.BufferOverflow,
                                "Microphone capture exceeded the two-second buffer budget.",
                                overflowException);
                        }

                        if (exception.InnerException is not null)
                        {
                            throw new AudioCaptureException(
                                AudioCaptureError.Unexpected,
                                "Microphone audio stream failed.",
                                exception.InnerException);
                        }

                        yield break;
                    }

                    yield return audio;
                }
            }
            finally
            {
                backend.DataAvailable -= HandleData;
                backend.StoppedEvent -= HandleStopped;
                try
                {
                    backend.StopCapture();
                }
                catch
                {
                    // A removed or denied device can reject stop; disposal still follows.
                }
            }
        }
    }
}
