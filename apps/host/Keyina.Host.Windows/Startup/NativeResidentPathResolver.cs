namespace Keyina.Host.Windows.Startup;

public static class NativeResidentPathResolver
{
    public const string ExecutableName = "KeyinaInput.exe";

    public static string ResolveSibling(string managedExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedExecutablePath);
        if (!Path.IsPathFullyQualified(managedExecutablePath))
        {
            throw new ArgumentException(
                "Managed executable path must be fully qualified.",
                nameof(managedExecutablePath));
        }
        if (managedExecutablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Managed executable path must not contain quotation marks.",
                nameof(managedExecutablePath));
        }

        var directory = Path.GetDirectoryName(managedExecutablePath)
            ?? throw new ArgumentException(
                "Managed executable path has no parent directory.",
                nameof(managedExecutablePath));
        return Path.Combine(directory, ExecutableName);
    }

    public static string ResolveCurrentProcessSibling() =>
        ResolveSibling(
            Environment.ProcessPath ??
            throw new InvalidOperationException(
                "The current process executable path is unavailable."));
}
