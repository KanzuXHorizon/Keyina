namespace Keyina.Host.Tests;

internal static class WindowsPublishContractTests
{
    [KeyinaTest("Windows publish script bundles native resident managed companion engine and tray assets")]
    private static void PublishScriptContainsRequiredBundleContract()
    {
        var path = Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "publish.ps1");
        AssertEx.True(File.Exists(path), "Windows publish script was missing.");
        var script = File.ReadAllText(path);

        foreach (var required in new[]
                 {
                     "dotnet",
                     "publish",
                     "cmake",
                     "KeyinaInput.exe",
                     "Keyina.Host.exe",
                     "Keyina.Host.dll",
                     "KeyinaEngine.dll",
                     "keyina-tray-active.ico",
                     "keyina-tray-inactive.ico",
                     "--self-contained",
                     "false",
                 })
        {
            AssertEx.True(
                script.Contains(required, StringComparison.Ordinal),
                $"Publish script omitted required contract token: {required}.");
        }
    }

    [KeyinaTest("Windows release pipeline packages and verifies the native resident")]
    private static void ReleasePipelineContainsNativeResidentContract()
    {
        var buildPath = Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "build-release.ps1");
        var verifyPath = Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "verify-release.ps1");
        var buildScript = File.ReadAllText(buildPath);
        var verifyScript = File.ReadAllText(verifyPath);

        foreach (var required in new[]
                 {
                     "KeyinaInput.exe",
                     "windows-msvc-release",
                     "--typing-self-test",
                     "--tray-resource-self-test",
                     "--profile-reload-self-test",
                 })
        {
            AssertEx.True(
                buildScript.Contains(required, StringComparison.Ordinal),
                $"Release build omitted native resident contract token: {required}.");
        }

        foreach (var required in new[]
                 {
                     "KeyinaInput.exe",
                     "--typing-self-test",
                     "--tray-resource-self-test",
                     "--profile-reload-self-test",
                 })
        {
            AssertEx.True(
                verifyScript.Contains(required, StringComparison.Ordinal),
                $"Release verification omitted native resident contract token: {required}.");
        }
    }

    [KeyinaTest("Windows installer starts native resident and opens settings companion")]
    private static void InstallerUsesNativeResidentContract()
    {
        var path = Path.Combine(
            RepositoryPaths.Root,
            "installer",
            "Keyina.iss");
        var script = File.ReadAllText(path);

        foreach (var required in new[]
                 {
                     "KeyinaInput.exe",
                     "Keyina.Host.exe",
                     "Local\\Keyina.NativeInput",
                     "--companion-settings",
                 })
        {
            AssertEx.True(
                script.Contains(required, StringComparison.Ordinal),
                $"Installer omitted native resident contract token: {required}.");
        }
    }
}
