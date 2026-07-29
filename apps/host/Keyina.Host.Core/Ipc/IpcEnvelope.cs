using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Keyina.Host.Core.Ipc;

public readonly record struct IpcSessionId(ulong Low, ulong High)
{
    public static IpcSessionId New()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return FromBytes(bytes);
    }

    public static IpcSessionId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
        {
            throw new ArgumentException("Session ID must contain exactly 16 bytes.", nameof(bytes));
        }

        return new IpcSessionId(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
    }

    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < 16)
        {
            throw new ArgumentException("Destination must contain at least 16 bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination[..8], Low);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), High);
    }
}

public sealed record IpcEnvelope(
    IpcMessageType MessageType,
    ushort Flags,
    IpcSessionId SessionId,
    ulong FocusGeneration,
    string Payload);
