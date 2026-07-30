using Keyina.Host.Runtime;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class TsfSetupServiceTests
{
    [KeyinaTest("TSF setup reports missing DLL without launching privileged work")]
    private static void MissingDllNeedsSetup()
    {
        var platform = new FakeTsfSetupPlatform { FilePresent = false };
        var service = new TsfSetupService(platform, "C:\\Keyina\\KeyinaTsf.dll");

        var result = service.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();

        AssertEx.Equal(TsfSetupState.NotInstalled, result.State);
        AssertEx.Equal(0, platform.LaunchCount);
    }

    [KeyinaTest("TSF setup health distinguishes registered and broken installations")]
    private static void HealthStateIsTruthful()
    {
        var platform = new FakeTsfSetupPlatform
        {
            FilePresent = true,
            ComRegistered = true,
            ProfileRegistered = false,
        };
        var service = new TsfSetupService(platform, "C:\\Keyina\\KeyinaTsf.dll");

        var result = service.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();

        AssertEx.Equal(TsfSetupState.NeedsRepair, result.State);
        AssertEx.True(result.NativeDllPresent, "Native DLL should be reported present.");
        AssertEx.True(result.ComRegistered, "COM registration should be reported present.");
        AssertEx.False(result.TsfProfileRegistered, "TSF profile should be reported missing.");
    }

    [KeyinaTest("TSF registration constructs an elevated silent regsvr32 request")]
    private static void RegistrationUsesExplicitElevation()
    {
        var platform = new FakeTsfSetupPlatform
        {
            FilePresent = true,
            RegistrationExitCode = 0,
            RegisterAfterLaunch = true,
        };
        var service = new TsfSetupService(platform, "C:\\Keyina\\KeyinaTsf.dll");

        var result = service.RegisterAsync(CancellationToken.None).GetAwaiter().GetResult();

        AssertEx.True(result.Succeeded, "Registration should succeed after verified state is healthy.");
        AssertEx.NotNull(platform.LastRequest, "Registration did not launch the privileged request.");
        var request = platform.LastRequest!;
        AssertEx.Equal("runas", request.Verb);
        AssertEx.Equal("regsvr32.exe", Path.GetFileName(request.FileName));
        AssertEx.True(request.Arguments.Contains("/s", StringComparison.Ordinal), "Silent flag missing.");
        AssertEx.True(request.Arguments.Contains("\"C:\\Keyina\\KeyinaTsf.dll\"", StringComparison.Ordinal), "DLL path was not quoted.");
    }

    [KeyinaTest("TSF registration cancellation is reported without claiming readiness")]
    private static void RegistrationCancellationIsNotSuccess()
    {
        var platform = new FakeTsfSetupPlatform
        {
            FilePresent = true,
            RegistrationCancelled = true,
        };
        var service = new TsfSetupService(platform, "C:\\Keyina\\KeyinaTsf.dll");

        var result = service.RegisterAsync(CancellationToken.None).GetAwaiter().GetResult();

        AssertEx.False(result.Succeeded, "UAC cancellation must not be reported as success.");
        AssertEx.Equal("elevation_cancelled", result.ErrorCode);
    }

    private sealed class FakeTsfSetupPlatform : ITsfSetupPlatform
    {
        public bool FilePresent { get; init; }
        public bool ComRegistered { get; set; }
        public bool ProfileRegistered { get; set; }
        public bool RegisterAfterLaunch { get; init; }
        public int RegistrationExitCode { get; init; }
        public bool RegistrationCancelled { get; init; }
        public int LaunchCount { get; private set; }
        public TsfProcessRequest? LastRequest { get; private set; }

        public bool FileExists(string path) => FilePresent;
        public bool IsComRegistered() => ComRegistered;
        public bool IsProfileRegistered() => ProfileRegistered;

        public Task<TsfProcessResult> LaunchAsync(
            TsfProcessRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCount++;
            LastRequest = request;
            if (RegisterAfterLaunch && !RegistrationCancelled && RegistrationExitCode == 0)
            {
                ComRegistered = true;
                ProfileRegistered = true;
            }
            return Task.FromResult(RegistrationCancelled
                ? TsfProcessResult.CancelledResult
                : new TsfProcessResult(RegistrationExitCode, Cancelled: false));
        }

        public void OpenLanguageSettings() { }
    }
}
