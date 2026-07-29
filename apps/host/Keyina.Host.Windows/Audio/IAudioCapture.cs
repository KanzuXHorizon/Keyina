namespace Keyina.Host.Windows.Audio;

public enum AudioCaptureError
{
    NoDevice,
    PermissionDenied,
    DeviceRemoved,
    BufferOverflow,
    UnsupportedFormat,
    Unexpected,
}

public sealed class AudioCaptureException : Exception
{
    public AudioCaptureException(AudioCaptureError error, string message)
        : base(message)
    {
        Error = error;
    }

    public AudioCaptureException(AudioCaptureError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public AudioCaptureError Error { get; }
}

public sealed class AudioDataAvailableEventArgs(ReadOnlyMemory<byte> data) : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}

public sealed class AudioCaptureStoppedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}

public interface IAudioCaptureBackendFactory
{
    IAudioCaptureBackend Create();
}

public interface IAudioCaptureBackend : IDisposable
{
    AudioSourceFormat Format { get; }

    event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

    event EventHandler<AudioCaptureStoppedEventArgs>? StoppedEvent;

    void Start();

    void StopCapture();
}

public interface IAudioCapture
{
    IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(CancellationToken cancellationToken);
}
