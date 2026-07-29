using System.Buffers.Binary;
using Keyina.Host.Windows.Audio;

namespace Keyina.Host.Tests;

internal static class Pcm16MonoConverterTests
{
    [KeyinaTest("float stereo audio is mixed resampled and clipped to PCM16 mono")]
    private static void FloatStereoConvertsToPcm16Mono()
    {
        var converter = new Pcm16MonoConverter(
            new AudioSourceFormat(48_000, 2, AudioSampleEncoding.IeeeFloat));
        var source = FloatBytes(
            0.5f, 0.5f,
            2.0f, 2.0f,
            -2.0f, -2.0f,
            0.25f, 0.75f,
            0.0f, 0.0f,
            0.0f, 0.0f,
            0.0f, 0.0f);

        var output = converter.Convert(source);
        AssertEx.True((output.Length & 1) == 0, "PCM16 output had an odd byte count.");
        AssertEx.Equal(4, output.Length);
        AssertEx.Equal((short)16_384, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(0, 2)));
        AssertEx.Equal((short)16_384, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(2, 2)));
    }

    [KeyinaTest("PCM16 stereo channels are averaged without overflow")]
    private static void Pcm16StereoMixesSafely()
    {
        var converter = new Pcm16MonoConverter(
            new AudioSourceFormat(16_000, 2, AudioSampleEncoding.Pcm16));
        var source = Pcm16Bytes(
            short.MaxValue, short.MinValue,
            10_000, 30_000,
            -30_000, -10_000);

        var output = converter.Convert(source);
        AssertEx.Equal(4, output.Length);
        AssertEx.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(0, 2)));
        AssertEx.Equal((short)20_000, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(2, 2)));
    }

    [KeyinaTest("streaming resampling is identical across arbitrary callback boundaries")]
    private static void StreamingConversionPreservesContinuity()
    {
        var samples = Enumerable.Range(0, 600)
            .SelectMany(index => new[]
            {
                MathF.Sin(index * 0.03f),
                MathF.Cos(index * 0.02f),
            })
            .ToArray();
        var source = FloatBytes(samples);
        var format = new AudioSourceFormat(48_000, 2, AudioSampleEncoding.IeeeFloat);

        var wholeConverter = new Pcm16MonoConverter(format);
        var whole = wholeConverter.Convert(source);

        var splitConverter = new Pcm16MonoConverter(format);
        var first = splitConverter.Convert(source.AsSpan(0, 480 * sizeof(float)));
        var second = splitConverter.Convert(source.AsSpan(480 * sizeof(float), 360 * sizeof(float)));
        var third = splitConverter.Convert(source.AsSpan(840 * sizeof(float)));
        var split = first.Concat(second).Concat(third).ToArray();

        AssertEx.True(whole.SequenceEqual(split), "Resampling changed at callback boundaries.");
    }

    [KeyinaTest("audio converter rejects incomplete frames and invalid source formats")]
    private static void InvalidAudioIsRejected()
    {
        AssertThrows<ArgumentOutOfRangeException>(() =>
            _ = new Pcm16MonoConverter(new AudioSourceFormat(0, 1, AudioSampleEncoding.Pcm16)));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            _ = new Pcm16MonoConverter(new AudioSourceFormat(16_000, 0, AudioSampleEncoding.Pcm16)));

        var converter = new Pcm16MonoConverter(
            new AudioSourceFormat(16_000, 2, AudioSampleEncoding.Pcm16));
        AssertThrows<ArgumentException>(() => converter.Convert(new byte[3]));
    }

    private static byte[] FloatBytes(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Pcm16Bytes(params short[] values)
    {
        var bytes = new byte[values.Length * sizeof(short)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
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
