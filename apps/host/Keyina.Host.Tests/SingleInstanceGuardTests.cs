using Keyina.Host;

namespace Keyina.Host.Tests;

internal static class SingleInstanceGuardTests
{
    [KeyinaTest("single instance guard rejects a concurrent host and releases ownership")]
    private static void GuardRejectsConcurrentOwner()
    {
        var name = $"Local\\Keyina.Host.Tests.{Guid.NewGuid():N}";
        AssertEx.True(SingleInstanceGuard.TryAcquire(name, out var first),
            "First host instance could not acquire the mutex.");
        AssertEx.NotNull(first, "First guard must not be null after successful acquisition.");

        try
        {
            AssertEx.False(SingleInstanceGuard.TryAcquire(name, out var second),
                "Second host instance unexpectedly acquired the same mutex.");
            AssertEx.Equal<SingleInstanceGuard?>(null, second);
        }
        finally
        {
            first!.Dispose();
        }

        AssertEx.True(SingleInstanceGuard.TryAcquire(name, out var third),
            "Mutex ownership was not released after disposing the first guard.");
        third!.Dispose();
    }
}
