using System.Text.RegularExpressions;
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
                     "true",
                 })
        {
            AssertEx.True(
                script.Contains(required, StringComparison.Ordinal),
                $"Publish script omitted required contract token: {required}.");
        }
    }

    [KeyinaTest("native resident declares per-monitor-v2 DPI awareness")]
    private static void NativeResidentDeclaresPerMonitorV2DpiAwareness()
    {
        var manifestPath = Path.Combine(
            RepositoryPaths.Root,
            "platform",
            "windows",
            "input",
            "KeyinaInput.manifest");
        AssertEx.True(File.Exists(manifestPath),
            "Native resident manifest was missing.");

        var manifest = XDocument.Load(manifestPath);
        var settings = manifest.Descendants()
            .Where(element => element.Name.LocalName is "dpiAware" or "dpiAwareness")
            .Select(element => element.Value.Trim())
            .ToArray();
        AssertEx.True(
            settings.Contains("true/pm", StringComparer.OrdinalIgnoreCase),
            "Legacy Windows DPI awareness fallback was not per-monitor aware.");
        AssertEx.True(
            settings.Any(value => value.Contains(
                "PerMonitorV2",
                StringComparison.OrdinalIgnoreCase)),
            "Native resident did not request PerMonitorV2 DPI awareness.");

        var cmake = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "platform",
            "windows",
            "input",
            "CMakeLists.txt"));
        AssertEx.True(
            cmake.Contains("KeyinaInput.manifest", StringComparison.Ordinal),
            "Native resident build did not embed its DPI manifest.");
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

    [KeyinaTest("release restores managed dependencies before cleaning outputs")]
    private static void ReleaseRestoresBeforeClean()
    {
        var releaseScript = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "build-release.ps1"));
        var restore = releaseScript.IndexOf(
            "@('restore', 'Keyina.slnx')",
            StringComparison.Ordinal);
        var clean = releaseScript.IndexOf(
            "@('clean', 'Keyina.slnx', '-c', 'Release')",
            StringComparison.Ordinal);

        AssertEx.True(
            restore >= 0 && clean > restore,
            "Release must restore managed packages and runtime packs before dotnet clean.");
    }

    [KeyinaTest("desktop interactive native tests are opt in locally and explicit in CI")]
    private static void DesktopInteractiveTestsAreExplicit()
    {
        var rootCmake = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "CMakeLists.txt"));
        var inputCmake = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "platform",
            "windows",
            "input",
            "CMakeLists.txt"));
        var ciWorkflow = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            ".github",
            "workflows",
            "ci.yml"));
        var releaseWorkflow = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            ".github",
            "workflows",
            "release.yml"));
        var releaseScript = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "build-release.ps1"));
        var verifyScript = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "verify-release.ps1"));

        AssertEx.True(
            Regex.IsMatch(
                rootCmake,
                @"option\s*\(\s*KEYINA_ENABLE_INTERACTIVE_DESKTOP_TESTS\b[\s\S]*?\bOFF\s*\)",
                RegexOptions.CultureInvariant),
            "Interactive desktop CMake option is not default-off.");
        AssertEx.True(
            inputCmake.Contains(
                "if(KEYINA_ENABLE_INTERACTIVE_DESKTOP_TESTS)",
                StringComparison.Ordinal) &&
            inputCmake.Contains(
                "interactive-desktop",
                StringComparison.Ordinal),
            "Desktop tests are not conditionally registered and labeled.");
        AssertEx.True(
            ciWorkflow.Contains(
                "-DKEYINA_ENABLE_INTERACTIVE_DESKTOP_TESTS=ON",
                StringComparison.Ordinal),
            "CI does not explicitly enable desktop-interactive tests.");
        AssertEx.True(
            releaseWorkflow.Contains(
                "-RunDesktopInteractiveTests",
                StringComparison.Ordinal) &&
            releaseScript.Contains(
                "desktopInteractiveTestsEnabled",
                StringComparison.Ordinal) &&
            verifyScript.Contains(
                "desktopInteractiveTestsEnabled",
                StringComparison.Ordinal),
            "Release build or verification does not explicitly opt into desktop tests.");
    }

    [KeyinaTest("release packaging publishes from an isolated staging directory")]
    private static void ReleasePackagingUsesStagingDirectory()
    {
        var releaseScript = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "build-release.ps1"));

        foreach (var required in new[]
                 {
                     ".staging-",
                     "finalArtifactRoot",
                     "Move-Item",
                     "Remove-DirectoryWithRetry $finalArtifactRoot",
                 })
        {
            AssertEx.True(
                releaseScript.Contains(required, StringComparison.Ordinal),
                $"Release packaging omitted staging contract token: {required}.");
        }
    }

    [KeyinaTest("release manifest checksums and installer metadata are verified one to one")]
    private static void ReleaseArtifactsUseStrictVerification()
    {
        var buildScript = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "build-release.ps1"));
        var verifyScript = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "verify-release.ps1"));

        foreach (var required in new[]
                 {
                     "schema_version = 2",
                     "installer_lifecycle_verified",
                     "build_test_suites_skipped",
                     "desktop_interactive_tests_skipped",
                     "preserved_user_data_directory",
                 })
        {
            AssertEx.True(
                buildScript.Contains(required, StringComparison.Ordinal),
                $"Release manifest omitted required field: {required}.");
        }

        foreach (var required in new[]
                 {
                     "Duplicate checksum entry",
                     "Duplicate manifest artifact",
                     "resolved to $($matches.Count) files",
                     "Artifact length mismatch",
                     "Manifest hash mismatch",
                     "ProductVersion",
                     "ProductName",
                 })
        {
            AssertEx.True(
                verifyScript.Contains(required, StringComparison.Ordinal),
                $"Release verification omitted strict artifact check: {required}.");
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

    [KeyinaTest("Windows installer launches interactively and remains silent safe")]
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
                     "--open-settings",
                     "--exit",
                     "[UninstallRun]",
                     "RunOnceId:",
                     "skipifsilent",
                     "RegDeleteValue",
                     "{userstartup}\\Keyina.lnk",
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
                    StringComparison.Ordinal) &&
                !line.Contains("--open-settings", StringComparison.Ordinal) &&
                !line.Contains("--exit", StringComparison.Ordinal));
        AssertEx.True(
            residentRunLine.Contains("skipifsilent", StringComparison.OrdinalIgnoreCase),
            "Silent install and upgrade must not leave the native resident running.");

        var settingsLaunchLines = script
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("--open-settings", StringComparison.Ordinal))
            .ToArray();
        AssertEx.True(
            settingsLaunchLines.Length >= 2 && settingsLaunchLines.All(line =>
                line.Contains("{#MyAppResidentExeName}", StringComparison.Ordinal)),
            "Installer settings entry points must forward through the native resident.");
        AssertEx.True(
            !script.Contains(
                "Name: \"{localappdata}\\Keyina\"",
                StringComparison.OrdinalIgnoreCase),
            "Installer must preserve the user configuration directory on uninstall.");
    }

    [KeyinaTest("installer lifecycle verification is integrated into Windows release")]
    private static void InstallerLifecycleVerificationIsIntegrated()
    {
        var lifecyclePath = Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "test-installer.ps1");
        var lifecycleBuilderPath = Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "build-lifecycle-installer.ps1");
        AssertEx.True(
            File.Exists(lifecyclePath),
            "Installer lifecycle verifier was missing.");
        AssertEx.True(
            File.Exists(lifecycleBuilderPath),
            "Isolated lifecycle installer builder was missing.");

        var lifecycle = File.ReadAllText(lifecyclePath);
        foreach (var required in new[]
                 {
                     "InstallerPath",
                     "Version",
                     "/CURRENTUSER",
                     "/VERYSILENT",
                     "/NOICONS",
                     "unins000.exe",
                     "settings.json",
                     "Keyina.Host.exe",
                     "KeyinaInput.exe",
                 })
        {
            AssertEx.True(
                lifecycle.Contains(required, StringComparison.Ordinal),
                $"Installer lifecycle verifier omitted required token: {required}.");
        }

        foreach (var scriptName in new[] { "build-release.ps1", "verify-release.ps1" })
        {
            var releaseScript = File.ReadAllText(Path.Combine(
                RepositoryPaths.Root,
                "scripts",
                "windows",
                scriptName));
            AssertEx.True(
                releaseScript.Contains("test-installer.ps1", StringComparison.Ordinal) &&
                releaseScript.Contains("build-lifecycle-installer.ps1", StringComparison.Ordinal),
                $"{scriptName} does not invoke isolated installer lifecycle verification.");
        }
    }

    [KeyinaTest("duplicate native launch forwards settings without creating another resident")]
    private static void NativeResidentForwardsSettingsToExistingInstance()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "platform",
            "windows",
            "input",
            "native_resident.cpp"));

        foreach (var required in new[]
                 {
                     "Local\\\\Keyina.NativeInput",
                     "--open-settings",
                     "--exit",
                     "FindWindowW(kWindowClassName",
                     "SendMessageTimeoutW(",
                     "runtime.RequestOpenSettings()",
                     "kSettingsMenuCommand",
                     "kExitMenuCommand",
                     "ForwardCommandToExistingResident",
                 })
        {
            AssertEx.True(
                source.Contains(required, StringComparison.Ordinal),
                $"Native resident omitted singleton forwarding token: {required}.");
        }
    }

    [KeyinaTest("opening the managed executable delegates to the native resident")]
    private static void ManagedExecutableDelegatesToNativeResident()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "apps",
            "host",
            "Keyina.Host",
            "Program.cs"));

        AssertEx.True(
            source.Contains(
                "return NativeResidentLauncher.TryOpenSettings() ? 0 : 1;",
                StringComparison.Ordinal),
            "Opening Keyina.Host directly can still create a competing managed resident.");
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
