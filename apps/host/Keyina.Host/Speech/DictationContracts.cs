using Keyina.Host.Core.Ipc;
using Keyina.Speechmatics;

namespace Keyina.Host.Speech;

public interface ISpeechmaticsSessionFactory
{
    ISpeechmaticsRealtimeSession Create(string apiKey);
}

public interface IIpcEnvelopeWriter
{
    ValueTask WriteAsync(IpcEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class SpeechmaticsSessionFactory : ISpeechmaticsSessionFactory
{
    private readonly SpeechmaticsOptions options;
    private readonly Func<ISpeechmaticsTransport> transportFactory;

    public SpeechmaticsSessionFactory(
        SpeechmaticsOptions? options = null,
        Func<ISpeechmaticsTransport>? transportFactory = null)
    {
        this.options = options ?? SpeechmaticsOptions.VietnameseDefault;
        this.options.Validate();
        this.transportFactory = transportFactory ?? (() => new ClientWebSocketTransport());
    }

    public ISpeechmaticsRealtimeSession Create(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return new SpeechmaticsRealtimeSession(options, transportFactory(), apiKey);
    }
}
