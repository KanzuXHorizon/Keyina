using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Windows.Hotkeys;

namespace Keyina.Host.Hotkeys;

public sealed record HotkeySelfTestResult(bool Success, string Code);

public static class HotkeySelfTest
{
    private const int TestHotkeyId = 0x4B59;
    private static readonly VirtualKey F24 = (VirtualKey)0x87;

    public static HotkeySelfTestResult Run()
    {
        using var received = new ManualResetEventSlim();
        try
        {
            using var window = new HotkeyMessageWindow();
            RegisteredHotkeyManager? manager = null;
            try
            {
                var expectedCommand = HotkeyCommand.CancelDictation;
                var receivedCommand = HotkeyCommand.None;
                window.Invoke(() =>
                {
                    manager = new RegisteredHotkeyManager(
                        nativeApi: null,
                        window.Handle);
                    manager.CommandReceived += (_, command) =>
                    {
                        receivedCommand = command;
                        received.Set();
                    };
                    window.HotkeyReceived += (_, id) => _ = manager.TryDispatch(id);
                    manager.Register(
                    [
                        new RegisteredHotkeyBinding(
                            TestHotkeyId,
                            new HotkeyChord(
                                HotkeyModifiers.Control |
                                HotkeyModifiers.Alt |
                                HotkeyModifiers.Shift,
                                F24),
                            expectedCommand),
                    ]);
                });

                if (!window.PostHotkeyForTest(TestHotkeyId) ||
                    !received.Wait(TimeSpan.FromSeconds(2)) ||
                    receivedCommand != expectedCommand)
                {
                    return new HotkeySelfTestResult(false, "hotkey_self_test_dispatch_failed");
                }

                return new HotkeySelfTestResult(true, "hotkey_self_test_ok");
            }
            finally
            {
                if (manager is not null)
                {
                    window.Invoke(manager.Dispose);
                }
            }
        }
        catch (HotkeyRegistrationException)
        {
            return new HotkeySelfTestResult(false, "hotkey_self_test_registration_conflict");
        }
        catch
        {
            return new HotkeySelfTestResult(false, "hotkey_self_test_failed");
        }
    }
}
