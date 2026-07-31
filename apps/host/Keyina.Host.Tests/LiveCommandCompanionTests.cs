using System.Diagnostics;
using System.Runtime.InteropServices;
using Keyina.Host.Runtime;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class LiveCommandCompanionTests
{
    private const uint WindowMessageClose = 0x0010;

    [KeyinaTest("live command companion translates a selection in a real focused textbox")]
    private static void LiveCommandCompanionTranslatesSelection()
    {
        if (!IsEnabled("KEYINA_RUN_LIVE_COMMAND_TRANSLATION_TEST"))
        {
            return;
        }

        using var target = new LiveInputTarget("Xin chào");
        target.FocusAndSelectAll();
        AssertFocusedTarget(target);

        using var companion = StartCompanion(CompanionCommand.TranslateSelection);
        var previewWindow = WaitForWindow(
            companion,
            "Bản dịch",
            TimeSpan.FromSeconds(20));
        AssertEx.True(
            previewWindow != nint.Zero,
            companion.HasExited
                ? $"Translation companion exited before showing a preview: {companion.ExitCode}."
                : "Translation companion did not show its preview window.");

        _ = PostMessageW(
            previewWindow,
            WindowMessageClose,
            nint.Zero,
            nint.Zero);
        AssertEx.True(
            companion.WaitForExit(10_000),
            "Translation companion did not exit after its preview closed.");
        AssertEx.Equal(0, companion.ExitCode);
    }

    [KeyinaTest("live command companion starts and cancels speech in a real focused textbox")]
    private static void LiveCommandCompanionStartsAndCancelsSpeech()
    {
        if (!IsEnabled("KEYINA_RUN_LIVE_COMMAND_SPEECH_TEST"))
        {
            return;
        }

        using var target = new LiveInputTarget(string.Empty);
        target.FocusAndSelectAll();
        AssertFocusedTarget(target);

        using var companion = StartCompanion(CompanionCommand.ToggleDictation);
        Thread.Sleep(TimeSpan.FromSeconds(5));
        AssertEx.False(
            companion.HasExited,
            companion.HasExited
                ? $"Speech companion exited during startup: {companion.ExitCode}."
                : "Speech companion exited during startup.");

        using var cancel = StartCompanion(CompanionCommand.CancelActiveCommand);
        AssertEx.True(
            cancel.WaitForExit(5_000),
            "Speech cancel signal process did not exit.");
        AssertEx.Equal(0, cancel.ExitCode);
        AssertEx.True(
            companion.WaitForExit(15_000),
            "Speech companion did not exit after cancellation.");
        AssertEx.Equal(0, companion.ExitCode);
    }

    private static Process StartCompanion(CompanionCommand command)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Keyina.Host.exe");
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = CompanionCommandProtocol.ToArgument(command),
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Command companion process did not start.");
    }

    private static void AssertFocusedTarget(LiveInputTarget target)
    {
        var context = WindowsTypingContextProbe.Capture();
        AssertEx.Equal(
            Environment.ProcessId,
            context.ForegroundProcessId,
            "The live textbox process was not foreground.");
        AssertEx.Equal(
            target.TextBoxHandle,
            context.FocusWindow,
            "The live textbox control was not focused.");
        AssertEx.False(
            context.ShouldBypassTyping,
            "The live textbox was treated as secure input.");
    }

    private static nint WaitForWindow(
        Process process,
        string title,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (TryFindTopLevelWindow(process.Id, title, out var window))
            {
                return window;
            }
            if (process.HasExited)
            {
                return nint.Zero;
            }
            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);

        return nint.Zero;
    }

    private static bool TryFindTopLevelWindow(
        int processId,
        string title,
        out nint window)
    {
        var found = nint.Zero;
        _ = EnumWindows(
            (candidate, state) =>
            {
                _ = state;
                _ = GetWindowThreadProcessId(candidate, out var ownerProcessId);
                if (ownerProcessId != processId)
                {
                    return true;
                }

                var length = GetWindowTextLengthW(candidate);
                if (length <= 0)
                {
                    return true;
                }
                var text = new char[length + 1];
                var copied = GetWindowTextW(candidate, text, text.Length);
                var windowTitle = copied > 0
                    ? new string(text, 0, copied)
                    : string.Empty;
                if (!string.Equals(windowTitle, title, StringComparison.Ordinal))
                {
                    return true;
                }

                found = candidate;
                return false;
            },
            nint.Zero);
        window = found;
        return found != nint.Zero;
    }

    private static bool IsEnabled(string variable) =>
        string.Equals(
            Environment.GetEnvironmentVariable(variable),
            "1",
            StringComparison.Ordinal);

    private sealed class LiveInputTarget : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEventSlim ready = new(initialState: false);
        private Form? form;
        private TextBox? textBox;
        private Exception? startupFailure;

        public LiveInputTarget(string text)
        {
            thread = new Thread(() => Run(text))
            {
                IsBackground = true,
                Name = "Keyina live input target",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            AssertEx.True(
                ready.Wait(TimeSpan.FromSeconds(10)),
                "The live input target did not start.");
            if (startupFailure is not null)
            {
                throw new InvalidOperationException(
                    "The live input target failed to start.",
                    startupFailure);
            }
        }

        public nint TextBoxHandle => textBox?.Handle ?? nint.Zero;

        public void FocusAndSelectAll()
        {
            var currentForm = form ?? throw new InvalidOperationException(
                "The live input form is unavailable.");
            currentForm.Invoke(() =>
            {
                currentForm.Show();
                currentForm.Activate();
                _ = SetForegroundWindow(currentForm.Handle);
                textBox!.Focus();
                textBox.SelectAll();
            });

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            do
            {
                var context = WindowsTypingContextProbe.Capture();
                if (context.ForegroundProcessId == Environment.ProcessId &&
                    context.FocusWindow == TextBoxHandle)
                {
                    return;
                }
                Thread.Sleep(25);
            }
            while (DateTime.UtcNow < deadline);
        }

        public void Dispose()
        {
            var currentForm = form;
            if (currentForm is not null && !currentForm.IsDisposed)
            {
                try
                {
                    currentForm.Invoke(currentForm.Close);
                }
                catch (InvalidOperationException)
                {
                }
            }
            _ = thread.Join(TimeSpan.FromSeconds(5));
            ready.Dispose();
        }

        private void Run(string text)
        {
            try
            {
                using var localForm = new Form
                {
                    Text = "Keyina live command target",
                    Width = 640,
                    Height = 240,
                    StartPosition = FormStartPosition.CenterScreen,
                    TopMost = true,
                };
                using var localTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    Text = text,
                };
                localForm.Controls.Add(localTextBox);
                form = localForm;
                textBox = localTextBox;
                localForm.Shown += (_, _) =>
                {
                    localForm.Activate();
                    _ = SetForegroundWindow(localForm.Handle);
                    localTextBox.Focus();
                    localTextBox.SelectAll();
                    ready.Set();
                };
                Application.Run(localForm);
            }
            catch (Exception exception)
            {
                startupFailure = exception;
                ready.Set();
            }
        }
    }

    private delegate bool EnumWindowsCallback(nint window, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        nint state);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(
        nint window,
        [Out] char[] text,
        int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
