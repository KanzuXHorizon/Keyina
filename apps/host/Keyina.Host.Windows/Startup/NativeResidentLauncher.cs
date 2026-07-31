using System.ComponentModel;
using System.Diagnostics;

namespace Keyina.Host.Windows.Startup;

public static class NativeResidentLauncher
{
    private const string NativeResidentMutexName = "Local\\Keyina.NativeInput";

    public static bool TryEnsureRunning()
    {
        if (IsRunning())
        {
            return true;
        }

        try
        {
            var executablePath =
                NativeResidentPathResolver.ResolveCurrentProcessSibling();
            if (!File.Exists(executablePath))
            {
                return false;
            }

            var workingDirectory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return false;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--background",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
            return process is not null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    private static bool IsRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(
                    NativeResidentMutexName,
                    out var mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
