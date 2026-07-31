using System.Buffers.Binary;
using System.Text;
using Keyina.Host.Core.Snippets;

namespace Keyina.Host.Core.Configuration;

public sealed record RuntimeSnippetProfileEntrySnapshot(
    string Trigger,
    string Expansion,
    bool CaseSensitive,
    bool PreserveDelimiter,
    string Delimiters,
    IReadOnlyList<ulong> AllowedApplicationHashes,
    IReadOnlyList<ulong> ExcludedApplicationHashes,
    SnippetCommand Command);

public sealed record RuntimeSnippetProfileSnapshot(
    IReadOnlyList<RuntimeSnippetProfileEntrySnapshot> Entries);

public static class RuntimeSnippetProfileCodec
{
    public const int MaximumProfileBytes = 1024 * 1024;
    public const int HeaderLength = 20;
    public const int EntryHeaderLength = 16;

    private const byte FormatVersion = 1;
    private const int BuiltInSnippetCount = 5;
    private const byte CaseSensitiveFlag = 1 << 0;
    private const byte PreserveDelimiterFlag = 1 << 1;
    private const byte KnownFlags = CaseSensitiveFlag | PreserveDelimiterFlag;
    private const int ChecksumOffset = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static ReadOnlySpan<byte> Magic => "KYSN"u8;

    public static byte[] Encode(KeyinaConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var definitions = BuiltInSnippets.Create()
            .Concat(configuration.ValidateAndCreateSnippets())
            .ToArray();
        try
        {
            _ = new SnippetMatcher(definitions);
        }
        catch (ArgumentException exception)
        {
            throw new ConfigurationValidationException(
                "Custom snippet triggers must not conflict with built-in triggers.",
                exception);
        }

        using var stream = new MemoryStream(capacity: 4096);
        stream.SetLength(HeaderLength);
        stream.Position = HeaderLength;
        foreach (var definition in definitions)
        {
            WriteDefinition(stream, definition);
            if (stream.Length > MaximumProfileBytes)
            {
                throw new ConfigurationValidationException(
                    $"Runtime snippet profile exceeds {MaximumProfileBytes} bytes.");
            }
        }

        var bytes = stream.ToArray();
        Magic.CopyTo(bytes);
        bytes[4] = FormatVersion;
        bytes[5] = HeaderLength;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(8, 4),
            checked((uint)definitions.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(12, 4),
            checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(ChecksumOffset, 4),
            ComputeChecksum(bytes));
        return bytes;
    }

    public static RuntimeSnippetProfileSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderLength || bytes.Length > MaximumProfileBytes)
        {
            throw new InvalidDataException("Runtime snippet profile length is invalid.");
        }
        if (!bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Runtime snippet profile magic is invalid.");
        }
        if (bytes[4] != FormatVersion || bytes[5] != HeaderLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2)) != 0)
        {
            throw new InvalidDataException("Runtime snippet profile header is unsupported.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4)) != bytes.Length)
        {
            throw new InvalidDataException("Runtime snippet profile size is inconsistent.");
        }
        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(ChecksumOffset, 4));
        if (expectedChecksum != ComputeChecksum(bytes))
        {
            throw new InvalidDataException("Runtime snippet profile checksum is invalid.");
        }

        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4));
        if (entryCount > KeyinaConfiguration.MaximumCustomSnippets + BuiltInSnippetCount)
        {
            throw new InvalidDataException("Runtime snippet profile contains too many entries.");
        }

        var entries = new List<RuntimeSnippetProfileEntrySnapshot>(checked((int)entryCount));
        var definitions = new List<SnippetDefinition>(checked((int)entryCount));
        var offset = HeaderLength;
        for (var index = 0u; index < entryCount; index++)
        {
            if (bytes.Length - offset < EntryHeaderLength)
            {
                throw new InvalidDataException("Runtime snippet entry header is truncated.");
            }

            var header = bytes.Slice(offset, EntryHeaderLength);
            offset += EntryHeaderLength;
            var flags = header[0];
            if ((flags & ~KnownFlags) != 0 || header[2] != 0 || header[3] != 0)
            {
                throw new InvalidDataException("Runtime snippet entry flags are invalid.");
            }
            var command = (SnippetCommand)header[1];
            if (!Enum.IsDefined(command))
            {
                throw new InvalidDataException("Runtime snippet command is invalid.");
            }

            var delimiterBytes = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
            var triggerBytes = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
            var allowedCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2));
            var excludedCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(10, 2));
            var expansionBytes = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));
            int bodyLength;
            try
            {
                bodyLength = checked(
                    (int)delimiterBytes + triggerBytes + (int)expansionBytes +
                    ((int)allowedCount + excludedCount) * sizeof(ulong));
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    "Runtime snippet entry length is invalid.",
                    exception);
            }
            if (bytes.Length - offset < bodyLength)
            {
                throw new InvalidDataException("Runtime snippet entry body is truncated.");
            }

            string delimiters;
            string trigger;
            string expansion;
            try
            {
                delimiters = StrictUtf8.GetString(bytes.Slice(offset, delimiterBytes));
                offset += delimiterBytes;
                trigger = StrictUtf8.GetString(bytes.Slice(offset, triggerBytes));
                offset += triggerBytes;
                expansion = StrictUtf8.GetString(bytes.Slice(offset, checked((int)expansionBytes)));
                offset += checked((int)expansionBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "Runtime snippet profile contains malformed UTF-8.",
                    exception);
            }

            var allowed = ReadHashes(bytes, ref offset, allowedCount);
            var excluded = ReadHashes(bytes, ref offset, excludedCount);
            try
            {
                var definition = new SnippetDefinition(
                    trigger,
                    expansion,
                    (flags & CaseSensitiveFlag) != 0,
                    (flags & PreserveDelimiterFlag) != 0,
                    delimiters.ToHashSet(),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    command);
                _ = definition.Validate();
                definitions.Add(definition);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "Runtime snippet entry is invalid.",
                    exception);
            }

            entries.Add(new RuntimeSnippetProfileEntrySnapshot(
                trigger,
                expansion,
                (flags & CaseSensitiveFlag) != 0,
                (flags & PreserveDelimiterFlag) != 0,
                delimiters,
                allowed,
                excluded,
                command));
        }

        if (offset != bytes.Length)
        {
            throw new InvalidDataException("Runtime snippet profile contains trailing bytes.");
        }
        try
        {
            _ = new SnippetMatcher(definitions);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Runtime snippet profile contains duplicate triggers.",
                exception);
        }

        return new RuntimeSnippetProfileSnapshot(entries);
    }

    public static ulong HashApplicationId(string applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var normalized = applicationId.Trim().ToUpperInvariant();
        var bytes = StrictUtf8.GetBytes(normalized);
        var hash = 14695981039346656037UL;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static void WriteDefinition(Stream stream, SnippetDefinition definition)
    {
        var delimiters = string.Concat(definition.Delimiters.Order());
        var delimiterBytes = StrictUtf8.GetBytes(delimiters);
        var triggerBytes = StrictUtf8.GetBytes(definition.Trigger);
        var expansionBytes = StrictUtf8.GetBytes(definition.Expansion);
        var allowed = definition.AllowedApplications
            .Select(HashApplicationId)
            .Order()
            .ToArray();
        var excluded = definition.ExcludedApplications
            .Select(HashApplicationId)
            .Order()
            .ToArray();
        if (delimiterBytes.Length > ushort.MaxValue ||
            triggerBytes.Length > ushort.MaxValue ||
            allowed.Length > ushort.MaxValue ||
            excluded.Length > ushort.MaxValue)
        {
            throw new ConfigurationValidationException(
                "Runtime snippet entry exceeds its encoded field limits.");
        }

        Span<byte> header = stackalloc byte[EntryHeaderLength];
        header.Clear();
        header[0] = (byte)(
            (definition.CaseSensitive ? CaseSensitiveFlag : 0) |
            (definition.PreserveDelimiter ? PreserveDelimiterFlag : 0));
        header[1] = checked((byte)definition.Command);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), checked((ushort)delimiterBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), checked((ushort)triggerBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(8, 2), checked((ushort)allowed.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(10, 2), checked((ushort)excluded.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), checked((uint)expansionBytes.Length));
        stream.Write(header);
        stream.Write(delimiterBytes);
        stream.Write(triggerBytes);
        stream.Write(expansionBytes);
        WriteHashes(stream, allowed);
        WriteHashes(stream, excluded);
    }

    private static ulong[] ReadHashes(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        ushort count)
    {
        var values = new ulong[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, sizeof(ulong)));
            offset += sizeof(ulong);
        }
        return values;
    }

    private static void WriteHashes(Stream stream, IReadOnlyList<ulong> hashes)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        foreach (var hash in hashes)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, hash);
            stream.Write(bytes);
        }
    }

    private static uint ComputeChecksum(ReadOnlySpan<byte> bytes)
    {
        var hash = 2166136261u;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index >= ChecksumOffset && index < ChecksumOffset + sizeof(uint))
            {
                continue;
            }
            hash ^= bytes[index];
            hash *= 16777619u;
        }
        return hash;
    }
}
