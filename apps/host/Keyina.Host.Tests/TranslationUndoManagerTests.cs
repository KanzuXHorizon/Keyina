using Keyina.Host.Translation;

namespace Keyina.Host.Tests;

internal static class TranslationUndoManagerTests
{
    [KeyinaTest("translation undo restores the original text once within its lifetime")]
    private static void UndoRestoresOnce()
    {
        var now = new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);
        var accessor = new FakeSelectionAccessor { RestoreResult = true };
        var manager = new TranslationUndoManager(
            accessor,
            () => now,
            TimeSpan.FromSeconds(30));
        var capture = new SelectedTextCapture("Xin chào", (nint)42, (nint)420);
        manager.Record(capture, "Xin chào", "Hello");

        var first = manager.UndoAsync(CancellationToken.None)
            .GetAwaiter().GetResult();
        var second = manager.UndoAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.True(first, "Valid translation undo did not restore text.");
        AssertEx.False(second, "Translation undo was reusable after one attempt.");
        AssertEx.Equal(1, accessor.RestoreCount);
        AssertEx.Equal("Hello", accessor.ExpectedTranslatedText);
        AssertEx.Equal("Xin chào", accessor.OriginalText);
        AssertEx.False(manager.CanUndo, "One-shot undo remained available.");
    }

    [KeyinaTest("translation undo expires without touching the selected application")]
    private static void UndoExpiresSafely()
    {
        var now = new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);
        var accessor = new FakeSelectionAccessor { RestoreResult = true };
        var manager = new TranslationUndoManager(
            accessor,
            () => now,
            TimeSpan.FromSeconds(5));
        manager.Record(
            new SelectedTextCapture("a", (nint)1, (nint)2),
            "a",
            "b");
        now = now.AddSeconds(6);

        var restored = manager.UndoAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.False(restored, "Expired undo unexpectedly restored text.");
        AssertEx.Equal(0, accessor.RestoreCount);
        AssertEx.False(manager.CanUndo, "Expired undo remained available.");
    }

    [KeyinaTest("new translation replaces the previous undo entry without exposing content")]
    private static void NewRecordReplacesPreviousEntry()
    {
        var accessor = new FakeSelectionAccessor { RestoreResult = true };
        var manager = new TranslationUndoManager(accessor);
        manager.Record(
            new SelectedTextCapture("private first", (nint)1, (nint)2),
            "private first",
            "first translated");
        manager.Record(
            new SelectedTextCapture("private second", (nint)3, (nint)4),
            "private second",
            "second translated");

        _ = manager.UndoAsync(CancellationToken.None).GetAwaiter().GetResult();

        AssertEx.Equal("second translated", accessor.ExpectedTranslatedText);
        AssertEx.Equal("private second", accessor.OriginalText);
        AssertEx.False(
            manager.ToString()!.Contains("private", StringComparison.Ordinal),
            "Undo manager string representation leaked content.");
    }

    private sealed class FakeSelectionAccessor : ISelectedTextAccessor
    {
        public bool RestoreResult { get; init; }

        public int RestoreCount { get; private set; }

        public string? ExpectedTranslatedText { get; private set; }

        public string? OriginalText { get; private set; }

        public Task<SelectedTextCapture?> CaptureAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SelectedTextCapture?>(null);

        public bool TryReplace(SelectedTextCapture selectedText, string translatedText) => false;

        public Task<bool> TryRestoreAsync(
            SelectedTextCapture selectedText,
            string expectedTranslatedText,
            string originalText,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            ExpectedTranslatedText = expectedTranslatedText;
            OriginalText = originalText;
            return Task.FromResult(RestoreResult);
        }
    }
}
