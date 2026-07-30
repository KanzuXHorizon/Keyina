namespace Keyina.Host.Runtime;

public sealed record TsfHealthResult(
    Keyina.Host.UI.TsfSetupState State,
    bool NativeDllPresent,
    bool ComRegistered,
    bool TsfProfileRegistered,
    string? ErrorCode);

public sealed record TsfRegistrationResult(
    bool Succeeded,
    string Message,
    string? ErrorCode);

public sealed record TsfProcessRequest(
    string FileName,
    string Arguments,
    string Verb,
    string WorkingDirectory);

public sealed record TsfProcessResult(int ExitCode, bool Cancelled)
{
    public static TsfProcessResult CancelledResult { get; } = new(-1, Cancelled: true);
}

public interface ITsfSetupPlatform
{
    bool FileExists(string path);
    bool IsComRegistered();
    bool IsProfileRegistered();
    Task<TsfProcessResult> LaunchAsync(TsfProcessRequest request, CancellationToken cancellationToken);
    void OpenLanguageSettings();
}
