using Keyina.Host.Core;

namespace Keyina.Host.Tests;

internal static class Program
{
    private sealed record TestCase(string Name, Action Run);

    public static int Main()
    {
        TestCase[] tests =
        [
            new("build info exposes the product name", BuildInfoExposesProductName),
        ];

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"[PASS] {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"[FAIL] {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static void BuildInfoExposesProductName()
    {
        Equal("Keyina", BuildInfo.ProductName);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, received {actual}.");
        }
    }
}
