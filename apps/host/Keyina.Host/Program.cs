using Keyina.Host.Core;
using Keyina.Host.Speech;

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

        if (args.Contains("--speech-self-test", StringComparer.Ordinal))
        {
            var result = SpeechSelfTest.RunAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Console.WriteLine(result.Code);
            return result.Success ? 0 : 1;
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
