using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

[KeyinaInteractiveTest]
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

        const int globalWindowStyle = -16;
        const nint editStylePassword = 0x20;
        var focusWindow = textBox.Handle;
        var originalStyle = GetWindowLongPtrW(focusWindow, globalWindowStyle);
        var ordinary = WindowsTypingContextProbe.Capture();
        try
        {
            _ = SetWindowLongPtrW(
                focusWindow,
                globalWindowStyle,
                originalStyle | editStylePassword);
            var password = WindowsTypingContextProbe.Capture();

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
                SendPhysicalKeyEvent(0x42, keyUp: false);
                SendPhysicalKeyEvent(0x42, keyUp: true);
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
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
        };
        using var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
        };
        form.Controls.Add(textBox);
        var allowFormClose = false;
        FormClosingEventHandler closeGuard = (_, eventArgs) =>
        {
            if (!allowFormClose)
            {
                eventArgs.Cancel = true;
            }
        };
        form.FormClosing += closeGuard;
        form.Show();
        EnsureForeground(form, textBox);

        using var hook = new VietnameseKeyboardHook();
        try
        {
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

            TypeWithInterferenceRetry(
                form,
                textBox,
                hook,
                baselineText: string.Empty,
                rawText: "register",
                expectedText: "register",
                operationName: "embedded tone key in a Latin token");

            TypeWithInterferenceRetry(
                form,
                textBox,
                hook,
                baselineText: string.Empty,
                rawText: "process",
                expectedText: "process",
                operationName: "double s in a Latin token");

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
            for (var caseIndex = 0; caseIndex < burstCases.Length; caseIndex++)
            {
                var testCase = burstCases[caseIndex];
                var rawWords = testCase.Raw.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);
                var expectedWords = testCase.Expected.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);
                AssertEx.Equal(rawWords.Length, expectedWords.Length);

                for (var wordIndex = 0; wordIndex < rawWords.Length; wordIndex++)
                {
                    var baselineText = expected.ToString();
                    var rawChunk = rawWords[wordIndex] + " ";
                    var expectedChunk = expectedWords[wordIndex] + " ";
                    var expectedText = baselineText + expectedChunk;
                    TypeWithInterferenceRetry(
                        form,
                        textBox,
                        hook,
                        baselineText,
                        rawChunk,
                        expectedText,
                        $"live Telex case {caseIndex + 1}, word {wordIndex + 1}");
                    expected.Append(expectedChunk);
                }
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
        }
        finally
        {
            hook.Dispose();
            allowFormClose = true;
            form.FormClosing -= closeGuard;
            form.Close();
            Application.DoEvents();
        }
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

            try
            {
                SendAscii(rawText, hook, form, textBox);
            }
            catch (InvalidOperationException exception) when (
                exception.InnerException is LiveInputDeliveryException &&
                attempt < maximumAttempts)
            {
                DrainLiveInput(hook);
                continue;
            }
            PumpUntil(
                () => string.Equals(textBox.Text, expectedText, StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            if (string.Equals(textBox.Text, expectedText, StringComparison.Ordinal))
            {
                return;
            }

            if (attempt == maximumAttempts)
            {
                AssertEx.Equal(
                    expectedText,
                    textBox.Text,
                    CreateStressMismatchMessage(
                        attempt,
                        expectedText,
                        textBox.Text));
            }

            DrainLiveInput(hook);
        }

        throw new InvalidOperationException(
            $"The live hook could not complete {operationName} after " +
            $"{maximumAttempts} attempts because the desktop focus or pointer state changed.");
    }

    private static void DrainLiveInput(VietnameseKeyboardHook hook)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        var stableSince = DateTime.UtcNow;
        var lastCount = hook.ProcessedPhysicalEventCount;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
            var currentCount = hook.ProcessedPhysicalEventCount;
            if (currentCount != lastCount)
            {
                lastCount = currentCount;
                stableSince = DateTime.UtcNow;
                continue;
            }
            if (DateTime.UtcNow - stableSince >= TimeSpan.FromMilliseconds(150))
            {
                break;
            }
        }
        hook.Reset();
        Application.DoEvents();
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

    private static void SendAscii(
        string text,
        VietnameseKeyboardHook hook,
        Form form,
        TextBox textBox)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(hook);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(textBox);

        var expectedSnapshots = BuildExpectedTextSnapshots(textBox.Text, text);
        var targetWindow = form.Handle;
        Exception? workerFailure = null;
        Exception? deliveryFailure = null;
        var attemptedCharacters = 0;
        var dispatchedIndex = -1;
        using var completed = new ManualResetEventSlim();
        using var keyDispatched = new AutoResetEvent(initialState: false);
        using var uiAcknowledged = new AutoResetEvent(initialState: false);
        using var inputEvents = new LiveInputEventTracker(hook);
        var worker = new Thread(() =>
        {
            try
            {
                for (var index = 0; index < text.Length; index++)
                {
                    Volatile.Write(ref attemptedCharacters, index + 1);
                    var character = text[index];
                    var virtualKey = character == ' '
                        ? checked((ushort)0x20)
                        : checked((ushort)char.ToUpperInvariant(character));
                    WaitForLiveInputPreconditions(
                        targetWindow,
                        virtualKey,
                        index);
                    SendVirtualKeyFromWorker(virtualKey, inputEvents);
                    Volatile.Write(ref dispatchedIndex, index);
                    keyDispatched.Set();
                    if (!uiAcknowledged.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        throw new LiveInputDeliveryException(
                            $"The UI thread did not acknowledge key 0x{virtualKey:X2} at index {index}.");
                    }
                    var failure = Volatile.Read(ref deliveryFailure);
                    if (failure is not null)
                    {
                        throw failure;
                    }
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
        var timeout = TimeSpan.FromSeconds(Math.Max(5, text.Length * 0.1));
        var deadline = DateTime.UtcNow + timeout;
        while (!completed.IsSet && DateTime.UtcNow < deadline)
        {
            if (!keyDispatched.WaitOne(millisecondsTimeout: 0))
            {
                Application.DoEvents();
                Thread.Sleep(1);
                continue;
            }

            var index = Volatile.Read(ref dispatchedIndex);
            var deliveryDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (index >= 0 &&
                   !form.IsDisposed &&
                   !textBox.IsDisposed &&
                   !string.Equals(
                       textBox.Text,
                       expectedSnapshots[index],
                       StringComparison.Ordinal) &&
                   GetForegroundWindow() == targetWindow &&
                   textBox.Focused &&
                   DateTime.UtcNow < deliveryDeadline)
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }

            if (index < 0)
            {
                Volatile.Write(
                    ref deliveryFailure,
                    new LiveInputDeliveryException(
                        "The live input source signaled without a dispatched index."));
            }
            else if (form.IsDisposed || textBox.IsDisposed)
            {
                Volatile.Write(
                    ref deliveryFailure,
                    new LiveInputDeliveryException(
                        $"The live target was disposed after index {index}."));
            }
            else if (GetForegroundWindow() != targetWindow || !textBox.Focused)
            {
                var foreground = GetForegroundWindow();
                Volatile.Write(
                    ref deliveryFailure,
                    new LiveInputDeliveryException(
                        $"The live target lost focus after index {index}; " +
                        $"target=0x{targetWindow:X};foreground={DescribeWindow(foreground)};" +
                        $"modifiers={DescribePhysicalModifiers()}."));
            }
            else if (!string.Equals(
                         textBox.Text,
                         expectedSnapshots[index],
                         StringComparison.Ordinal))
            {
                var recentActions = string.Join(
                    ",",
                    TypingTraceBuffer.Snapshot(24).Select(entry => entry.Action));
                Volatile.Write(
                    ref deliveryFailure,
                    new LiveInputDeliveryException(
                        $"The target did not apply index {index}. " +
                        $"Expected '{expectedSnapshots[index]}', actual '{textBox.Text}', " +
                        $"recent actions=[{recentActions}]."));
            }
            uiAcknowledged.Set();
        }

        AssertEx.True(
            completed.IsSet,
            $"The live-hook input worker timed out after attempting " +
            $"{Volatile.Read(ref attemptedCharacters)}/{text.Length} characters; " +
            $"processedEvents={hook.ProcessedPhysicalEventCount}.");
        worker.Join();
        if (workerFailure is not null)
        {
            throw new InvalidOperationException(
                "The live-hook input worker failed.",
                workerFailure);
        }
        Application.DoEvents();
    }

    private static string[] BuildExpectedTextSnapshots(
        string baselineText,
        string rawText)
    {
        using var engine = new NativeEngineClient();
        engine.Configure(restoreInvalidWord: true);
        var snapshots = new string[rawText.Length];
        var current = baselineText;
        for (var index = 0; index < rawText.Length; index++)
        {
            var character = rawText[index];
            var edit = character == ' '
                ? engine.Process(NativeEngineKeyKind.CommitBoundary, new Rune(' '))
                : engine.Process(NativeEngineKeyKind.Character, new Rune(character));
            current = edit.ConsumePhysicalKey
                ? ApplyExpectedEdit(current, edit)
                : current + character;
            snapshots[index] = current;
        }
        return snapshots;
    }

    private static string ApplyExpectedEdit(string text, HookEdit edit)
    {
        var end = text.Length;
        for (var index = 0; index < edit.BackspaceCount; index++)
        {
            if (end == 0)
            {
                throw new InvalidOperationException(
                    "The expected live-input model erased beyond its owned text.");
            }
            end--;
            if (char.IsLowSurrogate(text[end]) &&
                end > 0 &&
                char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }
        }
        return string.Concat(text.AsSpan(0, end), edit.InsertText);
    }

    private static void SendVirtualKeyFromWorker(
        ushort virtualKey,
        LiveInputEventTracker inputEvents)
    {
        SendPhysicalKeyEvent(
            virtualKey,
            keyUp: false,
            LiveInputEventTracker.Marker);
        try
        {
            inputEvents.WaitFor(
                virtualKey,
                isKeyDown: true,
                TimeSpan.FromSeconds(2));
        }
        finally
        {
            SendPhysicalKeyEvent(
                virtualKey,
                keyUp: true,
                LiveInputEventTracker.Marker);
        }
        inputEvents.WaitFor(
            virtualKey,
            isKeyDown: false,
            TimeSpan.FromSeconds(2));
    }

    private static void WaitForLiveInputPreconditions(
        nint targetWindow,
        ushort virtualKey,
        int index)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (GetForegroundWindow() == targetWindow &&
                !IsPhysicalModifierPressed())
            {
                return;
            }
            Thread.Sleep(2);
        }

        throw new LiveInputDeliveryException(
            $"The desktop was not ready for key 0x{virtualKey:X2} at index {index}. " +
            $"foreground=0x{GetForegroundWindow():X};modifiersDown={IsPhysicalModifierPressed()}.");
    }

    private static bool IsPhysicalModifierPressed() =>
        IsPhysicalKeyPressed(0x10) ||
        IsPhysicalKeyPressed(0x11) ||
        IsPhysicalKeyPressed(0x12) ||
        IsPhysicalKeyPressed(0x5B) ||
        IsPhysicalKeyPressed(0x5C);

    private static string DescribePhysicalModifiers() =>
        $"shift={IsPhysicalKeyPressed(0x10)}," +
        $"control={IsPhysicalKeyPressed(0x11)}," +
        $"alt={IsPhysicalKeyPressed(0x12)}," +
        $"leftWin={IsPhysicalKeyPressed(0x5B)}," +
        $"rightWin={IsPhysicalKeyPressed(0x5C)}";

    private static string DescribeWindow(nint window)
    {
        _ = GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return $"0x{window:X}/{process.ProcessName}/{processId}";
        }
        catch (Exception)
        {
            return $"0x{window:X}/unknown/{processId}";
        }
    }

    private static bool IsPhysicalKeyPressed(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;

    private static void SendPhysicalKeyEvent(
        ushort virtualKey,
        bool keyUp,
        nuint extraInfo = 0)
    {
        Input[] inputs = [Input.Key(virtualKey, keyUp, extraInfo)];
        var sent = SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Windows did not send key {(keyUp ? "up" : "down")} 0x{virtualKey:X2}.");
        }
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
            throw new LiveInputDeliveryException(
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

    private sealed class LiveInputDeliveryException(string message)
        : InvalidOperationException(message);

    private sealed class LiveInputEventTracker : IDisposable
    {
        public static readonly nuint Marker =
            unchecked((nuint)0x4B455954455354UL);

        private readonly ConcurrentQueue<VietnameseKeyboardEvent> events = new();
        private readonly AutoResetEvent eventAvailable = new(initialState: false);
        private readonly IDisposable subscription;
        private bool disposed;

        public LiveInputEventTracker(VietnameseKeyboardHook hook)
        {
            subscription = hook.SubscribePhysicalEvents(Record);
        }

        public void WaitFor(
            ushort virtualKey,
            bool isKeyDown,
            TimeSpan timeout)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                while (events.TryDequeue(out var keyboardEvent))
                {
                    if (keyboardEvent.VirtualKey == virtualKey &&
                        keyboardEvent.IsKeyDown == isKeyDown)
                    {
                        return;
                    }
                    throw new LiveInputDeliveryException(
                        $"The live hook observed an unexpected marked event. " +
                        $"Expected key 0x{virtualKey:X2} " +
                        $"{(isKeyDown ? "down" : "up")}, received " +
                        $"0x{keyboardEvent.VirtualKey:X2} " +
                        $"{(keyboardEvent.IsKeyDown ? "down" : "up")}.");
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }
                _ = eventAvailable.WaitOne(remaining);
            }

            throw new LiveInputDeliveryException(
                $"The live hook did not observe marked key 0x{virtualKey:X2} " +
                $"{(isKeyDown ? "down" : "up")} within {timeout.TotalSeconds:F1} seconds.");
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            subscription.Dispose();
            eventAvailable.Dispose();
        }

        private void Record(VietnameseKeyboardEvent keyboardEvent)
        {
            if (keyboardEvent.ExtraInfo != Marker)
            {
                return;
            }
            events.Enqueue(keyboardEvent);
            eventAvailable.Set();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        private const uint InputKeyboard = 1;
        private const uint KeyEventKeyUp = 0x0002;

        public uint Type;
        public InputUnion Union;

        public static Input Key(
            ushort virtualKey,
            bool keyUp,
            nuint extraInfo = 0) => new()
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                    ExtraInfo = extraInfo,
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
    private static extern short GetAsyncKeyState(int virtualKey);

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
}
