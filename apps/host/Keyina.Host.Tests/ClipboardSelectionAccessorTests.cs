using System.Runtime.InteropServices;
using Keyina.Host.Translation;

namespace Keyina.Host.Tests;

internal static class ClipboardSelectionAccessorTests
{
    [KeyinaTest("clipboard selection capture restores the original clipboard and focus identity")]
    private static void CaptureRestoresClipboard()
    {
        var originalClipboard = new object();
        var platform = new FakeClipboardPlatform
        {
            ForegroundWindow = (nint)42,
            FocusedWindow = (nint)420,
            ClipboardSequence = 10,
            ClipboardSnapshot = originalClipboard,
            SelectedText = "Xin chào",
        };
        var accessor = new ClipboardSelectionAccessor(platform);

        var capture = accessor.CaptureAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.NotNull(capture, "Selected text was not captured.");
        AssertEx.Equal("Xin chào", capture!.Text);
        AssertEx.Equal((nint)42, capture.ForegroundWindow);
        AssertEx.Equal((nint)420, capture.FocusedWindow);
        AssertEx.Equal(1, platform.CopyShortcutCount);
        AssertEx.Equal(1, platform.RestoreCount);
        AssertEx.True(
            ReferenceEquals(originalClipboard, platform.RestoredClipboard),
            "The original clipboard snapshot was not restored.");
    }

    [KeyinaTest("clipboard selection capture uses the foreground window when Chromium exposes no focused child")]
    private static void ChromiumFocusFallbackCapturesSelection()
    {
        var platform = new FakeClipboardPlatform
        {
            ForegroundWindow = (nint)42,
            FocusedWindow = nint.Zero,
            ClipboardSequence = 10,
            SelectedText = "Đoạn đã chọn",
        };
        var accessor = new ClipboardSelectionAccessor(platform);

        var capture = accessor.CaptureAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.NotNull(capture, "Chromium selection was rejected because no child HWND was reported.");
        AssertEx.Equal((nint)42, capture!.FocusedWindow);
        AssertEx.Equal("Đoạn đã chọn", capture.Text);
    }

    [KeyinaTest("clipboard selection capture returns no selection when copy produces no Unicode text")]
    private static void EmptyCopyReturnsNoSelection()
    {
        var platform = new FakeClipboardPlatform
        {
            ForegroundWindow = (nint)42,
            FocusedWindow = (nint)420,
            ClipboardSequence = 10,
            SelectedText = null,
        };
        var accessor = new ClipboardSelectionAccessor(platform);

        var capture = accessor.CaptureAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.True(capture is null, "Capture unexpectedly returned empty selected text.");
        AssertEx.Equal(1, platform.RestoreCount);
    }

    [KeyinaTest("clipboard selection capture restores clipboard after transient clipboard failure")]
    private static void ClipboardFailureStillRestores()
    {
        var platform = new FakeClipboardPlatform
        {
            ForegroundWindow = (nint)42,
            FocusedWindow = (nint)420,
            ClipboardSequence = 10,
            SelectedText = "text",
            ReadFailuresRemaining = 1,
        };
        var accessor = new ClipboardSelectionAccessor(platform);

        var capture = accessor.CaptureAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.NotNull(capture, "Transient clipboard read was not retried.");
        AssertEx.True(platform.DelayCount > 0, "Clipboard retry did not wait between attempts.");
        AssertEx.Equal(1, platform.RestoreCount);
    }

    [KeyinaTest("clipboard replacement is blocked after foreground window or focused control changes")]
    private static void FocusGuardPreventsReplacement()
    {
        var platform = new FakeClipboardPlatform
        {
            ForegroundWindow = (nint)42,
            FocusedWindow = (nint)420,
        };
        var accessor = new ClipboardSelectionAccessor(platform);
        var capture = new SelectedTextCapture("source", (nint)42, (nint)420);

        platform.ForegroundWindow = (nint)99;
        AssertEx.False(
            accessor.TryReplace(capture, "translated"),
            "Replacement succeeded after the foreground window changed.");
        AssertEx.Equal(0, platform.InsertCount);

        platform.ForegroundWindow = (nint)42;
        platform.FocusedWindow = (nint)421;
        AssertEx.False(
            accessor.TryReplace(capture, "translated"),
            "Replacement succeeded in a different focused control of the same window.");
        AssertEx.Equal(0, platform.InsertCount);

        platform.FocusedWindow = (nint)420;
        AssertEx.True(
            accessor.TryReplace(capture, "translated"),
            "Replacement failed while the original control still had focus.");
        AssertEx.Equal(1, platform.InsertCount);
        AssertEx.Equal("translated", platform.InsertedText);
    }

    [KeyinaTest("clipboard translation undo restores only an exact focused translated suffix")]
    private static void UndoRestoresExactTranslatedSuffix()
    {
        var originalClipboard = new object();
        var platform = new FakeClipboardPlatform
        {
            ForegroundWindow = (nint)42,
            FocusedWindow = (nint)420,
            ClipboardSequence = 10,
            ClipboardSnapshot = originalClipboard,
            SelectedText = "Hello",
        };
        var accessor = new ClipboardSelectionAccessor(platform);
        var capture = new SelectedTextCapture("Xin chào", (nint)42, (nint)420);

        var restored = accessor.TryRestoreAsync(
                capture,
                "Hello",
                "Xin chào",
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.True(restored, "Exact translated suffix was not restored.");
        AssertEx.Equal(5, platform.SelectedPreviousTextElements);
        AssertEx.Equal(1, platform.CopyShortcutCount);
        AssertEx.Equal(1, platform.InsertCount);
        AssertEx.Equal("Xin chào", platform.InsertedText);
        AssertEx.Equal(0, platform.CollapseSelectionCount);
        AssertEx.Equal(1, platform.RestoreCount);
        AssertEx.True(
            ReferenceEquals(originalClipboard, platform.RestoredClipboard),
            "Undo did not restore the original clipboard.");
    }

    [KeyinaTest("clipboard translation undo rejects mismatched text and collapses selection")]
    private static void UndoRejectsMismatchedTranslatedSuffix()
    {
        var platform = new FakeClipboardPlatform
        {
            ForegroundWindow = (nint)42,
            FocusedWindow = (nint)420,
            ClipboardSequence = 10,
            SelectedText = "Changed",
        };
        var accessor = new ClipboardSelectionAccessor(platform);

        var restored = accessor.TryRestoreAsync(
                new SelectedTextCapture("Xin chào", (nint)42, (nint)420),
                "Hello",
                "Xin chào",
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.False(restored, "Mismatched translated suffix was overwritten.");
        AssertEx.Equal(0, platform.InsertCount);
        AssertEx.Equal(1, platform.CollapseSelectionCount);
        AssertEx.Equal(1, platform.RestoreCount);
    }

    private sealed class ClipboardBusyException : ExternalException
    {
        public ClipboardBusyException()
            : base("clipboard busy")
        {
        }
    }

    private sealed class FakeClipboardPlatform : IClipboardSelectionPlatform
    {
        public nint ForegroundWindow { get; set; }

        public nint FocusedWindow { get; set; }

        public uint ClipboardSequence { get; set; }

        public object? ClipboardSnapshot { get; init; }

        public string? SelectedText { get; init; }

        public int ReadFailuresRemaining { get; set; }

        public int CopyShortcutCount { get; private set; }

        public int RestoreCount { get; private set; }

        public object? RestoredClipboard { get; private set; }

        public int DelayCount { get; private set; }

        public int InsertCount { get; private set; }

        public string? InsertedText { get; private set; }

        public int SelectedPreviousTextElements { get; private set; }

        public int CollapseSelectionCount { get; private set; }

        public bool RestoreFocusResult { get; set; }

        public nint GetForegroundWindow() => ForegroundWindow;

        public nint GetFocusedWindow() => FocusedWindow;

        public uint GetClipboardSequenceNumber() => ClipboardSequence;

        public object? CaptureClipboard() => ClipboardSnapshot;

        public string? ReadUnicodeText()
        {
            if (ReadFailuresRemaining > 0)
            {
                ReadFailuresRemaining--;
                throw new ClipboardBusyException();
            }
            return SelectedText;
        }

        public void RestoreClipboard(object? snapshot)
        {
            RestoreCount++;
            RestoredClipboard = snapshot;
        }

        public void SendCopyShortcut()
        {
            CopyShortcutCount++;
            ClipboardSequence++;
        }

        public void SelectPreviousText(int textElementCount) =>
            SelectedPreviousTextElements = textElementCount;

        public void CollapseSelectionToEnd() => CollapseSelectionCount++;

        public bool TryRestoreFocus(nint foregroundWindow, nint focusedWindow)
        {
            if (!RestoreFocusResult)
            {
                return false;
            }
            ForegroundWindow = foregroundWindow;
            FocusedWindow = focusedWindow;
            return true;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCount++;
            return Task.CompletedTask;
        }

        public void InsertUnicode(string text)
        {
            InsertCount++;
            InsertedText = text;
        }
    }
}
