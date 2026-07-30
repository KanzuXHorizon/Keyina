using System.Buffers.Binary;
using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Core.Configuration;

public sealed record RuntimeInputProfileSnapshot(
    bool VietnameseEnabled,
    bool SpeechEnabled,
    bool TranslationEnabled,
    bool TraditionalTonePlacement,
    bool RestoreInvalidWord,
    int SourceSchemaVersion,
    HotkeyPreferences Hotkeys);

public static class RuntimeInputProfileCodec
{
    public const int EncodedLength = 32;
    public const int ChecksumOffset = 28;

    private const byte FormatVersion = 1;
    private const byte BindingCount = 5;
    private const byte VietnameseEnabledFlag = 1 << 0;
    private const byte SpeechEnabledFlag = 1 << 1;
    private const byte TranslationEnabledFlag = 1 << 2;
    private const byte TraditionalTonePlacementFlag = 1 << 3;
    private const byte RestoreInvalidWordFlag = 1 << 4;
    private const byte KnownFlags =
        VietnameseEnabledFlag |
        SpeechEnabledFlag |
        TranslationEnabledFlag |
        TraditionalTonePlacementFlag |
        RestoreInvalidWordFlag;

    private static ReadOnlySpan<byte> Magic => "KIRP"u8;

    public static byte[] Encode(KeyinaConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _ = configuration.ValidateAndCreateSnippets();

        var bytes = new byte[EncodedLength];
        Magic.CopyTo(bytes);
        bytes[4] = FormatVersion;
        bytes[5] = EncodedLength;
        bytes[6] = ComposeFlags(configuration);
        bytes[7] = BindingCount;

        WriteBinding(bytes, 0, configuration.Hotkeys.ToggleVietnamese);
        WriteBinding(bytes, 1, configuration.Hotkeys.PushToTalk);
        WriteBinding(bytes, 2, configuration.Hotkeys.ToggleDictation);
        WriteBinding(bytes, 3, configuration.Hotkeys.TranslateSelection);
        WriteBinding(bytes, 4, configuration.Hotkeys.CancelActiveCommand);
        bytes[23] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(24, sizeof(int)),
            configuration.SchemaVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(ChecksumOffset, sizeof(uint)),
            ComputeFnv1a(bytes.AsSpan(0, ChecksumOffset)));
        return bytes;
    }

    public static RuntimeInputProfileSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != EncodedLength)
        {
            throw new InvalidDataException(
                $"Runtime input profile must be exactly {EncodedLength} bytes.");
        }
        if (!bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Runtime input profile magic is invalid.");
        }
        if (bytes[4] != FormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported runtime input profile version: {bytes[4]}.");
        }
        if (bytes[5] != EncodedLength || bytes[7] != BindingCount)
        {
            throw new InvalidDataException("Runtime input profile header is inconsistent.");
        }
        if ((bytes[6] & ~KnownFlags) != 0 || bytes[23] != 0)
        {
            throw new InvalidDataException("Runtime input profile contains unsupported flags.");
        }

        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(ChecksumOffset, sizeof(uint)));
        var actualChecksum = ComputeFnv1a(bytes[..ChecksumOffset]);
        if (expectedChecksum != actualChecksum)
        {
            throw new InvalidDataException("Runtime input profile checksum is invalid.");
        }

        var sourceSchemaVersion = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.Slice(24, sizeof(int)));
        if (sourceSchemaVersion <= 0)
        {
            throw new InvalidDataException("Runtime input profile source schema is invalid.");
        }

        try
        {
            var hotkeys = new HotkeyPreferences(
                ReadBinding(bytes, 0),
                ReadBinding(bytes, 1),
                ReadBinding(bytes, 2),
                ReadBinding(bytes, 3),
                ReadBinding(bytes, 4));
            hotkeys.Validate();

            var flags = bytes[6];
            return new RuntimeInputProfileSnapshot(
                (flags & VietnameseEnabledFlag) != 0,
                (flags & SpeechEnabledFlag) != 0,
                (flags & TranslationEnabledFlag) != 0,
                (flags & TraditionalTonePlacementFlag) != 0,
                (flags & RestoreInvalidWordFlag) != 0,
                sourceSchemaVersion,
                hotkeys);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Runtime input profile contains an invalid hotkey binding.",
                exception);
        }
    }

    private static byte ComposeFlags(KeyinaConfiguration configuration)
    {
        byte flags = 0;
        if (configuration.VietnameseEnabled)
        {
            flags |= VietnameseEnabledFlag;
        }
        if (configuration.SpeechEnabled)
        {
            flags |= SpeechEnabledFlag;
        }
        if (configuration.TranslationEnabled)
        {
            flags |= TranslationEnabledFlag;
        }
        return flags;
    }

    private static void WriteBinding(
        Span<byte> destination,
        int index,
        HotkeyPreference preference)
    {
        var offset = 8 + (index * 3);
        destination[offset] = EncodeGesture(preference.GestureKind);
        destination[offset + 1] = checked((byte)preference.Chord.Modifiers);
        destination[offset + 2] = checked((byte)preference.Chord.Key);
    }

    private static HotkeyPreference ReadBinding(ReadOnlySpan<byte> source, int index)
    {
        var offset = 8 + (index * 3);
        return new HotkeyPreference(
            DecodeGesture(source[offset]),
            new HotkeyChord(
                (HotkeyModifiers)source[offset + 1],
                (VirtualKey)source[offset + 2]));
    }

    private static byte EncodeGesture(HotkeyGestureKind gesture) => gesture switch
    {
        HotkeyGestureKind.Press => 0,
        HotkeyGestureKind.Hold => 1,
        HotkeyGestureKind.ModifierGesture => 2,
        _ => throw new ArgumentOutOfRangeException(
            nameof(gesture),
            gesture,
            "Unsupported runtime hotkey gesture."),
    };

    private static HotkeyGestureKind DecodeGesture(byte gesture) => gesture switch
    {
        0 => HotkeyGestureKind.Press,
        1 => HotkeyGestureKind.Hold,
        2 => HotkeyGestureKind.ModifierGesture,
        _ => throw new ArgumentException(
            $"Unsupported runtime hotkey gesture value: {gesture}.",
            nameof(gesture)),
    };

    private static uint ComputeFnv1a(ReadOnlySpan<byte> bytes)
    {
        var hash = 2166136261u;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= 16777619u;
        }
        return hash;
    }
}
