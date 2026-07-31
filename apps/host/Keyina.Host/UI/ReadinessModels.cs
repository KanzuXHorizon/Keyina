namespace Keyina.Host.UI;

public enum KeyinaReadiness
{
    Ready,
    NeedsSetup,
    NeedsAttention,
    Unavailable,
}

public enum TsfSetupState
{
    Ready,
    NotInstalled,
    NeedsRepair,
    Unavailable,
}

public sealed record KeyinaHealthSnapshot(
    bool OperatingSystemSupported,
    bool NativeDllPresent,
    bool ComRegistered,
    bool TsfProfileRegistered,
    bool HostHealthy,
    bool IpcConnected,
    bool EndToEndTypingPassed,
    DateTimeOffset? LastTypingTestAt,
    string? FocusedApplication,
    string? FailureCode)
{
    public static KeyinaHealthSnapshot Healthy { get; } = new(
        OperatingSystemSupported: true,
        NativeDllPresent: true,
        ComRegistered: true,
        TsfProfileRegistered: true,
        HostHealthy: true,
        IpcConnected: true,
        EndToEndTypingPassed: true,
        LastTypingTestAt: DateTimeOffset.UtcNow,
        FocusedApplication: "Test host",
        FailureCode: null);
}

public static class ReadinessMapper
{
    public static bool IsSystemFailureCode(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return false;
        }

        return errorCode.StartsWith("input_", StringComparison.Ordinal) ||
            errorCode.StartsWith("typing_", StringComparison.Ordinal) ||
            errorCode.StartsWith("hotkey_", StringComparison.Ordinal) ||
            errorCode.StartsWith("ipc_", StringComparison.Ordinal) ||
            errorCode.StartsWith("tsf_", StringComparison.Ordinal) ||
            errorCode.StartsWith("runtime_profile_", StringComparison.Ordinal) ||
            errorCode.StartsWith("native_", StringComparison.Ordinal);
    }

    public static KeyinaReadiness Map(KeyinaHealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.OperatingSystemSupported)
        {
            return KeyinaReadiness.Unavailable;
        }
        if (!snapshot.NativeDllPresent)
        {
            return KeyinaReadiness.NeedsSetup;
        }
        var explicitTypingTestFailed =
            snapshot.LastTypingTestAt is not null &&
            !snapshot.EndToEndTypingPassed;
        if (!snapshot.HostHealthy ||
            explicitTypingTestFailed ||
            snapshot.FailureCode is not null)
        {
            return KeyinaReadiness.NeedsAttention;
        }
        return KeyinaReadiness.Ready;
    }

    public static TsfSetupState SetupState(KeyinaHealthSnapshot snapshot) =>
        Map(snapshot) switch
        {
            KeyinaReadiness.Ready => TsfSetupState.Ready,
            KeyinaReadiness.NeedsSetup => TsfSetupState.NotInstalled,
            KeyinaReadiness.NeedsAttention => TsfSetupState.NeedsRepair,
            KeyinaReadiness.Unavailable => TsfSetupState.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
        };

    public static string Title(KeyinaHealthSnapshot snapshot) =>
        Map(snapshot) switch
        {
            KeyinaReadiness.Ready => "Sẵn sàng",
            KeyinaReadiness.NeedsSetup => "Cần thiết lập",
            KeyinaReadiness.NeedsAttention => "Cần xử lý",
            KeyinaReadiness.Unavailable => "Không khả dụng",
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
        };
}
