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
    private readonly Func<string>? languageProvider;
    private readonly Func<ISpeechmaticsTransport> transportFactory;

    public SpeechmaticsSessionFactory(
        SpeechmaticsOptions? options = null,
        Func<ISpeechmaticsTransport>? transportFactory = null,
        Func<string>? languageProvider = null)
    {
        this.options = options ?? SpeechmaticsOptions.MultilingualDefault;
        this.options.Validate();
        this.languageProvider = languageProvider;
        this.transportFactory = transportFactory ?? (() => new ClientWebSocketTransport());
    }

    public ISpeechmaticsRealtimeSession Create(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var sessionOptions = languageProvider is null
            ? options
            : options with
            {
                Language = Keyina.Host.Core.Speech.SpeechLanguageCatalog.Normalize(languageProvider()),
            };
        sessionOptions.Validate();
        return new SpeechmaticsRealtimeSession(sessionOptions, transportFactory(), apiKey);
    }
}
