using System.Buffers.Binary;

namespace Keyina.BrandAssets;

internal static class IcoWriter
{
    public static byte[] Create(IReadOnlyList<IconFrame> frames)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one ICO frame is required.", nameof(frames));
        }
        if (frames.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(frames));
        }

        var ordered = frames.OrderBy(frame => frame.Size).ToArray();
        if (ordered.Select(frame => frame.Size).Distinct().Count() != ordered.Length)
        {
            throw new InvalidOperationException("ICO frame sizes must be unique.");
        }

        const int directoryHeaderSize = 6;
        const int directoryEntrySize = 16;
        var dataOffset = directoryHeaderSize + (ordered.Length * directoryEntrySize);
        var totalLength = dataOffset + ordered.Sum(frame => frame.PngBytes.Length);
        var output = new byte[totalLength];
        var span = output.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span[..2], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), checked((ushort)ordered.Length));

        var currentOffset = dataOffset;
        for (var index = 0; index < ordered.Length; index++)
        {
            var frame = ordered[index];
            if (frame.Size is < 1 or > 256)
            {
                throw new InvalidOperationException($"ICO frame size is unsupported: {frame.Size}");
            }

            var entry = span.Slice(directoryHeaderSize + (index * directoryEntrySize), directoryEntrySize);
            entry[0] = frame.Size == 256 ? (byte)0 : checked((byte)frame.Size);
            entry[1] = frame.Size == 256 ? (byte)0 : checked((byte)frame.Size);
            entry[2] = 0;
            entry[3] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(4, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(6, 2), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), checked((uint)frame.PngBytes.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12, 4), checked((uint)currentOffset));

            frame.PngBytes.CopyTo(span.Slice(currentOffset, frame.PngBytes.Length));
            currentOffset += frame.PngBytes.Length;
        }

        return output;
    }
}

internal sealed record IconFrame(int Size, byte[] PngBytes);
