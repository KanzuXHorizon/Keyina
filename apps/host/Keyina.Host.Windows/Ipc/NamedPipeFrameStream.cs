using System.Buffers.Binary;
using Keyina.Host.Core.Ipc;

namespace Keyina.Host.Windows.Ipc;

public sealed class PipeProtocolException : Exception
{
    public PipeProtocolException(string message)
        : base(message)
    {
    }
}

public static class NamedPipeFrameProtocol
{
    public static async ValueTask<IpcEnvelope> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var frame = new byte[IpcFrameCodec.HeaderSize];
        await ReadExactlyAsync(
            stream,
            frame.AsMemory(),
            cancellationToken).ConfigureAwait(false);

        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(
            frame.AsSpan(10, 4));
        if (payloadLength > IpcFrameCodec.MaximumPayloadBytes)
        {
            throw new PipeProtocolException("Named-pipe frame exceeds the 64 KiB limit.");
        }

        var totalLength = IpcFrameCodec.HeaderSize + checked((int)payloadLength);
        if (totalLength != frame.Length)
        {
            Array.Resize(ref frame, totalLength);
            await ReadExactlyAsync(
                stream,
                frame.AsMemory(IpcFrameCodec.HeaderSize),
                cancellationToken).ConfigureAwait(false);
        }

        var status = IpcFrameCodec.TryDecode(
            frame,
            out var envelope,
            out var consumed,
            out var error);
        if (status != IpcDecodeStatus.Success ||
            envelope is null ||
            consumed != frame.Length)
        {
            throw new PipeProtocolException(
                $"Named-pipe frame is invalid ({status}, {error}).");
        }

        return envelope;
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var frame = IpcFrameCodec.Encode(envelope);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer[offset..],
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Named-pipe connection closed mid-frame.");
            }
            offset += read;
        }
    }
}
