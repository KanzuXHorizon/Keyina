namespace Keyina.Host.Diagnostics;

public static class ResidentInputResourceBudget
{
    public const long MaximumPrivateMemoryBytes = 10L * 1024 * 1024;
    public const int MaximumThreadCountDelta = 1;

    public static bool IsSatisfied(
        long privateMemoryBytes,
        int threadCountDelta,
        bool typingHookRunning,
        bool measurementContaminatedByInput) =>
        privateMemoryBytes <= MaximumPrivateMemoryBytes &&
        threadCountDelta <= MaximumThreadCountDelta &&
        typingHookRunning &&
        !measurementContaminatedByInput;
}
