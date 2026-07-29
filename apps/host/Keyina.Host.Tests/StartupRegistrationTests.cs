using Keyina.Host.Windows.Startup;

namespace Keyina.Host.Tests;

internal static class StartupRegistrationTests
{
    [KeyinaTest("startup registration writes a quoted current-user Run command and removes it idempotently")]
    private static void StartupLifecycleRoundTrips()
    {
        var valueName = $"Keyina.Tests.{Guid.NewGuid():N}";
        var executable = Path.Combine(Path.GetTempPath(), "Keyina Test Host.exe");
        var registration = new WindowsStartupRegistration(valueName, () => executable);

        try
        {
            registration.SetEnabled(false);
            AssertEx.True(!registration.IsEnabled, "Startup unexpectedly began enabled.");

            registration.SetEnabled(true);
            AssertEx.True(registration.IsEnabled, "Startup registration was not enabled.");
            AssertEx.Equal($"\"{executable}\" --background", registration.RegisteredCommand);

            registration.SetEnabled(true);
            AssertEx.True(registration.IsEnabled, "Repeated enable removed startup registration.");

            registration.SetEnabled(false);
            registration.SetEnabled(false);
            AssertEx.True(!registration.IsEnabled, "Startup registration remained after disable.");
        }
        finally
        {
            registration.SetEnabled(false);
        }
    }

    [KeyinaTest("startup registration rejects invalid names paths and oversized Run commands")]
    private static void InvalidStartupInputsAreRejected()
    {
        AssertThrows<ArgumentException>(() =>
            _ = new WindowsStartupRegistration("", () => "C:\\Keyina.exe"));
        AssertThrows<ArgumentException>(() =>
            _ = new WindowsStartupRegistration("Keyina", () => ""));
        AssertThrows<ArgumentException>(() =>
            _ = new WindowsStartupRegistration(
                "Keyina",
                () => $"C:\\{new string('a', 260)}\\Keyina.exe"));
        AssertThrows<ArgumentException>(() =>
            _ = new WindowsStartupRegistration(
                "Keyina",
                () => "C:\\Keyina\"bad.exe"));
    }

    [KeyinaTest("production startup target is stable and user scoped")]
    private static void ProductionTargetIsStable()
    {
        AssertEx.Equal("Keyina", StartupRegistrationDefaults.ValueName);
        AssertEx.Equal(
            "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            StartupRegistrationDefaults.RegistryPath);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
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
