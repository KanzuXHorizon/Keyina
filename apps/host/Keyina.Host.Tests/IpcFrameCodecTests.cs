using Keyina.Host.Core.Ipc;

namespace Keyina.Host.Tests;

internal static class IpcFrameCodecTests
{
    private static string GoldenFrameHex => File.ReadAllText(
        Path.Combine(RepositoryPaths.Root, "tests", "data", "ipc_frame_v1.hex"))
        .Trim();

    [KeyinaTest("IPC frame round trips Vietnamese UTF-8 text")]
    private static void FrameRoundTrips()
    {
        var envelope = CreateGoldenEnvelope();
        var encoded = IpcFrameCodec.Encode(envelope);
        AssertEx.Equal(GoldenFrameHex, Convert.ToHexString(encoded));

        var result = IpcFrameCodec.TryDecode(encoded, out var decoded, out var consumed, out var error);
        AssertEx.Equal(IpcDecodeStatus.Success, result);
        AssertEx.Equal(IpcDecodeError.None, error);
        AssertEx.Equal(encoded.Length, consumed);
        AssertEx.Equal(envelope, decoded);
    }

    [KeyinaTest("IPC decoder requests more data for partial header and payload")]
    private static void PartialFramesNeedMoreData()
    {
        var encoded = IpcFrameCodec.Encode(CreateGoldenEnvelope());
        foreach (var length in new[] { 0, 1, IpcFrameCodec.HeaderSize - 1, encoded.Length - 1 })
        {
            var result = IpcFrameCodec.TryDecode(
                encoded.AsSpan(0, length),
                out var envelope,
                out var consumed,
                out var error);
            AssertEx.Equal(IpcDecodeStatus.NeedMoreData, result, $"Unexpected status at {length} bytes.");
            AssertEx.Equal<IpcEnvelope?>(null, envelope);
            AssertEx.Equal(0, consumed);
            AssertEx.Equal(IpcDecodeError.None, error);
        }
    }

    [KeyinaTest("IPC decoder rejects invalid magic version type and UTF-8")]
    private static void InvalidFramesAreRejected()
    {
        AssertInvalid(Mutate(0, 0x00), IpcDecodeError.InvalidMagic);
        AssertInvalid(Mutate(4, 0x02), IpcDecodeError.UnsupportedVersion);
        AssertInvalid(Mutate(6, 0xFF), IpcDecodeError.UnknownMessageType);

        var invalidUtf8 = IpcFrameCodec.Encode(CreateGoldenEnvelope());
        invalidUtf8[^2] = 0xC0;
        invalidUtf8[^1] = 0xAF;
        AssertInvalid(invalidUtf8, IpcDecodeError.InvalidUtf8);
    }

    [KeyinaTest("IPC encoder and decoder reject frames over 64 KiB")]
    private static void OversizedFramesAreRejected()
    {
        var payload = new string('x', IpcFrameCodec.MaximumPayloadBytes + 1);
        AssertThrows<ArgumentException>(() => IpcFrameCodec.Encode(
            CreateGoldenEnvelope() with { Payload = payload }));

        var encoded = IpcFrameCodec.Encode(CreateGoldenEnvelope());
        encoded[10] = 0xFF;
        encoded[11] = 0xFF;
        encoded[12] = 0x00;
        encoded[13] = 0x00;
        AssertInvalid(encoded, IpcDecodeError.FrameTooLarge);
    }

    [KeyinaTest("IPC session validator rejects stale session and focus generation")]
    private static void SessionValidatorRejectsStaleMessages()
    {
        var expected = IpcSessionId.FromBytes(
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]);
        var validator = new IpcSessionValidator(expected, minimumFocusGeneration: 8);

        AssertEx.Equal(
            IpcSessionValidation.Valid,
            validator.Validate(CreateGoldenEnvelope() with { FocusGeneration = 8 }));
        AssertEx.Equal(
            IpcSessionValidation.StaleFocus,
            validator.Validate(CreateGoldenEnvelope() with { FocusGeneration = 7 }));
        AssertEx.Equal(
            IpcSessionValidation.WrongSession,
            validator.Validate(CreateGoldenEnvelope() with { SessionId = IpcSessionId.New() }));
    }

    private static IpcEnvelope CreateGoldenEnvelope() => new(
        IpcMessageType.FinalTranscript,
        Flags: 0x1234,
        IpcSessionId.FromBytes(
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]),
        FocusGeneration: 0x0102030405060708,
        Payload: "xin chào");

    private static byte[] Mutate(int index, byte value)
    {
        var frame = IpcFrameCodec.Encode(CreateGoldenEnvelope());
        frame[index] = value;
        return frame;
    }

    private static void AssertInvalid(byte[] frame, IpcDecodeError expected)
    {
        var status = IpcFrameCodec.TryDecode(frame, out var envelope, out var consumed, out var error);
        AssertEx.Equal(IpcDecodeStatus.Invalid, status);
        AssertEx.Equal(expected, error);
        AssertEx.Equal<IpcEnvelope?>(null, envelope);
        AssertEx.Equal(0, consumed);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
