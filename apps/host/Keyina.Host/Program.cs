using Keyina.Host.Core;

namespace Keyina.Host;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            Console.WriteLine($"{BuildInfo.ProductName} {BuildInfo.ProductVersion}");
            return 0;
        }

        return 0;
    }
}
