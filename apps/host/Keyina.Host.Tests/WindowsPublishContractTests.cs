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
}
