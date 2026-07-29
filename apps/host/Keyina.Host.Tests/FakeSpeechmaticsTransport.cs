using System.Threading.Channels;
using Keyina.Speechmatics;

namespace Keyina.Host.Tests;

internal sealed class FakeSpeechmaticsTransport : ISpeechmaticsTransport
{
    private readonly Channel<SpeechTransportMessage> incoming = Channel.CreateUnbounded<SpeechTransportMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly List<byte[]> sentText = [];
    private readonly List<byte[]> sentBinary = [];

    public Uri? ConnectedEndpoint { get; private set; }

    public string? AuthorizationHeader { get; private set; }

    public IReadOnlyList<byte[]> SentText => sentText;

    public IReadOnlyList<byte[]> SentBinary => sentBinary;

    public bool Closed { get; private set; }

    public bool Disposed { get; private set; }

    public Task ConnectAsync(Uri endpoint, string authorizationHeader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectedEndpoint = endpoint;
        AuthorizationHeader = authorizationHeader;
        return Task.CompletedTask;
    }

    public ValueTask SendTextAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sentText.Add(utf8Json.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sentBinary.Add(audio.ToArray());
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<SpeechTransportMessage> ReceiveAsync(CancellationToken cancellationToken) =>
        incoming.Reader.ReadAllAsync(cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Closed = true;
        incoming.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public void EnqueueJson(string json) =>
        incoming.Writer.TryWrite(SpeechTransportMessage.Text(System.Text.Encoding.UTF8.GetBytes(json)));

    public void EnqueueClosed(string? reason = null) =>
        incoming.Writer.TryWrite(SpeechTransportMessage.Closed(reason));
}
