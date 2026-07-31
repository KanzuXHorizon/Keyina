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

    private readonly Func<FeedbackSoundCue, bool> playSystemSound;
    private readonly Func<FeedbackSoundCue, bool> playFallback;

    public WindowsFeedbackSoundPlayer()
        : this(PlayWindowsSystemSound, PlayPinnedWave)
    {
    }

    public WindowsFeedbackSoundPlayer(Func<byte[], bool> playWave)
        : this(_ => false, cue => playWave(Waves[cue].Bytes))
    {
    }

    public WindowsFeedbackSoundPlayer(
        Func<FeedbackSoundCue, bool> playSystemSound,
        Func<FeedbackSoundCue, bool> playFallback)
    {
        this.playSystemSound = playSystemSound ?? throw new ArgumentNullException(nameof(playSystemSound));
        this.playFallback = playFallback ?? throw new ArgumentNullException(nameof(playFallback));
    }

    public void Play(FeedbackSoundCue cue)
    {
        if (cue == FeedbackSoundCue.None)
        {
            return;
        }

        try
        {
            if (!playSystemSound(cue))
            {
                _ = playFallback(cue);
            }
        }
        catch (Exception)
        {
            // Feedback is best-effort and must never break a shortcut or typing path.
        }
    }

    private static bool PlayWindowsSystemSound(FeedbackSoundCue cue) =>
        MessageBeep(cue switch
        {
            FeedbackSoundCue.Enabled or FeedbackSoundCue.Start => 0x00000040,
            FeedbackSoundCue.Success => 0x00000040,
            FeedbackSoundCue.Disabled or FeedbackSoundCue.Cancel => 0x00000030,
            FeedbackSoundCue.Error => 0x00000010,
            _ => 0xFFFFFFFF,
        });

    private static bool PlayPinnedWave(FeedbackSoundCue cue) =>
        PlaySound(Waves[cue].Pointer, IntPtr.Zero, PlaybackFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);

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
