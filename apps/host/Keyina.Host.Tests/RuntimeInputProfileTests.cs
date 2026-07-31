using System.Buffers.Binary;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Tests;

internal static class RuntimeInputProfileTests
{
    private static readonly byte[] DefaultVector = Convert.FromHexString(
        "4B4952500224110602030001052000055600055400055A00001B000001000000B6CD5DCA");

    [KeyinaTest("runtime input profile encodes the exact default cross-language vector")]
    private static void DefaultProfileMatchesExactVector()
    {
        var encoded = RuntimeInputProfileCodec.Encode(KeyinaConfiguration.Default);

        AssertEx.Equal(RuntimeInputProfileCodec.EncodedLength, encoded.Length);
        AssertEx.True(
            encoded.AsSpan().SequenceEqual(DefaultVector),
            $"Unexpected runtime profile bytes: {Convert.ToHexString(encoded)}");
        AssertEx.True(
            RuntimeInputProfileCodec.Decode(encoded).RestoreInvalidWord,
            "The default native typing profile must restore invalid Latin tokens.");
    }

    [KeyinaTest("runtime input profile round trips configured state and hotkeys")]
    private static void ProfileRoundTripsConfiguredState()
    {
        var configuration = KeyinaConfiguration.Default with
        {
            VietnameseEnabled = false,
            SpeechEnabled = true,
            TranslationEnabled = true,
            ClipboardCompatibilityEnabled = true,
            Hotkeys = HotkeyPreferences.Default
                .WithChord(
                    HotkeyCommand.ToggleVietnamese,
                    new HotkeyChord(
                        HotkeyModifiers.Control | HotkeyModifiers.Alt,
                        VirtualKey.None))
                .WithChord(
                    HotkeyCommand.ToggleDictation,
                    new HotkeyChord(
                        HotkeyModifiers.Control | HotkeyModifiers.Shift,
                        VirtualKey.F9))
                .WithChord(
                    HotkeyCommand.UndoTranslation,
                    new HotkeyChord(
                        HotkeyModifiers.Control | HotkeyModifiers.Shift,
                        VirtualKey.F10)),
        };

        var decoded = RuntimeInputProfileCodec.Decode(
            RuntimeInputProfileCodec.Encode(configuration));

        AssertEx.False(decoded.VietnameseEnabled, "Vietnamese state changed during profile round trip.");
        AssertEx.True(decoded.SpeechEnabled, "Speech state changed during profile round trip.");
        AssertEx.True(decoded.TranslationEnabled, "Translation state changed during profile round trip.");
        AssertEx.True(decoded.RestoreInvalidWord, "Invalid-Latin restoration changed during profile round trip.");
        AssertEx.True(decoded.ClipboardCompatibilityEnabled, "Clipboard compatibility mode changed during profile round trip.");
        AssertEx.Equal(configuration.SchemaVersion, decoded.SourceSchemaVersion);
        AssertEx.Equal(configuration.Hotkeys, decoded.Hotkeys);
    }

    [KeyinaTest("runtime input profile rejects checksum version and gesture corruption")]
    private static void ProfileRejectsCorruption()
    {
        var checksumCorrupt = DefaultVector.ToArray();
        checksumCorrupt[8] ^= 0x01;
        AssertInvalid(checksumCorrupt, "checksum corruption");

        var unknownVersion = DefaultVector.ToArray();
        unknownVersion[4] = 3;
        RewriteChecksum(unknownVersion);
        AssertInvalid(unknownVersion, "unknown version");

        var unsupportedGesture = DefaultVector.ToArray();
        unsupportedGesture[8] = 0x7F;
        RewriteChecksum(unsupportedGesture);
        AssertInvalid(unsupportedGesture, "unsupported gesture");
    }

    [KeyinaTest("runtime input profile rejects invalid length magic flags and reserved bytes")]
    private static void ProfileRejectsInvalidEnvelope()
    {
        AssertInvalid(DefaultVector.AsSpan(0, DefaultVector.Length - 1).ToArray(), "short profile");

        var invalidMagic = DefaultVector.ToArray();
        invalidMagic[0] = (byte)'X';
        RewriteChecksum(invalidMagic);
        AssertInvalid(invalidMagic, "invalid magic");

        var unknownFlag = DefaultVector.ToArray();
        unknownFlag[6] |= 0x80;
        RewriteChecksum(unknownFlag);
        AssertInvalid(unknownFlag, "unknown flag");

        var reservedByte = DefaultVector.ToArray();
        reservedByte[26] = 1;
        RewriteChecksum(reservedByte);
        AssertInvalid(reservedByte, "reserved byte");
    }

    private static void AssertInvalid(byte[] bytes, string scenario)
    {
        try
        {
            _ = RuntimeInputProfileCodec.Decode(bytes);
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException($"Runtime profile accepted {scenario}.");
    }

    private static void RewriteChecksum(byte[] bytes)
    {
        var checksum = ComputeFnv1a(bytes.AsSpan(0, RuntimeInputProfileCodec.ChecksumOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(RuntimeInputProfileCodec.ChecksumOffset, sizeof(uint)),
            checksum);
    }

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
