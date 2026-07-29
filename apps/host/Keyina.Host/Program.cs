using Keyina.Host.Core;

namespace Keyina.Host;

internal static class Program
{
    private const int AlreadyRunningExitCode = 17;
    private const string HostMutexName = "Local\\Keyina.Host";

    public static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            Console.WriteLine($"{BuildInfo.ProductName} {BuildInfo.ProductVersion}");
            return 0;
        }

        if (!SingleInstanceGuard.TryAcquire(HostMutexName, out var guard))
        {
            return AlreadyRunningExitCode;
        }

        using (guard)
        {
            return 0;
        }
    }
}
