using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Keyina.Host.Windows.Audio;

internal sealed class NAudioCaptureBackendFactory : IAudioCaptureBackendFactory
{
    public IAudioCaptureBackend Create() => new NAudioCaptureBackend();
}

internal sealed class NAudioCaptureBackend : IAudioCaptureBackend
{
    private readonly WasapiCapture capture;
    private bool disposed;

    public NAudioCaptureBackend()
    {
        capture = new WasapiCapture();
        try
        {
            Format = MapFormat(capture.WaveFormat);
            capture.DataAvailable += HandleDataAvailable;
            capture.RecordingStopped += HandleRecordingStopped;
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    public AudioSourceFormat Format { get; }

    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

    public event EventHandler<AudioCaptureStoppedEventArgs>? StoppedEvent;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        capture.StartRecording();
    }

    public void StopCapture()
    {
        if (disposed)
        {
            return;
        }

        capture.StopRecording();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        capture.DataAvailable -= HandleDataAvailable;
        capture.RecordingStopped -= HandleRecordingStopped;
        capture.Dispose();
    }

    private static AudioSourceFormat MapFormat(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var encoding = format.Encoding switch
        {
            WaveFormatEncoding.Pcm when format.BitsPerSample == 16 =>
                AudioSampleEncoding.Pcm16,
            WaveFormatEncoding.IeeeFloat when format.BitsPerSample == 32 =>
                AudioSampleEncoding.IeeeFloat,
            _ => throw new AudioCaptureException(
                AudioCaptureError.UnsupportedFormat,
                $"Unsupported WASAPI format: {format.Encoding}, {format.BitsPerSample}-bit."),
        };

        return new AudioSourceFormat(format.SampleRate, format.Channels, encoding);
    }

    private void HandleDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded <= 0)
        {
            return;
        }

        DataAvailable?.Invoke(
            this,
            new AudioDataAvailableEventArgs(
                eventArgs.Buffer.AsMemory(0, eventArgs.BytesRecorded)));
    }

    private void HandleRecordingStopped(object? sender, StoppedEventArgs eventArgs) =>
        StoppedEvent?.Invoke(this, new AudioCaptureStoppedEventArgs(eventArgs.Exception));
}
