using System.Buffers.Binary;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Overlay;

namespace Keyina.Host.Tests;

internal static class RuntimeInputProfileTests
{
    private static readonly byte[] LegacyDefaultVector = Convert.FromHexString(
        "4B4952500224110602030001052000055600055400055A00001B000001000000B6CD5DCA");

    private static readonly byte[] PreviousDefaultVector = Convert.FromHexString(
        "4B4952500328110602030001052000055600055400055A00001B0064010000005C84031EE68FA6BC");

    private static readonly byte[] DefaultVector = Convert.FromHexString(
        "4B4952500428110602030001052000055600055400055A00001B0064010000005C84031EB99701EA");

    [KeyinaTest("runtime input profile encodes the exact default cross-language vector")]
    private static void DefaultProfileMatchesExactVector()
    {
        var encoded = RuntimeInputProfileCodec.Encode(KeyinaConfiguration.Default);

        AssertEx.Equal(RuntimeInputProfileCodec.EncodedLength, encoded.Length);
        AssertEx.True(
            encoded.AsSpan().SequenceEqual(DefaultVector),
            $"Unexpected runtime profile bytes: {Convert.ToHexString(encoded)}");
        var decoded = RuntimeInputProfileCodec.Decode(encoded);
        AssertEx.True(
            decoded.RestoreInvalidWord,
            "The default native typing profile must restore invalid Latin tokens.");
        AssertEx.Equal(KeystrokeOverlayPreferences.Default, decoded.KeystrokeOverlay);
        AssertEx.Equal(
            KeystrokeOverlayPreferences.Default,
            RuntimeInputProfileCodec.Decode(LegacyDefaultVector).KeystrokeOverlay);
        AssertEx.Equal(
            KeystrokeOverlayPreferences.Default,
            RuntimeInputProfileCodec.Decode(PreviousDefaultVector).KeystrokeOverlay);

        var previousConfigured = PreviousDefaultVector.ToArray();
        previousConfigured[26] = 0x39;
        RewriteChecksum(previousConfigured);
        var previousOverlay = RuntimeInputProfileCodec.Decode(
            previousConfigured).KeystrokeOverlay;
        AssertEx.True(previousOverlay.Enabled, "The version-three overlay enabled bit was lost.");
        AssertEx.True(previousOverlay.PresentationMode, "The version-three presentation bit was lost.");
        AssertEx.Equal(
            KeystrokeOverlayFallbackCorner.TopLeft,
            previousOverlay.FallbackCorner);
    }

    [KeyinaTest("runtime input profile round trips configured state and hotkeys")]
    private static void ProfileRoundTripsConfiguredState()
    {
        var configuration = KeyinaConfiguration.Default with
        {
            VietnameseEnabled = false,
            SpeechEnabled = true,
            TranslationEnabled = true,
            TraditionalTonePlacement = true,
            QuickTelexLetters = true,
            StandaloneWToUHorn = false,
            ClipboardCompatibilityEnabled = true,
            KeystrokeOverlay = KeystrokeOverlayPreferences.Default with
            {
                Enabled = true,
                Motion = KeystrokeOverlayMotionLevel.Reduced,
                SizePercent = 125,
                OpacityPercent = 80,
                HideDelayMilliseconds = 1_250,
                FallbackCorner = KeystrokeOverlayFallbackCorner.TopCenter,
                PresentationMode = true,
            },
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
        AssertEx.True(decoded.TraditionalTonePlacement, "Tone placement changed during profile round trip.");
        AssertEx.True(decoded.QuickTelexLetters, "Quick Telex state changed during profile round trip.");
        AssertEx.False(decoded.StandaloneWToUHorn, "Standalone W behavior changed during profile round trip.");
        AssertEx.True(decoded.RestoreInvalidWord, "Invalid-Latin restoration changed during profile round trip.");
        AssertEx.True(decoded.ClipboardCompatibilityEnabled, "Clipboard compatibility mode changed during profile round trip.");
        AssertEx.Equal(configuration.SchemaVersion, decoded.SourceSchemaVersion);
        AssertEx.Equal(configuration.Hotkeys, decoded.Hotkeys);
        AssertEx.Equal(configuration.KeystrokeOverlay, decoded.KeystrokeOverlay);
    }

    [KeyinaTest("runtime input profile rejects checksum version and gesture corruption")]
    private static void ProfileRejectsCorruption()
    {
        var checksumCorrupt = DefaultVector.ToArray();
        checksumCorrupt[8] ^= 0x01;
        AssertInvalid(checksumCorrupt, "checksum corruption");

        var unknownVersion = DefaultVector.ToArray();
        unknownVersion[4] = 99;
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

        var invalidSoundVolume = DefaultVector.ToArray();
        invalidSoundVolume[35] = 127;
        RewriteChecksum(invalidSoundVolume);
        AssertInvalid(invalidSoundVolume, "invalid sound volume");
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
        var checksumOffset = bytes.Length == RuntimeInputProfileCodec.LegacyEncodedLength
            ? 32
            : RuntimeInputProfileCodec.ChecksumOffset;
        var checksum = ComputeFnv1a(bytes.AsSpan(0, checksumOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(checksumOffset, sizeof(uint)),
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
