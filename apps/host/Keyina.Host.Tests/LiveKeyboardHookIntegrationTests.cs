using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class LiveKeyboardHookIntegrationTests
{
    [KeyinaTest("native typing context refreshes password state on the same focused control")]
    private static void NativeTypingContextRefreshesPasswordState()
    {
        using var liveInputLease = AcquireLiveInputLease();
        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-1200, 100),
            Size = new Size(320, 120),
            ShowInTaskbar = false,
        };
        using var textBox = new TextBox { Dock = DockStyle.Fill };
        form.Controls.Add(textBox);
        form.Show();
        EnsureForeground(form, textBox);

        var contextProbe = new WindowsTypingContextProbe();

        const int globalWindowStyle = -16;
        const nint editStylePassword = 0x20;
        var focusWindow = textBox.Handle;
        var originalStyle = GetWindowLongPtrW(focusWindow, globalWindowStyle);
        var ordinary = contextProbe.Capture();
        try
        {
            _ = SetWindowLongPtrW(
                focusWindow,
                globalWindowStyle,
                originalStyle | editStylePassword);
            var password = contextProbe.Capture();

            AssertEx.Equal(focusWindow, ordinary.FocusWindow);
            AssertEx.False(ordinary.ShouldBypassTyping, "Ordinary text was classified as secure input.");
            AssertEx.Equal(ordinary.FocusWindow, password.FocusWindow);
            AssertEx.True(password.ShouldBypassTyping, "Password state was not refreshed for the same HWND.");
        }
        finally
        {
            _ = SetWindowLongPtrW(focusWindow, globalWindowStyle, originalStyle);
        }
    }

    [KeyinaTest("live Windows hook remains responsive while its owner UI thread is blocked")]
    private static void LiveHookUsesDedicatedMessageThread()
    {
        using var liveInputLease = AcquireLiveInputLease();
        using var form = new Form
        {
            Text = $"Keyina Hook Thread Test {Guid.NewGuid():N}",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-1200, 100),
            Size = new Size(480, 180),
            ShowInTaskbar = false,
        };
        using var textBox = new TextBox { Dock = DockStyle.Fill };
        form.Controls.Add(textBox);
        form.Show();
        EnsureForeground(form, textBox);

        using var hook = new VietnameseKeyboardHook();
        hook.Start(enabledInitially: true);
        EnsureForeground(form, textBox);

        Exception? workerFailure = null;
        var elapsed = TimeSpan.MaxValue;
        using var start = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var processedBefore = hook.ProcessedPhysicalEventCount;
        var worker = new Thread(() =>
        {
            try
            {
                start.Wait();
                var timer = Stopwatch.StartNew();
                keybd_event(0x42, 0, 0, 0);
                keybd_event(0x42, 0, 0x0002, 0);
                WaitForProcessedEvents(
                    hook,
                    processedBefore + 2,
                    "blocked-owner probe key");
                timer.Stop();
                elapsed = timer.Elapsed;
            }
            catch (Exception exception)
            {
                workerFailure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Keyina blocked-owner hook probe",
        };

        worker.Start();
        start.Set();
        Thread.Sleep(300);
        AssertEx.True(
            completed.Wait(TimeSpan.FromSeconds(2)),
            "The dedicated typing hook did not process input while the UI thread was blocked.");
        worker.Join();
        if (workerFailure is not null)
        {
            throw new InvalidOperationException(
                "The blocked-owner hook probe failed.",
                workerFailure);
        }
        AssertEx.True(
            elapsed < TimeSpan.FromMilliseconds(250),
            $"Typing hook callback waited {elapsed.TotalMilliseconds:F1} ms for the owner UI thread.");

        Application.DoEvents();
        form.Close();
    }

    [KeyinaTest("live Windows hook types Vietnamese into a focused textbox without TSF")]
    private static void LiveHookTypesIntoFocusedTextbox()
    {
        using var liveInputLease = AcquireLiveInputLease();
        using var typingTrace = new TypingTraceLease();
        using var form = new Form
        {
            Text = $"Keyina Hook Test {Guid.NewGuid():N}",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-1200, 100),
            Size = new Size(480, 180),
            ShowInTaskbar = false,
        };
        using var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
        };
        form.Controls.Add(textBox);
        form.Show();
        EnsureForeground(form, textBox);

        using var hook = new VietnameseKeyboardHook();
        hook.Start(enabledInitially: true);
        EnsureForeground(form, textBox);

        TypeWithInterferenceRetry(
            form,
            textBox,
            hook,
            baselineText: string.Empty,
            rawText: "tieengs vieetj",
            expectedText: "tiếng việt",
            operationName: "basic Vietnamese typing");

        TypeWithInterferenceRetry(
            form,
            textBox,
            hook,
            baselineText: string.Empty,
            rawText: "as",
            expectedText: "á",
            operationName: "selection replacement typing");

        EnsureForeground(form, textBox);
        textBox.Clear();
        hook.Reset();
        var burstCases = new[]
        {
            (Raw: "truocws dduocwj nuawx tieengs vieetj vaanx vowis mootj motoj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truowcs dduowcj nuwxa tieesng vieejt vaanx voisw motoj mootj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truwocs dduwocj nuawx tieengs vieejt vaanx vowis mootj motoj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truowsc dduowjc nuwxa tieesng vieetj vaanx voisw motoj mootj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truocsw dduocwj nuawx tieengs vieejt vaanx vowis mootj motoj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
        };
        var expected = new StringBuilder();
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var baselineText = expected.ToString();
            var testCase = burstCases[iteration % burstCases.Length];
            var expectedText = baselineText + testCase.Expected;
            TypeWithInterferenceRetry(
                form,
                textBox,
                hook,
                baselineText,
                testCase.Raw,
                expectedText,
                $"stress iteration {iteration + 1}");
            expected.Append(testCase.Expected);
        }

        var pasteElapsed = PasteWithRetry(
            form,
            textBox,
            hook,
            baselineText: "nội dung cũ cần thay",
            clipboardText: "đoạn văn được dán nguyên vẹn");
        AssertEx.True(
            pasteElapsed < TimeSpan.FromSeconds(1),
            $"Ctrl+V took {pasteElapsed.TotalMilliseconds:F0} ms.");

        hook.Dispose();
        form.Close();
        Application.DoEvents();
    }

    private static TimeSpan PasteWithRetry(
        Form form,
        TextBox textBox,
        VietnameseKeyboardHook hook,
        string baselineText,
        string clipboardText)
    {
        const int maximumAttempts = 3;
        SetClipboardText(clipboardText);
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            EnsureForeground(form, textBox);
            textBox.Text = baselineText;
            textBox.SelectAll();
            hook.Reset();
            Application.DoEvents();
            EnsureForeground(form, textBox);

            var timer = Stopwatch.StartNew();
            SendControlV();
            PumpUntil(
                () => string.Equals(
                    textBox.Text,
                    clipboardText,
                    StringComparison.Ordinal),
                TimeSpan.FromSeconds(2));
            timer.Stop();
            if (string.Equals(textBox.Text, clipboardText, StringComparison.Ordinal))
            {
                return timer.Elapsed;
            }
        }

        throw new InvalidOperationException(
            $"Ctrl+V did not replace the selected text after {maximumAttempts} attempts. " +
            $"{CreateStressMismatchMessage(maximumAttempts, clipboardText, textBox.Text)}");
    }

    private static void TypeWithInterferenceRetry(
        Form form,
        TextBox textBox,
        VietnameseKeyboardHook hook,
        string baselineText,
        string rawText,
        string expectedText,
        string operationName)
    {
        const int maximumAttempts = 3;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            EnsureForeground(form, textBox);
            textBox.Text = baselineText;
            textBox.SelectionStart = textBox.TextLength;
            textBox.SelectionLength = 0;
            hook.Reset();
            TypingTraceBuffer.Clear();
            TypingTraceBuffer.SetEnabled(true);

            SendAscii(rawText, hook);
            PumpUntil(
                () => string.Equals(textBox.Text, expectedText, StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            if (string.Equals(textBox.Text, expectedText, StringComparison.Ordinal))
            {
                return;
            }

            if (!HasExternalInputInterference(form, textBox))
            {
                AssertEx.Equal(
                    expectedText,
                    textBox.Text,
                    CreateStressMismatchMessage(
                        attempt,
                        expectedText,
                        textBox.Text));
            }
        }

        throw new InvalidOperationException(
            $"The live hook could not complete {operationName} after " +
            $"{maximumAttempts} attempts because the desktop focus or pointer state changed.");
    }

    private static bool HasExternalInputInterference(Form form, TextBox textBox)
    {
        if (GetForegroundWindow() != form.Handle || !textBox.Focused)
        {
            return true;
        }

        var entries = TypingTraceBuffer.Snapshot();
        var pointerResets = entries.Count(entry =>
            string.Equals(entry.Action, "pointer-reset", StringComparison.Ordinal));
        return pointerResets > 1 || entries.Any(entry =>
            string.Equals(entry.Action, "focus-reset", StringComparison.Ordinal) ||
            string.Equals(entry.Action, "secure-bypass", StringComparison.Ordinal));
    }

    private static MutexLease AcquireLiveInputLease()
    {
        var mutex = new Mutex(
            initiallyOwned: false,
            name: @"Local\Keyina.Host.Tests.LiveKeyboardInput");
        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                {
                    throw new InvalidOperationException(
                        "Timed out waiting for exclusive access to live Windows input.");
                }
            }
            catch (AbandonedMutexException)
            {
                // Windows grants ownership when the previous test process ended
                // without releasing the desktop-input lease.
            }
            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private static string CreateStressMismatchMessage(
        int iteration,
        string expected,
        string actual)
    {
        var mismatch = 0;
        var commonLength = Math.Min(expected.Length, actual.Length);
        while (mismatch < commonLength && expected[mismatch] == actual[mismatch])
        {
            mismatch++;
        }

        const int contextLength = 48;
        var contextStart = Math.Max(0, mismatch - contextLength);
        var expectedTail = expected[contextStart..];
        var actualTail = actual.Length > contextStart
            ? actual[contextStart..]
            : string.Empty;
        var recentActions = string.Join(
            ",",
            TypingTraceBuffer.Snapshot(32).Select(entry => entry.Action));
        return $"Live hook diverged at stress iteration {iteration}, index {mismatch}. " +
            $"Expected tail '{expectedTail}', actual tail '{actualTail}'. " +
            $"Foreground=0x{GetForegroundWindow():X}, recent actions=[{recentActions}].";
    }

    private static void EnsureForeground(Form form, TextBox textBox)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var currentThread = GetCurrentThreadId();
            var foregroundThread = GetWindowThreadProcessId(
                GetForegroundWindow(),
                out _);
            var attached = foregroundThread != 0 &&
                foregroundThread != currentThread &&
                AttachThreadInput(currentThread, foregroundThread, attach: true);
            try
            {
                form.TopMost = true;
                _ = ShowWindow(form.Handle, showCommand: 9);
                _ = BringWindowToTop(form.Handle);
                form.Activate();
                _ = SetForegroundWindow(form.Handle);
                _ = SetActiveWindow(form.Handle);
                _ = SetFocus(textBox.Handle);
                form.TopMost = false;
                Application.DoEvents();
            }
            finally
            {
                if (attached)
                {
                    _ = AttachThreadInput(currentThread, foregroundThread, attach: false);
                }
            }

            if (GetForegroundWindow() == form.Handle && textBox.Focused)
            {
                return;
            }
            Thread.Sleep(20);
        }

        throw new InvalidOperationException(
            "The live hook test window could not acquire foreground keyboard focus.");
    }

    private static void SetClipboardText(string value)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(value);
                return;
            }
            catch (ExternalException) when (attempt < 4)
            {
                Thread.Sleep(20);
            }
        }
        throw new InvalidOperationException("Could not acquire the Windows clipboard.");
    }

    private static void SendControlV()
    {
        const ushort control = 0x11;
        const ushort v = 0x56;
        Input[] inputs =
        [
            Input.Key(control, keyUp: false),
            Input.Key(v, keyUp: false),
            Input.Key(v, keyUp: true),
            Input.Key(control, keyUp: true),
        ];

        Exception? workerFailure = null;
        using var completed = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            try
            {
                var sent = SendInput(
                    checked((uint)inputs.Length),
                    inputs,
                    Marshal.SizeOf<Input>());
                if (sent != inputs.Length)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows did not send the complete Ctrl+V shortcut.");
                }
            }
            catch (Exception exception)
            {
                workerFailure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Keyina live-hook paste source",
        };

        worker.Start();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!completed.IsSet && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }

        AssertEx.True(completed.IsSet, "The Ctrl+V input worker timed out.");
        worker.Join();
        if (workerFailure is not null)
        {
            throw new InvalidOperationException(
                "The Ctrl+V input worker failed.",
                workerFailure);
        }
        Application.DoEvents();
    }

    private static void SendAscii(string text, VietnameseKeyboardHook hook)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(hook);

        Exception? workerFailure = null;
        using var completed = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            try
            {
                foreach (var character in text)
                {
                    var virtualKey = character == ' '
                        ? checked((ushort)0x20)
                        : checked((ushort)char.ToUpperInvariant(character));
                    SendVirtualKeyFromWorker(virtualKey, hook);
                }
            }
            catch (Exception exception)
            {
                workerFailure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Keyina live-hook input source",
        };

        worker.Start();
        var timeout = TimeSpan.FromSeconds(Math.Max(5, text.Length * 0.05));
        var deadline = DateTime.UtcNow + timeout;
        while (!completed.IsSet && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }

        AssertEx.True(completed.IsSet, "The live-hook input worker timed out.");
        worker.Join();
        if (workerFailure is not null)
        {
            throw new InvalidOperationException(
                "The live-hook input worker failed.",
                workerFailure);
        }
        Application.DoEvents();
    }

    private static void SendVirtualKeyFromWorker(
        ushort virtualKey,
        VietnameseKeyboardHook hook)
    {
        const uint keyEventKeyUp = 0x0002;
        var processedBefore = hook.ProcessedPhysicalEventCount;

        keybd_event(checked((byte)virtualKey), 0, 0, 0);
        WaitForProcessedEvents(
            hook,
            processedBefore + 1,
            $"key-down 0x{virtualKey:X2}");

        keybd_event(checked((byte)virtualKey), 0, keyEventKeyUp, 0);
        WaitForProcessedEvents(
            hook,
            processedBefore + 2,
            $"key-up 0x{virtualKey:X2}");

        // The hook callback can enqueue Unicode replacement input after the
        // physical event. Give the target UI thread a brief chance to apply
        // that edit before the next synthetic physical key arrives.
        Thread.Sleep(3);
    }

    private static void WaitForProcessedEvents(
        VietnameseKeyboardHook hook,
        long expectedCount,
        string eventDescription)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (hook.ProcessedPhysicalEventCount < expectedCount &&
               DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
        }

        if (hook.ProcessedPhysicalEventCount < expectedCount)
        {
            throw new InvalidOperationException(
                $"The live hook did not process {eventDescription} in time. " +
                $"Expected at least {expectedCount} events, received " +
                $"{hook.ProcessedPhysicalEventCount}.");
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        private const uint InputKeyboard = 1;
        private const uint KeyEventKeyUp = 0x0002;

        public uint Type;
        public InputUnion Union;

        public static Input Key(ushort virtualKey, bool keyUp) => new()
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                },
            },
        };
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private sealed class TypingTraceLease : IDisposable
    {
        public TypingTraceLease()
        {
            TypingTraceBuffer.Clear();
            TypingTraceBuffer.SetEnabled(true);
        }

        public void Dispose() => TypingTraceBuffer.Clear();
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        private Mutex? ownedMutex = mutex;

        public void Dispose()
        {
            var mutex = Interlocked.Exchange(ref ownedMutex, null);
            if (mutex is null)
            {
                return;
            }

            try
            {
                mutex.ReleaseMutex();
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int showCommand);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(
        nint window,
        int index,
        nint newValue);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint attachThread,
        uint attachToThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        nuint extraInfo);
}
