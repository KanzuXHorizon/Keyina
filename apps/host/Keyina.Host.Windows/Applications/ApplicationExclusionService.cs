using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Keyina.Host.Core.Applications;

namespace Keyina.Host.Windows.Applications;

public interface IApplicationExclusionService
{
    void Update(ApplicationPreferences preferences);

    bool IsDisabled(ApplicationFeature feature, int processId);

    bool IsForegroundDisabled(ApplicationFeature feature);

    string? GetForegroundExecutableName();
}

public sealed class ApplicationExclusionService : IApplicationExclusionService
{
    private const string MissingExecutable = "\0";
    private readonly ConcurrentDictionary<int, string> executableNames = [];
    private ApplicationPreferences preferences = ApplicationPreferences.Default;

    public void Update(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = preferences.Normalize();
        Volatile.Write(ref this.preferences, normalized);
        executableNames.Clear();
    }

    public bool IsDisabled(ApplicationFeature feature, int processId)
    {
        if (processId <= 0)
        {
            return false;
        }
        var executableName = ResolveExecutableName(processId);
        return executableName is not null &&
            Volatile.Read(ref preferences).IsDisabled(feature, executableName);
    }

    public bool IsForegroundDisabled(ApplicationFeature feature)
    {
        var processId = GetForegroundProcessId();
        return IsDisabled(feature, processId);
    }

    public string? GetForegroundExecutableName()
    {
        var processId = GetForegroundProcessId();
        return processId <= 0 ? null : ResolveExecutableName(processId);
    }

    private string? ResolveExecutableName(int processId)
    {
        var cached = executableNames.GetOrAdd(
            processId,
            static id => ResolveExecutableNameCore(id) ?? MissingExecutable);
        return string.Equals(cached, MissingExecutable, StringComparison.Ordinal)
            ? null
            : cached;
    }

    private static string? ResolveExecutableNameCore(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            string? executableName = null;
            try
            {
                executableName = Path.GetFileName(process.MainModule?.FileName);
            }
            catch (Exception exception) when (
                exception is System.ComponentModel.Win32Exception or
                    InvalidOperationException or NotSupportedException)
            {
                // Fall back to ProcessName when module inspection is unavailable.
            }

            executableName ??= string.IsNullOrWhiteSpace(process.ProcessName)
                ? null
                : process.ProcessName + ".exe";
            return executableName is null
                ? null
                : ApplicationPreferences.NormalizeExecutableName(executableName);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static int GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return 0;
        }
        _ = GetWindowThreadProcessId(window, out var processId);
        return unchecked((int)processId);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);
}
