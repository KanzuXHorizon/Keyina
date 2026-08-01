namespace Keyina.Host.Core.Overlay;

public enum KeystrokeOverlayMotionLevel
{
    Adaptive,
    Full,
    Reduced,
    Off,
}

public enum KeystrokeOverlayFallbackCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

public sealed record KeystrokeOverlayPreferences(
    bool Enabled,
    KeystrokeOverlayMotionLevel Motion,
    int SizePercent,
    int OpacityPercent,
    int HideDelayMilliseconds,
    KeystrokeOverlayFallbackCorner FallbackCorner,
    bool PresentationMode,
    bool PerKeySoundEnabled,
    int SoundVolumePercent)
{
    public const int MinimumSizePercent = 75;
    public const int MaximumSizePercent = 150;
    public const int MinimumOpacityPercent = 25;
    public const int MaximumOpacityPercent = 100;
    public const int MinimumHideDelayMilliseconds = 500;
    public const int MaximumHideDelayMilliseconds = 2_000;
    public const int MinimumSoundVolumePercent = 0;
    public const int MaximumSoundVolumePercent = 100;

    public static KeystrokeOverlayPreferences Default { get; } = new(
        Enabled: false,
        Motion: KeystrokeOverlayMotionLevel.Adaptive,
        SizePercent: 100,
        OpacityPercent: 92,
        HideDelayMilliseconds: 900,
        FallbackCorner: KeystrokeOverlayFallbackCorner.BottomRight,
        PresentationMode: false,
        PerKeySoundEnabled: false,
        SoundVolumePercent: 30);

    public void Validate()
    {
        if (!Enum.IsDefined(Motion))
        {
            throw new ArgumentOutOfRangeException(nameof(Motion));
        }
        if (!Enum.IsDefined(FallbackCorner))
        {
            throw new ArgumentOutOfRangeException(nameof(FallbackCorner));
        }
        if (SizePercent is < MinimumSizePercent or > MaximumSizePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(SizePercent));
        }
        if (OpacityPercent is < MinimumOpacityPercent or > MaximumOpacityPercent)
        {
            throw new ArgumentOutOfRangeException(nameof(OpacityPercent));
        }
        if (HideDelayMilliseconds is < MinimumHideDelayMilliseconds or > MaximumHideDelayMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(HideDelayMilliseconds));
        }
        if (SoundVolumePercent is < MinimumSoundVolumePercent or > MaximumSoundVolumePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(SoundVolumePercent));
        }
    }
}
