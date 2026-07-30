using System.Runtime.InteropServices;
using Keyina.Host.Core.Feedback;

namespace Keyina.Host.Windows.Feedback;

public interface IFeedbackSoundPlayer
{
    void Play(FeedbackSoundCue cue);
}

public sealed class WindowsFeedbackSoundPlayer : IFeedbackSoundPlayer
{
    private const uint SoundAsync = 0x0001;
    private const uint SoundNoDefault = 0x0002;
    private const uint SoundMemory = 0x0004;
    private const uint SoundNoStop = 0x0010;
    private const uint SoundSystem = 0x00200000;
    private const uint PlaybackFlags =
        SoundAsync | SoundNoDefault | SoundMemory | SoundNoStop | SoundSystem;

    private static readonly Dictionary<FeedbackSoundCue, PinnedWave> Waves =
        Enum.GetValues<FeedbackSoundCue>()
            .Where(cue => cue != FeedbackSoundCue.None)
            .ToDictionary(
                cue => cue,
                cue => new PinnedWave(FeedbackWaveBuilder.CreateCue(cue)));

    private readonly Func<FeedbackSoundCue, bool> play;

    public WindowsFeedbackSoundPlayer()
        : this(PlayPinnedWave)
    {
    }

    public WindowsFeedbackSoundPlayer(Func<byte[], bool> playWave)
    {
        ArgumentNullException.ThrowIfNull(playWave);
        play = cue => playWave(Waves[cue].Bytes);
    }

    private WindowsFeedbackSoundPlayer(Func<FeedbackSoundCue, bool> play)
    {
        this.play = play;
    }

    public void Play(FeedbackSoundCue cue)
    {
        if (cue == FeedbackSoundCue.None)
        {
            return;
        }

        try
        {
            _ = play(cue);
        }
        catch (Exception)
        {
            // Feedback is best-effort and must never break a shortcut or typing path.
        }
    }

    private static bool PlayPinnedWave(FeedbackSoundCue cue) =>
        PlaySound(Waves[cue].Pointer, IntPtr.Zero, PlaybackFlags);

    [DllImport("winmm.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(
        IntPtr sound,
        IntPtr module,
        uint flags);

    private sealed class PinnedWave
    {
        private readonly GCHandle handle;

        public PinnedWave(byte[] bytes)
        {
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
            Pointer = handle.AddrOfPinnedObject();
        }

        public byte[] Bytes { get; }

        public IntPtr Pointer { get; }
    }
}
