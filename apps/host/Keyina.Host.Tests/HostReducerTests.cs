using Keyina.Host.Core;

namespace Keyina.Host.Tests;

internal static class HostReducerTests
{
    [KeyinaTest("host initial state maps to Vietnamese enabled tray icon")]
    private static void InitialStateIsVietnameseOn()
    {
        var state = HostState.Initial;
        AssertEx.True(state.VietnameseEnabled, "Vietnamese input should start enabled.");
        AssertEx.False(state.Listening, "Host must not start in listening state.");
        AssertEx.Equal(TrayState.VietnameseOn, state.TrayState);
        AssertEx.Equal("Assets/keyina-tray-active.ico", state.TrayAssetPath);
    }

    [KeyinaTest("input mode events switch between familiar enabled and disabled tray states")]
    private static void InputModeEventsSwitchTrayState()
    {
        var disabled = HostReducer.Reduce(HostState.Initial, new InputModeChanged(false));
        AssertEx.False(disabled.VietnameseEnabled, "Vietnamese input was not disabled.");
        AssertEx.Equal(TrayState.VietnameseOff, disabled.TrayState);
        AssertEx.Equal("Assets/keyina-tray-inactive.ico", disabled.TrayAssetPath);

        var enabled = HostReducer.Reduce(disabled, new InputModeChanged(true));
        AssertEx.True(enabled.VietnameseEnabled, "Vietnamese input was not enabled.");
        AssertEx.Equal(TrayState.VietnameseOn, enabled.TrayState);
    }

    [KeyinaTest("listening tray state takes precedence over input mode")]
    private static void ListeningTakesPrecedenceOverInputMode()
    {
        var disabled = HostReducer.Reduce(HostState.Initial, new InputModeChanged(false));
        var listening = HostReducer.Reduce(disabled, new ListeningStarted());
        AssertEx.True(listening.Listening, "Listening state was not recorded.");
        AssertEx.Equal(TrayState.Listening, listening.TrayState);
        AssertEx.Equal("Assets/keyina-tray-listening.ico", listening.TrayAssetPath);

        var stopped = HostReducer.Reduce(listening, new ListeningStopped());
        AssertEx.False(stopped.Listening, "Listening state was not cleared.");
        AssertEx.Equal(TrayState.VietnameseOff, stopped.TrayState);
    }

    [KeyinaTest("host errors override ordinary tray state and recovery restores it")]
    private static void ErrorAndRecoveryAreDeterministic()
    {
        var listening = HostReducer.Reduce(HostState.Initial, new ListeningStarted());
        var failed = HostReducer.Reduce(listening, new HostFailed("speech-auth"));
        AssertEx.Equal("speech-auth", failed.ErrorCode);
        AssertEx.Equal(TrayState.Error, failed.TrayState);
        AssertEx.Equal("Assets/keyina-tray-inactive.ico", failed.TrayAssetPath);

        var recovered = HostReducer.Reduce(failed, new HostRecovered());
        AssertEx.Equal<string?>(null, recovered.ErrorCode);
        AssertEx.Equal(TrayState.Listening, recovered.TrayState);
    }

    [KeyinaTest("host reducer rejects empty error codes")]
    private static void EmptyErrorCodeIsRejected()
    {
        var threw = false;
        try
        {
            HostReducer.Reduce(HostState.Initial, new HostFailed("   "));
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        AssertEx.True(threw, "Empty host error code should be rejected.");
    }
}
