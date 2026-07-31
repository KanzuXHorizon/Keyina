using System.Text;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Windows.Feedback;

namespace Keyina.Host.Tests;

internal static class FeedbackWaveBuilderTests
{
    [KeyinaTest("feedback cues are bounded valid PCM wave data")]
    private static void FeedbackCuesAreValidBoundedWaveData()
    {
        foreach (var cue in new[]
                 {
                     FeedbackSoundCue.Enabled,
                     FeedbackSoundCue.Disabled,
                     FeedbackSoundCue.Start,
                     FeedbackSoundCue.Success,
                     FeedbackSoundCue.Cancel,
                     FeedbackSoundCue.Error,
                 })
        {
            var wave = FeedbackWaveBuilder.CreateCue(cue);

            AssertEx.True(wave.Length > 44, $"{cue} wave contains no sample data.");
            AssertEx.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
            AssertEx.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
            AssertEx.Equal("fmt ", Encoding.ASCII.GetString(wave, 12, 4));
            AssertEx.Equal((ushort)1, BitConverter.ToUInt16(wave, 20));
            AssertEx.Equal((ushort)1, BitConverter.ToUInt16(wave, 22));
            AssertEx.Equal(22_050, BitConverter.ToInt32(wave, 24));
            AssertEx.Equal((ushort)16, BitConverter.ToUInt16(wave, 34));
            AssertEx.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));

            var byteRate = BitConverter.ToInt32(wave, 28);
            var dataBytes = BitConverter.ToInt32(wave, 40);
            var duration = TimeSpan.FromSeconds((double)dataBytes / byteRate);
            AssertEx.True(
                duration >= TimeSpan.FromMilliseconds(40),
                $"{cue} was too short: {duration.TotalMilliseconds:F1} ms.");
            AssertEx.True(
                duration <= TimeSpan.FromMilliseconds(180),
                $"{cue} was too long: {duration.TotalMilliseconds:F1} ms.");

            var peak = Enumerable.Range(0, dataBytes / sizeof(short))
                .Select(index => Math.Abs((int)BitConverter.ToInt16(
                    wave,
                    44 + (index * sizeof(short)))))
                .Max();
            AssertEx.True(
                peak >= 8_500,
                $"{cue} feedback was too quiet: peak {peak}.");
            AssertEx.True(
                peak <= 12_000,
                $"{cue} feedback was too loud or at clipping risk: peak {peak}.");
        }
    }

    [KeyinaTest("feedback cue builder rejects the no-sound sentinel")]
    private static void NoneCueIsRejected()
    {
        try
        {
            _ = FeedbackWaveBuilder.CreateCue(FeedbackSoundCue.None);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        throw new InvalidOperationException("FeedbackSoundCue.None should not create audio data.");
    }
}
