namespace Keyina.Speechmatics;

public enum SpeechTransportMessageKind
{
    Text,
    Closed,
}

public sealed record SpeechTransportMessage
{
    private SpeechTransportMessage(
        SpeechTransportMessageKind kind,
        ReadOnlyMemory<byte> payload,
        string? reason)
    {
        Kind = kind;
        Payload = payload;
        Reason = reason;
    }

    public SpeechTransportMessageKind Kind { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public string? Reason { get; }

    public static SpeechTransportMessage Text(ReadOnlyMemory<byte> payload) =>
        new(SpeechTransportMessageKind.Text, payload, null);

    public static SpeechTransportMessage Closed(string? reason) =>
        new(SpeechTransportMessageKind.Closed, ReadOnlyMemory<byte>.Empty, reason);
}

public interface ISpeechmaticsTransport : IAsyncDisposable
{
    Task ConnectAsync(
        Uri endpoint,
        string authorizationHeader,
        CancellationToken cancellationToken);

    ValueTask SendTextAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken);

    ValueTask SendBinaryAsync(
        ReadOnlyMemory<byte> audio,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SpeechTransportMessage> ReceiveAsync(
        CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}
