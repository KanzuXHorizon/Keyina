using System.Buffers;
using System.Buffers.Binary;

namespace Keyina.Host.Windows.Audio;

public enum AudioSampleEncoding
{
    Pcm16,
    IeeeFloat,
}

public readonly record struct AudioSourceFormat(
    int SampleRate,
    int Channels,
    AudioSampleEncoding Encoding)
{
    public int BytesPerSample => Encoding switch
    {
        AudioSampleEncoding.Pcm16 => sizeof(short),
        AudioSampleEncoding.IeeeFloat => sizeof(float),
        _ => throw new ArgumentOutOfRangeException(nameof(Encoding)),
    };

    public int BytesPerFrame => checked(BytesPerSample * Channels);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SampleRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Channels, 0);
        if (!Enum.IsDefined(Encoding))
        {
            throw new ArgumentOutOfRangeException(nameof(Encoding));
        }
    }
}

public sealed class Pcm16MonoConverter
{
    public const int OutputSampleRate = 16_000;

    private readonly AudioSourceFormat sourceFormat;
    private readonly double sourceFramesPerOutputSample;
    private long totalSourceFrames;
    private double nextOutputSourcePosition;
    private float previousMonoSample;
    private bool hasPreviousMonoSample;

    public Pcm16MonoConverter(AudioSourceFormat sourceFormat)
    {
        sourceFormat.Validate();
        this.sourceFormat = sourceFormat;
        sourceFramesPerOutputSample = sourceFormat.SampleRate / (double)OutputSampleRate;
    }

    public byte[] Convert(ReadOnlySpan<byte> sourceBytes)
    {
        if (sourceBytes.IsEmpty)
        {
            return [];
        }

        if (sourceBytes.Length % sourceFormat.BytesPerFrame != 0)
        {
            throw new ArgumentException(
                "Audio buffer must contain complete source frames.",
                nameof(sourceBytes));
        }

        var frameCount = sourceBytes.Length / sourceFormat.BytesPerFrame;
        var monoSamples = ArrayPool<float>.Shared.Rent(frameCount);
        try
        {
            DecodeMono(sourceBytes, monoSamples.AsSpan(0, frameCount));
            return Resample(monoSamples.AsSpan(0, frameCount));
        }
        finally
        {
            ArrayPool<float>.Shared.Return(monoSamples, clearArray: true);
        }
    }

    public void Reset()
    {
        totalSourceFrames = 0;
        nextOutputSourcePosition = 0;
        previousMonoSample = 0;
        hasPreviousMonoSample = false;
    }

    private void DecodeMono(ReadOnlySpan<byte> sourceBytes, Span<float> destination)
    {
        var frameOffset = 0;
        for (var frame = 0; frame < destination.Length; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < sourceFormat.Channels; channel++)
            {
                var sampleOffset = frameOffset + (channel * sourceFormat.BytesPerSample);
                sum += sourceFormat.Encoding switch
                {
                    AudioSampleEncoding.Pcm16 =>
                        BinaryPrimitives.ReadInt16LittleEndian(
                            sourceBytes.Slice(sampleOffset, sizeof(short))) / 32768.0,
                    AudioSampleEncoding.IeeeFloat =>
                        BitConverter.Int32BitsToSingle(
                            BinaryPrimitives.ReadInt32LittleEndian(
                                sourceBytes.Slice(sampleOffset, sizeof(float)))),
                    _ => throw new InvalidOperationException("Unsupported audio encoding."),
                };
            }

            destination[frame] = (float)(sum / sourceFormat.Channels);
            frameOffset += sourceFormat.BytesPerFrame;
        }
    }

    private byte[] Resample(ReadOnlySpan<float> monoSamples)
    {
        var absoluteStart = totalSourceFrames;
        var absoluteEnd = absoluteStart + monoSamples.Length - 1L;
        var estimatedSamples = checked(
            (int)Math.Ceiling((monoSamples.Length + 1) / sourceFramesPerOutputSample) + 2);
        var output = new byte[checked(estimatedSamples * sizeof(short))];
        var outputOffset = 0;

        while (Math.Floor(nextOutputSourcePosition) + 1 <= absoluteEnd)
        {
            var leftIndex = (long)Math.Floor(nextOutputSourcePosition);
            var fraction = nextOutputSourcePosition - leftIndex;
            var left = ReadAbsoluteSample(leftIndex, absoluteStart, monoSamples);
            var right = ReadAbsoluteSample(leftIndex + 1, absoluteStart, monoSamples);
            var interpolated = left + ((right - left) * (float)fraction);

            BinaryPrimitives.WriteInt16LittleEndian(
                output.AsSpan(outputOffset, sizeof(short)),
                Quantize(interpolated));
            outputOffset += sizeof(short);
            nextOutputSourcePosition += sourceFramesPerOutputSample;
        }

        previousMonoSample = monoSamples[^1];
        hasPreviousMonoSample = true;
        totalSourceFrames += monoSamples.Length;

        if (outputOffset == output.Length)
        {
            return output;
        }

        Array.Resize(ref output, outputOffset);
        return output;
    }

    private float ReadAbsoluteSample(
        long absoluteIndex,
        long absoluteStart,
        ReadOnlySpan<float> monoSamples)
    {
        if (absoluteIndex == absoluteStart - 1 && hasPreviousMonoSample)
        {
            return previousMonoSample;
        }

        var relativeIndex = checked((int)(absoluteIndex - absoluteStart));
        if ((uint)relativeIndex >= (uint)monoSamples.Length)
        {
            throw new InvalidOperationException("Resampler requested a sample outside the streaming window.");
        }

        return monoSamples[relativeIndex];
    }

    private static short Quantize(float sample)
    {
        if (sample >= 1.0f)
        {
            return short.MaxValue;
        }

        if (sample <= -1.0f)
        {
            return short.MinValue;
        }

        return checked((short)MathF.Round(sample * 32768.0f, MidpointRounding.ToEven));
    }
}
