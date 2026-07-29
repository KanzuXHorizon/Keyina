namespace Keyina.Host.Core;

public sealed record HostState(
    bool VietnameseEnabled,
    bool Listening,
    string? ErrorCode)
{
    public static HostState Initial { get; } = new(
        VietnameseEnabled: true,
        Listening: false,
        ErrorCode: null);

    public TrayState TrayState => ErrorCode is not null
        ? TrayState.Error
        : Listening
            ? TrayState.Listening
            : VietnameseEnabled
                ? TrayState.VietnameseOn
                : TrayState.VietnameseOff;

    public string TrayAssetPath => TrayState switch
    {
        TrayState.VietnameseOn => "Assets/keyina-tray-active.ico",
        TrayState.VietnameseOff => "Assets/keyina-tray-inactive.ico",
        TrayState.Listening => "Assets/keyina-tray-listening.ico",
        TrayState.Error => "Assets/keyina-tray-inactive.ico",
        _ => throw new InvalidOperationException($"Unsupported tray state: {TrayState}"),
    };
}
