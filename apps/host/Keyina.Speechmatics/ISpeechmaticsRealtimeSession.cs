namespace Keyina.Speechmatics;

public interface ISpeechmaticsRealtimeSession : IAsyncDisposable
{
    SpeechmaticsSessionState State { get; }

    Task StartAsync(CancellationToken cancellationToken);

    ValueTask SendAudioAsync(
        ReadOnlyMemory<byte> audio,
        CancellationToken cancellationToken);

    ValueTask<SpeechEvent> ReadEventAsync(CancellationToken cancellationToken);

    Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
