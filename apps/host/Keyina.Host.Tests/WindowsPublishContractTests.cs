using System.Xml.Linq;

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

    [KeyinaTest("release version flows from the shared property into native and managed builds")]
    private static void ReleaseVersionUsesOneSourceOfTruth()
    {
        var propsPath = Path.Combine(RepositoryPaths.Root, "Directory.Build.props");
        var version = XDocument.Load(propsPath)
            .Descendants("KeyinaVersion")
            .Select(element => element.Value.Trim())
            .Single();
        var cmake = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "CMakeLists.txt"));
        var releaseScript = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "build-release.ps1"));
        var hostLockFile = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "apps",
            "host",
            "Keyina.Host",
            "packages.lock.json"));

        AssertEx.True(
            cmake.Contains(
                $"set(KEYINA_VERSION \"{version}\" CACHE STRING",
                StringComparison.Ordinal),
            "CMake default version diverged from Directory.Build.props.");
        AssertEx.True(
            cmake.Contains(
                "project(Keyina VERSION \"${KEYINA_VERSION}\" LANGUAGES CXX)",
                StringComparison.Ordinal),
            "CMake project metadata does not consume KEYINA_VERSION.");
        AssertEx.True(
            releaseScript.Contains(
                "\"-DKEYINA_VERSION=$Version\"",
                StringComparison.Ordinal),
            "Release packaging does not pass the requested version to CMake.");
        AssertEx.True(
            hostLockFile.Contains(
                $"\"Keyina.Host.Core\": \"[{version}, )\"",
                StringComparison.Ordinal),
            "NuGet project dependency lock diverged from KeyinaVersion.");
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

        var residentRunLine = script
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line =>
                line.StartsWith("Filename:", StringComparison.Ordinal) &&
                line.Contains(
                    "{#MyAppResidentExeName}",
                    StringComparison.Ordinal));
        AssertEx.True(
            !residentRunLine.Contains("skipifsilent", StringComparison.OrdinalIgnoreCase),
            "Silent install and upgrade must start the native resident.");
    }

    [KeyinaTest("settings companion restores a missing native resident")]
    private static void SettingsCompanionRestoresNativeResident()
    {
        var path = Path.Combine(
            RepositoryPaths.Root,
            "apps",
            "host",
            "Keyina.Host",
            "Program.cs");
        var source = File.ReadAllText(path);

        AssertEx.True(
            source.Contains(
                "NativeResidentLauncher.TryEnsureRunning()",
                StringComparison.Ordinal),
            "Opening settings does not recover a stopped native resident.");
    }
}
