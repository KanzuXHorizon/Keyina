using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class ReadinessTests
{
    [KeyinaTest("readiness reports ready only when every required typing component is healthy")]
    private static void ReadyRequiresEveryTypingComponent()
    {
        var snapshot = KeyinaHealthSnapshot.Healthy;

        AssertEx.Equal(KeyinaReadiness.Ready, ReadinessMapper.Map(snapshot));
        AssertEx.Equal("Sẵn sàng", ReadinessMapper.Title(snapshot));
    }

    [KeyinaTest("readiness reports setup when the native hook backend is absent")]
    private static void MissingNativeBackendNeedsSetup()
    {
        var snapshot = KeyinaHealthSnapshot.Healthy with
        {
            NativeDllPresent = false,
        };

        AssertEx.Equal(KeyinaReadiness.NeedsSetup, ReadinessMapper.Map(snapshot));
        AssertEx.Equal(TsfSetupState.NotInstalled, ReadinessMapper.SetupState(snapshot));
    }

    [KeyinaTest("readiness does not require optional TSF registration or IPC")]
    private static void OptionalIntegrationDoesNotBlockTypingReadiness()
    {
        var snapshot = KeyinaHealthSnapshot.Healthy with
        {
            ComRegistered = false,
            TsfProfileRegistered = false,
            IpcConnected = false,
        };

        AssertEx.Equal(KeyinaReadiness.Ready, ReadinessMapper.Map(snapshot));
    }

    [KeyinaTest("readiness treats an untested running input backend as ready")]
    private static void UntestedRunningBackendIsReady()
    {
        var snapshot = KeyinaHealthSnapshot.Healthy with
        {
            EndToEndTypingPassed = false,
            LastTypingTestAt = null,
        };

        AssertEx.Equal(KeyinaReadiness.Ready, ReadinessMapper.Map(snapshot));
    }

    [KeyinaTest("readiness reports attention after an explicit typing test fails")]
    private static void BrokenRuntimeNeedsAttention()
    {
        var snapshot = KeyinaHealthSnapshot.Healthy with
        {
            EndToEndTypingPassed = false,
            LastTypingTestAt = DateTimeOffset.UtcNow,
        };

        AssertEx.Equal(KeyinaReadiness.NeedsAttention, ReadinessMapper.Map(snapshot));
        AssertEx.Equal(TsfSetupState.NeedsRepair, ReadinessMapper.SetupState(snapshot));
    }

    [KeyinaTest("readiness ignores optional feature failures but keeps input failures")]
    private static void OnlySystemFailuresAffectReadiness()
    {
        AssertEx.False(
            ReadinessMapper.IsSystemFailureCode("speech_provider_error"),
            "Speech failure incorrectly degraded input readiness.");
        AssertEx.False(
            ReadinessMapper.IsSystemFailureCode("translation_auth_failed"),
            "Translation failure incorrectly degraded input readiness.");
        AssertEx.True(
            ReadinessMapper.IsSystemFailureCode("typing_hook_start_failed"),
            "Typing hook failure was omitted from input readiness.");
        AssertEx.True(
            ReadinessMapper.IsSystemFailureCode("hotkey_start_failed"),
            "Hotkey failure was omitted from input readiness.");
    }

    [KeyinaTest("readiness reports unavailable on unsupported operating systems")]
    private static void UnsupportedOperatingSystemIsUnavailable()
    {
        var snapshot = KeyinaHealthSnapshot.Healthy with
        {
            OperatingSystemSupported = false,
        };

        AssertEx.Equal(KeyinaReadiness.Unavailable, ReadinessMapper.Map(snapshot));
        AssertEx.Equal(TsfSetupState.Unavailable, ReadinessMapper.SetupState(snapshot));
    }
}
