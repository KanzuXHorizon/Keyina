using Keyina.Host.Windows.Startup;

namespace Keyina.Host.Tests;

internal static class NativeResidentPathResolverTests
{
    [KeyinaTest("native resident path resolves beside the managed companion")]
    private static void ResolvesSiblingExecutable()
    {
        var managed = Path.GetFullPath(Path.Combine("publish", "Keyina.Host.exe"));

        var native = NativeResidentPathResolver.ResolveSibling(managed);

        AssertEx.Equal(
            Path.Combine(Path.GetDirectoryName(managed)!, "KeyinaInput.exe"),
            native);
        AssertEx.True(Path.IsPathFullyQualified(native),
            "Native resident path was not fully qualified.");
    }

    [KeyinaTest("native resident path rejects relative quoted and empty inputs")]
    private static void RejectsUnsafeInputs()
    {
        AssertThrows<ArgumentException>(() =>
            NativeResidentPathResolver.ResolveSibling("Keyina.Host.exe"));
        AssertThrows<ArgumentException>(() =>
            NativeResidentPathResolver.ResolveSibling("C:\\Keyina\\\"Host.exe"));
        AssertThrows<ArgumentException>(() =>
            NativeResidentPathResolver.ResolveSibling(""));
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
