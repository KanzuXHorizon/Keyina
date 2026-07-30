using System.Reflection;
using Keyina.Host.Core;

namespace Keyina.Host.Tests;

internal static class Program
{
    private sealed record TestCase(string Name, MethodInfo Method);

    [STAThread]
    public static int Main()
    {
        var tests = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(method => new
            {
                Method = method,
                Attribute = method.GetCustomAttribute<KeyinaTestAttribute>(),
            })
            .Where(item => item.Attribute is not null)
            .Select(item => new TestCase(item.Attribute!.Name, item.Method))
            .OrderBy(test => test.Name, StringComparer.Ordinal)
            .ToArray();

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                ValidateSignature(test.Method);
                test.Method.Invoke(null, null);
                Console.WriteLine($"[PASS] {test.Name}");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                failures++;
                Console.Error.WriteLine($"[FAIL] {test.Name}: {exception.InnerException}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"[FAIL] {test.Name}: {exception}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
        return failures == 0 ? 0 : 1;
    }

    [KeyinaTest("build info exposes the product name")]
    private static void BuildInfoExposesProductName()
    {
        AssertEx.Equal("Keyina", BuildInfo.ProductName);
    }

    private static void ValidateSignature(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException(
                $"Test method {method.DeclaringType?.FullName}.{method.Name} must be static void with no parameters.");
        }
    }
}
