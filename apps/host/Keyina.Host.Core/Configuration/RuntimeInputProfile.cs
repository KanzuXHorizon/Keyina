using System.Buffers.Binary;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Overlay;

namespace Keyina.Host.Core.Configuration;

public sealed record RuntimeInputProfileSnapshot(
    bool VietnameseEnabled,
    bool SpeechEnabled,
    bool TranslationEnabled,
    bool TraditionalTonePlacement,
    bool QuickTelexLetters,
    bool StandaloneWToUHorn,
    bool RestoreInvalidWord,
    bool ClipboardCompatibilityEnabled,
    int SourceSchemaVersion,
    HotkeyPreferences Hotkeys,
    KeystrokeOverlayPreferences KeystrokeOverlay);

public static class RuntimeInputProfileCodec
{
    public const int LegacyEncodedLength = 36;
    public const int EncodedLength = 40;
    public const int ChecksumOffset = 36;

    private const int LegacyChecksumOffset = 32;
    private const byte LegacyFormatVersion = 2;
    private const byte PreviousFormatVersion = 3;
    private const byte FormatVersion = 4;
    private const byte BindingCount = 6;
    private const byte VietnameseEnabledFlag = 1 << 0;
    private const byte SpeechEnabledFlag = 1 << 1;
    private const byte TranslationEnabledFlag = 1 << 2;
    private const byte TraditionalTonePlacementFlag = 1 << 3;
    private const byte RestoreInvalidWordFlag = 1 << 4;
    private const byte ClipboardCompatibilityFlag = 1 << 5;
    private const byte QuickTelexLettersFlag = 1 << 6;
    private const byte DisableStandaloneWFlag = 1 << 7;
    private const byte KnownFlags = 0xFF;
    private const byte OverlayEnabledFlag = 1 << 0;
    private const int OverlayMotionShift = 1;
    private const byte OverlayMotionMask = 0b0000_0110;
    private const int OverlayCornerShift = 3;
    private const byte PreviousOverlayCornerMask = 0b0001_1000;
    private const byte PreviousOverlayPresentationFlag = 1 << 5;
    private const byte PreviousKnownOverlayFlags = OverlayEnabledFlag | OverlayMotionMask |
        PreviousOverlayCornerMask | PreviousOverlayPresentationFlag;
    private const byte OverlayCornerMask = 0b0011_1000;
    private const byte OverlayPresentationFlag = 1 << 6;
    private const byte KnownOverlayFlags = OverlayEnabledFlag | OverlayMotionMask |
        OverlayCornerMask | OverlayPresentationFlag;

    private static ReadOnlySpan<byte> Magic => "KIRP"u8;

    public static byte[] Encode(KeyinaConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _ = configuration.ValidateAndCreateSnippets();
        var overlay = configuration.KeystrokeOverlay ?? KeystrokeOverlayPreferences.Default;

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
        WriteBinding(bytes, 4, configuration.Hotkeys.UndoTranslation);
        WriteBinding(bytes, 5, configuration.Hotkeys.CancelActiveCommand);
        bytes[26] = ComposeOverlayFlags(overlay);
        bytes[27] = checked((byte)overlay.SizePercent);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, sizeof(int)), configuration.SchemaVersion);
        bytes[32] = checked((byte)overlay.OpacityPercent);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(33, sizeof(ushort)), checked((ushort)overlay.HideDelayMilliseconds));
        bytes[35] = ComposeOverlaySound(overlay);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(ChecksumOffset, sizeof(uint)),
            ComputeFnv1a(bytes.AsSpan(0, ChecksumOffset)));
        return bytes;
    }

    public static RuntimeInputProfileSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is not LegacyEncodedLength and not EncodedLength)
        {
            throw new InvalidDataException("Runtime input profile length is invalid.");
        }
        if (!bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Runtime input profile magic is invalid.");
        }
        var version = bytes[4];
        var expectedLength = version == LegacyFormatVersion ? LegacyEncodedLength :
            version is PreviousFormatVersion or FormatVersion ? EncodedLength : 0;
        if (expectedLength == 0)
        {
            throw new InvalidDataException($"Unsupported runtime input profile version: {version}.");
        }
        if (bytes.Length != expectedLength || bytes[5] != expectedLength || bytes[7] != BindingCount)
        {
            throw new InvalidDataException("Runtime input profile header is inconsistent.");
        }
        var checksumOffset = version == LegacyFormatVersion ? LegacyChecksumOffset : ChecksumOffset;
        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(checksumOffset, sizeof(uint)));
        if (expectedChecksum != ComputeFnv1a(bytes[..checksumOffset]))
        {
            throw new InvalidDataException("Runtime input profile checksum is invalid.");
        }
        if ((bytes[6] & ~KnownFlags) != 0)
        {
            throw new InvalidDataException("Runtime input profile contains unsupported flags.");
        }

        var sourceSchemaVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(28, sizeof(int)));
        if (sourceSchemaVersion <= 0)
        {
            throw new InvalidDataException("Runtime input profile source schema is invalid.");
        }

        try
        {
            var hotkeys = new HotkeyPreferences(
                ReadBinding(bytes, 0), ReadBinding(bytes, 1), ReadBinding(bytes, 2),
                ReadBinding(bytes, 3), ReadBinding(bytes, 5))
            {
                UndoTranslation = ReadBinding(bytes, 4),
            };
            hotkeys.Validate();
            var flags = bytes[6];
            var overlay = version == LegacyFormatVersion
                ? KeystrokeOverlayPreferences.Default
                : DecodeOverlay(bytes, version);
            return new RuntimeInputProfileSnapshot(
                (flags & VietnameseEnabledFlag) != 0,
                (flags & SpeechEnabledFlag) != 0,
                (flags & TranslationEnabledFlag) != 0,
                (flags & TraditionalTonePlacementFlag) != 0,
                (flags & QuickTelexLettersFlag) != 0,
                (flags & DisableStandaloneWFlag) == 0,
                (flags & RestoreInvalidWordFlag) != 0,
                (flags & ClipboardCompatibilityFlag) != 0,
                sourceSchemaVersion,
                hotkeys,
                overlay);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Runtime input profile contains invalid values.", exception);
        }
    }

    private static KeystrokeOverlayPreferences DecodeOverlay(
        ReadOnlySpan<byte> bytes,
        byte version)
    {
        var flags = bytes[26];
        var previousFormat = version == PreviousFormatVersion;
        var knownFlags = previousFormat ? PreviousKnownOverlayFlags : KnownOverlayFlags;
        if ((flags & ~knownFlags) != 0)
        {
            throw new InvalidDataException("Runtime overlay profile contains unsupported flags.");
        }
        var cornerMask = previousFormat ? PreviousOverlayCornerMask : OverlayCornerMask;
        var presentationFlag = previousFormat
            ? PreviousOverlayPresentationFlag
            : OverlayPresentationFlag;
        var preferences = new KeystrokeOverlayPreferences(
            (flags & OverlayEnabledFlag) != 0,
            (KeystrokeOverlayMotionLevel)((flags & OverlayMotionMask) >> OverlayMotionShift),
            bytes[27],
            bytes[32],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(33, sizeof(ushort))),
            (KeystrokeOverlayFallbackCorner)((flags & cornerMask) >> OverlayCornerShift),
            (flags & presentationFlag) != 0,
            (bytes[35] & 0x80) != 0,
            bytes[35] & 0x7F);
        preferences.Validate();
        return preferences;
    }

    private static byte ComposeOverlaySound(KeystrokeOverlayPreferences preferences) =>
        checked((byte)((preferences.PerKeySoundEnabled ? 0x80 : 0) |
            preferences.SoundVolumePercent));

    private static byte ComposeOverlayFlags(KeystrokeOverlayPreferences preferences)
    {
        byte flags = 0;
        if (preferences.Enabled) flags |= OverlayEnabledFlag;
        flags |= (byte)(((int)preferences.Motion << OverlayMotionShift) & OverlayMotionMask);
        flags |= (byte)(((int)preferences.FallbackCorner << OverlayCornerShift) & OverlayCornerMask);
        if (preferences.PresentationMode) flags |= OverlayPresentationFlag;
        return flags;
    }

    private static byte ComposeFlags(KeyinaConfiguration configuration)
    {
        byte flags = 0;
        if (configuration.VietnameseEnabled) flags |= VietnameseEnabledFlag;
        if (configuration.SpeechEnabled) flags |= SpeechEnabledFlag;
        if (configuration.TranslationEnabled) flags |= TranslationEnabledFlag;
        if (configuration.TraditionalTonePlacement) flags |= TraditionalTonePlacementFlag;
        if (configuration.QuickTelexLetters) flags |= QuickTelexLettersFlag;
        if (!configuration.StandaloneWToUHorn) flags |= DisableStandaloneWFlag;
        flags |= RestoreInvalidWordFlag;
        if (configuration.ClipboardCompatibilityEnabled) flags |= ClipboardCompatibilityFlag;
        return flags;
    }

    private static void WriteBinding(Span<byte> destination, int index, HotkeyPreference preference)
    {
        var offset = 8 + (index * 3);
        destination[offset] = EncodeGesture(preference.GestureKind);
        destination[offset + 1] = checked((byte)preference.Chord.Modifiers);
        destination[offset + 2] = checked((byte)preference.Chord.Key);
    }

    private static HotkeyPreference ReadBinding(ReadOnlySpan<byte> source, int index)
    {
        var offset = 8 + (index * 3);
        return new HotkeyPreference(DecodeGesture(source[offset]),
            new HotkeyChord((HotkeyModifiers)source[offset + 1], (VirtualKey)source[offset + 2]));
    }

    private static byte EncodeGesture(HotkeyGestureKind gesture) => gesture switch
    {
        HotkeyGestureKind.Press => 0,
        HotkeyGestureKind.Hold => 1,
        HotkeyGestureKind.ModifierGesture => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(gesture)),
    };

    private static HotkeyGestureKind DecodeGesture(byte gesture) => gesture switch
    {
        0 => HotkeyGestureKind.Press,
        1 => HotkeyGestureKind.Hold,
        2 => HotkeyGestureKind.ModifierGesture,
        _ => throw new ArgumentException($"Unsupported runtime hotkey gesture value: {gesture}.", nameof(gesture)),
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
