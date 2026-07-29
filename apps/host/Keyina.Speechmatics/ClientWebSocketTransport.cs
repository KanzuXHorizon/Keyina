using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace Keyina.Speechmatics;

public sealed class ClientWebSocketTransport : ISpeechmaticsTransport
{
    private const int ReceiveBufferSize = 16 * 1024;
    private const int MaximumTextMessageBytes = 1024 * 1024;

    private readonly ClientWebSocket socket = new();
    private bool disposed;

    public async Task ConnectAsync(
        Uri endpoint,
        string authorizationHeader,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationHeader);

        if (socket.State != WebSocketState.None)
        {
            throw new InvalidOperationException("Speechmatics transport can only connect once.");
        }

        socket.Options.SetRequestHeader("Authorization", authorizationHeader);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SendTextAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOpen();
        return socket.SendAsync(
            utf8Json,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    public ValueTask SendBinaryAsync(
        ReadOnlyMemory<byte> audio,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOpen();
        return socket.SendAsync(
            audio,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken);
    }

    public async IAsyncEnumerable<SpeechTransportMessage> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOpen();

        var rented = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        var message = new ArrayBufferWriter<byte>(ReceiveBufferSize);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(
                    rented.AsMemory(0, ReceiveBufferSize),
                    cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield return SpeechTransportMessage.Closed(socket.CloseStatusDescription);
                    yield break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new SpeechmaticsSessionException(
                        "Speechmatics returned an unsupported binary server message.");
                }

                if (message.WrittenCount + result.Count > MaximumTextMessageBytes)
                {
                    throw new SpeechmaticsSessionException(
                        "Speechmatics server message exceeded the 1 MiB safety limit.");
                }

                message.Write(rented.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                yield return SpeechTransportMessage.Text(message.WrittenMemory.ToArray());
                message.Clear();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return;
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "Keyina dictation complete",
                cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        socket.Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException(
                $"Speechmatics WebSocket is not open (state: {socket.State}).");
        }
    }
}
