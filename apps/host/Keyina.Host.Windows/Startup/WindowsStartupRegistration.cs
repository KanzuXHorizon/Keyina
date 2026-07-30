using Microsoft.Win32;

namespace Keyina.Host.Windows.Startup;

public static class StartupRegistrationDefaults
{
    public const string ValueName = "Keyina";
    public const string RegistryPath =
        "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
}

public sealed class WindowsStartupRegistration
{
    private const int MaximumRunCommandCharacters = 260;

    private readonly string valueName;
    private readonly string command;

    public WindowsStartupRegistration(
        string valueName,
        Func<string> executablePathProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentNullException.ThrowIfNull(executablePathProvider);

        if (valueName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Startup value name must not contain control characters.",
                nameof(valueName));
        }

        var executablePath = executablePathProvider();
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "Startup executable path must be fully qualified.",
                nameof(executablePathProvider));
        }
        if (executablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Startup executable path must not contain quotation marks.",
                nameof(executablePathProvider));
        }

        var candidate = $"\"{executablePath}\" --background";
        if (candidate.Length > MaximumRunCommandCharacters)
        {
            throw new ArgumentException(
                $"Startup command cannot exceed {MaximumRunCommandCharacters} characters.",
                nameof(executablePathProvider));
        }

        this.valueName = valueName;
        command = candidate;
    }

    public static WindowsStartupRegistration CreateProduction() =>
        new(
            StartupRegistrationDefaults.ValueName,
            NativeResidentPathResolver.ResolveCurrentProcessSibling);

    public bool IsEnabled => string.Equals(
        RegisteredCommand,
        command,
        StringComparison.Ordinal);

    public string? RegisteredCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                StartupRegistrationDefaults.RegistryPath,
                writable: false);
            return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                StartupRegistrationDefaults.RegistryPath,
                writable: true)
                ?? throw new InvalidOperationException(
                    "Windows did not provide the current-user startup registry key.");
            key.SetValue(valueName, command, RegistryValueKind.String);
            return;
        }

        using var existing = Registry.CurrentUser.OpenSubKey(
            StartupRegistrationDefaults.RegistryPath,
            writable: true);
        existing?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
