using Keyina.Host.Windows.Credentials;

namespace Keyina.Host.Tests;

internal static class WindowsCredentialVaultTests
{
    [KeyinaTest("Windows Credential Manager writes reads overwrites and deletes a current user secret")]
    private static void CredentialLifecycleRoundTrips()
    {
        var vault = new WindowsCredentialVault();
        var target = $"Keyina.Tests/{Guid.NewGuid():N}";
        var first = $"test-{Guid.NewGuid():N}";
        var second = $"replacement-{Guid.NewGuid():N}";

        try
        {
            AssertEx.Equal<string?>(null, vault.Read(target));
            vault.Write(target, first);
            AssertEx.Equal(first, vault.Read(target));

            vault.Write(target, second);
            AssertEx.Equal(second, vault.Read(target));
            AssertEx.True(vault.Delete(target), "Existing credential was not deleted.");
            AssertEx.Equal<string?>(null, vault.Read(target));
            AssertEx.True(!vault.Delete(target), "Deleting a missing credential should return false.");
        }
        finally
        {
            vault.Delete(target);
        }
    }

    [KeyinaTest("Windows Credential Manager rejects invalid targets and oversized secrets")]
    private static void InvalidCredentialInputsAreRejected()
    {
        var vault = new WindowsCredentialVault();
        AssertThrows<ArgumentException>(() => vault.Write("", "secret"));
        AssertThrows<ArgumentException>(() => vault.Write("Keyina.Tests/invalid", ""));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            vault.Write("Keyina.Tests/oversized", new string('x', 2_000)));
        AssertThrows<ArgumentException>(() => vault.Read(""));
        AssertThrows<ArgumentException>(() => vault.Delete(""));
    }

    [KeyinaTest("production credential targets are stable and contain no secret")]
    private static void ProductionTargetsAreStable()
    {
        AssertEx.Equal("Keyina/Speechmatics/ApiKey", CredentialTargets.SpeechmaticsApiKey);
        AssertEx.Equal("Keyina/DeepL/ApiKey", CredentialTargets.DeepLApiKey);
        foreach (var target in new[]
                 {
                     CredentialTargets.SpeechmaticsApiKey,
                     CredentialTargets.DeepLApiKey,
                 })
        {
            AssertEx.True(
                !target.Contains("token", StringComparison.OrdinalIgnoreCase),
                "Credential target accidentally resembles a secret value.");
        }
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
