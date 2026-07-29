namespace Keyina.Host.Tests;

internal static class AssertEx
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                message ?? $"Expected {expected}, received {actual}.");
        }
    }

    public static void NotNull<T>(T? value, string message) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }
}
