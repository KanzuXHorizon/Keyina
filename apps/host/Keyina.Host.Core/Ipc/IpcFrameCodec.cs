using System.Buffers.Binary;
using System.Text;

namespace Keyina.Host.Core.Ipc;

public static class IpcFrameCodec
{
    private static readonly byte[] Magic = "KYNA"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public const ushort ProtocolVersion = 1;
    public const int HeaderSize = 38;
    public const int MaximumFrameBytes = 64 * 1024;
    public const int MaximumPayloadBytes = MaximumFrameBytes - HeaderSize;

    public static byte[] Encode(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Payload);
        ValidateMessageType(envelope.MessageType);

        var payloadLength = StrictUtf8.GetByteCount(envelope.Payload);
        if (payloadLength > MaximumPayloadBytes)
        {
            throw new ArgumentException(
                $"IPC payload exceeds {MaximumPayloadBytes} UTF-8 bytes.",
                nameof(envelope));
        }

        var frame = new byte[HeaderSize + payloadLength];
        var span = frame.AsSpan();
        Magic.CopyTo(span[..4]);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(
            span.Slice(6, 2),
            checked((ushort)envelope.MessageType));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8, 2), envelope.Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(10, 4), checked((uint)payloadLength));
        envelope.SessionId.WriteBytes(span.Slice(14, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(30, 8), envelope.FocusGeneration);
        StrictUtf8.GetBytes(envelope.Payload.AsSpan(), span[HeaderSize..]);
        return frame;
    }

    public static IpcDecodeStatus TryDecode(
        ReadOnlySpan<byte> buffer,
        out IpcEnvelope? envelope,
        out int consumed,
        out IpcDecodeError error)
    {
        envelope = null;
        consumed = 0;
        error = IpcDecodeError.None;

        if (buffer.Length < HeaderSize)
        {
            return IpcDecodeStatus.NeedMoreData;
        }
        if (!buffer[..4].SequenceEqual(Magic))
        {
            error = IpcDecodeError.InvalidMagic;
            return IpcDecodeStatus.Invalid;
        }
        if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2)) != ProtocolVersion)
        {
            error = IpcDecodeError.UnsupportedVersion;
            return IpcDecodeStatus.Invalid;
        }

        var rawType = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));
        if (!Enum.IsDefined(typeof(IpcMessageType), rawType))
        {
            error = IpcDecodeError.UnknownMessageType;
            return IpcDecodeStatus.Invalid;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(10, 4));
        if (payloadLength > MaximumPayloadBytes)
        {
            error = IpcDecodeError.FrameTooLarge;
            return IpcDecodeStatus.Invalid;
        }

        var totalLength = HeaderSize + checked((int)payloadLength);
        if (buffer.Length < totalLength)
        {
            return IpcDecodeStatus.NeedMoreData;
        }

        string payload;
        try
        {
            payload = StrictUtf8.GetString(buffer.Slice(HeaderSize, checked((int)payloadLength)));
        }
        catch (DecoderFallbackException)
        {
            error = IpcDecodeError.InvalidUtf8;
            return IpcDecodeStatus.Invalid;
        }

        envelope = new IpcEnvelope(
            (IpcMessageType)rawType,
            BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2)),
            IpcSessionId.FromBytes(buffer.Slice(14, 16)),
            BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(30, 8)),
            payload);
        consumed = totalLength;
        return IpcDecodeStatus.Success;
    }

    private static void ValidateMessageType(IpcMessageType messageType)
    {
        if (!Enum.IsDefined(messageType))
        {
            throw new ArgumentOutOfRangeException(nameof(messageType));
        }
    }
}
