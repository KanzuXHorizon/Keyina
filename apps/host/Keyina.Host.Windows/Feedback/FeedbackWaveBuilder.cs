using System.Buffers.Binary;
using System.Text;
using Keyina.Host.Core.Feedback;

namespace Keyina.Host.Windows.Feedback;

public static class FeedbackWaveBuilder
{
    private const int SampleRate = 22_050;
    private const int HeaderSize = 44;
    private const double PeakAmplitude = short.MaxValue * 0.28;

    public static byte[] CreateCue(FeedbackSoundCue cue)
    {
        var definition = cue switch
        {
            FeedbackSoundCue.Enabled => new CueDefinition(620, 880, 90),
            FeedbackSoundCue.Disabled => new CueDefinition(520, 360, 90),
            FeedbackSoundCue.Start => new CueDefinition(440, 660, 75),
            FeedbackSoundCue.Success => new CueDefinition(660, 990, 110),
            FeedbackSoundCue.Cancel => new CueDefinition(480, 320, 85),
            FeedbackSoundCue.Error => new CueDefinition(260, 220, 130),
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, null),
        };

        var sampleCount = checked(SampleRate * definition.DurationMilliseconds / 1_000);
        var dataLength = checked(sampleCount * sizeof(short));
        var wave = new byte[checked(HeaderSize + dataLength)];
        WriteHeader(wave, dataLength);

        for (var index = 0; index < sampleCount; index++)
        {
            var progress = index / (double)Math.Max(1, sampleCount - 1);
            var frequency = definition.StartFrequency +
                ((definition.EndFrequency - definition.StartFrequency) * progress);
            var envelope = Math.Sin(Math.PI * progress);
            var sample = Math.Sin(2D * Math.PI * frequency * index / SampleRate);
            var value = checked((short)Math.Round(
                sample * envelope * PeakAmplitude,
                MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteInt16LittleEndian(
                wave.AsSpan(HeaderSize + (index * sizeof(short)), sizeof(short)),
                value);
        }

        return wave;
    }

    private static void WriteHeader(Span<byte> destination, int dataLength)
    {
        Encoding.ASCII.GetBytes("RIFF", destination[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4, 4), 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVE", destination.Slice(8, 4));
        Encoding.ASCII.GetBytes("fmt ", destination.Slice(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(22, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(24, 4), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(28, 4),
            SampleRate * sizeof(short));
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(32, 2), sizeof(short));
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(34, 2), 16);
        Encoding.ASCII.GetBytes("data", destination.Slice(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(40, 4), dataLength);
    }

    private readonly record struct CueDefinition(
        double StartFrequency,
        double EndFrequency,
        int DurationMilliseconds);
}
