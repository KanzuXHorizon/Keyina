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

    [KeyinaTest("readiness reports attention when installation exists but runtime connection is broken")]
    private static void BrokenRuntimeNeedsAttention()
    {
        var snapshot = KeyinaHealthSnapshot.Healthy with
        {
            EndToEndTypingPassed = false,
        };

        AssertEx.Equal(KeyinaReadiness.NeedsAttention, ReadinessMapper.Map(snapshot));
        AssertEx.Equal(TsfSetupState.NeedsRepair, ReadinessMapper.SetupState(snapshot));
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
