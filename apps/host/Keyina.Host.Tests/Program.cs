using System.Reflection;
using Keyina.Host.Core;

namespace Keyina.Host.Tests;

internal static class Program
{
    private sealed record TestCase(string Name, MethodInfo Method);

    [STAThread]
    public static int Main(string[] args)
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
            .Where(item => !IsInteractive(item.Method))
            .Select(item => new TestCase(item.Attribute!.Name, item.Method))
            .OrderBy(test => test.Name, StringComparer.Ordinal)
            .ToArray();

        var filters = args
            .Where(argument => !string.Equals(
                argument,
                "--interactive",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (filters.Length > 0)
        {
            var exclusions = filters
                .Where(filter => filter.StartsWith('!'))
                .Select(filter => filter[1..])
                .Where(filter => filter.Length > 0)
                .ToArray();
            var inclusions = filters
                .Where(filter => !filter.StartsWith('!'))
                .ToArray();

            tests = tests
                .Where(test => inclusions.Length == 0 || inclusions.Any(filter =>
                    test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .Where(test => exclusions.All(filter =>
                    !test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        var failures = 0;
        foreach (var test in tests)
        {
            Console.WriteLine($"[RUN] {test.Name}");
            Console.Out.Flush();
            var previousSynchronizationContext = SynchronizationContext.Current;
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
            finally
            {
                SynchronizationContext.SetSynchronizationContext(
                    previousSynchronizationContext);
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
        return failures == 0 ? 0 : 1;
    }

    [KeyinaTest("build info exposes product identity and semantic version")]
    private static void BuildInfoExposesProductIdentity()
    {
        AssertEx.Equal("Keyina", BuildInfo.ProductName);
        var numericVersion = BuildInfo.ProductVersion.Split('-', 2)[0];
        AssertEx.True(
            Version.TryParse(numericVersion, out var version) && version.Major >= 0,
            "Build version does not start with a valid numeric semantic version.");
    }

    private static bool IsInteractive(MethodInfo method) =>
        method.IsDefined(typeof(KeyinaInteractiveTestAttribute), inherit: false) ||
        method.DeclaringType?.IsDefined(
            typeof(KeyinaInteractiveTestAttribute),
            inherit: false) == true;

    private static void ValidateSignature(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException(
                $"Test method {method.DeclaringType?.FullName}.{method.Name} must be static void with no parameters.");
        }
    }
}
