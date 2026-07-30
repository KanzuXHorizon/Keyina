using Keyina.Host.Core.Translation;
using Keyina.Host.Translation;

namespace Keyina.Host.Tests;

internal static class TranslationCoordinatorTests
{
    [KeyinaTest("translation coordinator skips provider when no text is selected")]
    private static void NoSelectionSkipsProvider()
    {
        var accessor = new FakeSelectionAccessor(null);
        var provider = new FakeProvider(new TranslationResult("Hello", "VI", "Fake"));
        using var coordinator = new TranslationCoordinator(accessor, provider);

        var outcome = coordinator.TranslateSelectionAsync(
                "key",
                "EN-US",
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(TranslationOutcomeStatus.Failed, outcome.Status);
        AssertEx.Equal(TranslationFailureCode.NoSelection, outcome.FailureCode);
        AssertEx.Equal(0, provider.CallCount);
        AssertEx.Equal(0, accessor.ReplaceCount);
    }

    [KeyinaTest("translation coordinator replaces the captured selection once on success")]
    private static void SuccessfulTranslationReplacesOnce()
    {
        var capture = new SelectedTextCapture(
            "Xin chào",
            (nint)42,
            (nint)420);
        var accessor = new FakeSelectionAccessor(capture);
        var provider = new FakeProvider(new TranslationResult("Hello", "VI", "Fake"));
        using var coordinator = new TranslationCoordinator(accessor, provider);

        var outcome = coordinator.TranslateSelectionAsync(
                "key",
                "EN-US",
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(TranslationOutcomeStatus.Succeeded, outcome.Status);
        AssertEx.Equal(1, provider.CallCount);
        AssertEx.Equal("Xin chào", provider.LastRequest!.Text);
        AssertEx.Equal("EN-US", provider.LastRequest.TargetLanguage);
        AssertEx.Equal(1, accessor.ReplaceCount);
        AssertEx.Equal("Hello", accessor.LastReplacement);
        AssertEx.True(coordinator.CanUndo, "Successful translation did not create undo state.");
        var undone = coordinator.UndoAsync(CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertEx.True(undone, "Coordinator could not restore the original translation.");
        AssertEx.Equal(1, accessor.RestoreCount);
        AssertEx.Equal("Hello", accessor.ExpectedTranslatedText);
        AssertEx.Equal("Xin chào", accessor.OriginalText);
        AssertEx.False(coordinator.CanUndo, "Undo state remained after one use.");
    }

    [KeyinaTest("translation coordinator returns preview without replacing until explicitly applied")]
    private static void PreviewDefersReplacementUntilApplied()
    {
        var now = new DateTimeOffset(2026, 7, 30, 11, 0, 0, TimeSpan.Zero);
        var accessor = new FakeSelectionAccessor(
            new SelectedTextCapture("Xin chào", (nint)42, (nint)420));
        var provider = new FakeProvider(new TranslationResult("Hello", "VI", "Fake"));
        using var coordinator = new TranslationCoordinator(
            accessor,
            provider,
            clock: () => now,
            previewLifetime: TimeSpan.FromMinutes(2));

        var outcome = coordinator.TranslateSelectionAsync(
                "key",
                "EN-US",
                CancellationToken.None,
                preview: true)
            .GetAwaiter().GetResult();

        AssertEx.Equal(TranslationOutcomeStatus.PreviewReady, outcome.Status);
        AssertEx.NotNull(outcome.Preview, "Preview result was missing.");
        AssertEx.Equal(0, accessor.ReplaceCount);
        AssertEx.Equal("Xin chào", outcome.Preview!.OriginalText);
        AssertEx.Equal("Hello", outcome.Preview.TranslatedText);
        AssertEx.Equal(now.AddMinutes(2), outcome.Preview.ExpiresAt);
        AssertEx.False(coordinator.CanUndo, "Preview created undo before replacement.");

        var applied = coordinator.ApplyPreview(outcome.Preview);

        AssertEx.Equal(TranslationOutcomeStatus.Succeeded, applied.Status);
        AssertEx.Equal(1, accessor.PreviewReplaceCount);
        AssertEx.Equal("Hello", accessor.LastReplacement);
        AssertEx.True(coordinator.CanUndo, "Applied preview did not create undo state.");
    }

    [KeyinaTest("translation coordinator rejects expired preview without replacing text")]
    private static void ExpiredPreviewIsRejected()
    {
        var now = new DateTimeOffset(2026, 7, 30, 11, 0, 0, TimeSpan.Zero);
        var accessor = new FakeSelectionAccessor(
            new SelectedTextCapture("Xin chào", (nint)42, (nint)420));
        using var coordinator = new TranslationCoordinator(
            accessor,
            new FakeProvider(new TranslationResult("Hello", "VI", "Fake")),
            clock: () => now,
            previewLifetime: TimeSpan.FromSeconds(5));
        var previewOutcome = coordinator.TranslateSelectionAsync(
                "key",
                "EN-US",
                CancellationToken.None,
                preview: true)
            .GetAwaiter().GetResult();
        now = now.AddSeconds(6);

        var applied = coordinator.ApplyPreview(previewOutcome.Preview!);

        AssertEx.Equal(TranslationOutcomeStatus.Failed, applied.Status);
        AssertEx.Equal(TranslationFailureCode.PreviewExpired, applied.FailureCode);
        AssertEx.Equal(0, accessor.PreviewReplaceCount);
        AssertEx.False(coordinator.CanUndo, "Expired preview created undo state.");
    }

    [KeyinaTest("translation coordinator refuses replacement after foreground focus changes")]
    private static void FocusChangePreventsReplacement()
    {
        var accessor = new FakeSelectionAccessor(
            new SelectedTextCapture("Xin chào", (nint)42, (nint)420))
        {
            ReplacementResult = false,
        };
        var provider = new FakeProvider(new TranslationResult("Hello", "VI", "Fake"));
        using var coordinator = new TranslationCoordinator(accessor, provider);

        var outcome = coordinator.TranslateSelectionAsync(
                "key",
                "EN-US",
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(TranslationOutcomeStatus.Failed, outcome.Status);
        AssertEx.Equal(TranslationFailureCode.FocusChanged, outcome.FailureCode);
        AssertEx.Equal(1, accessor.ReplaceCount);
    }

    [KeyinaTest("a newer translation command cancels the previous request")]
    private static void NewCommandCancelsPreviousRequest()
    {
        var accessor = new FakeSelectionAccessor(
            new SelectedTextCapture("first", (nint)42, (nint)420));
        var provider = new SupersedingProvider();
        using var coordinator = new TranslationCoordinator(accessor, provider);

        var first = coordinator.TranslateSelectionAsync("key", "VI", CancellationToken.None);
        provider.FirstCallStarted.Task.GetAwaiter().GetResult();
        var second = coordinator.TranslateSelectionAsync("key", "EN-US", CancellationToken.None);

        var firstOutcome = first.GetAwaiter().GetResult();
        var secondOutcome = second.GetAwaiter().GetResult();

        AssertEx.Equal(TranslationOutcomeStatus.Cancelled, firstOutcome.Status);
        AssertEx.Equal(TranslationOutcomeStatus.Succeeded, secondOutcome.Status);
        AssertEx.Equal(2, provider.CallCount);
        AssertEx.Equal("second", accessor.LastReplacement);
    }

    [KeyinaTest("translation coordinator maps unexpected selection failures without text leakage")]
    private static void UnexpectedSelectionFailureIsStable()
    {
        using var coordinator = new TranslationCoordinator(
            new ThrowingSelectionAccessor(),
            new FakeProvider(new TranslationResult("Hello", "VI", "Fake")));

        var outcome = coordinator.TranslateSelectionAsync(
                "key",
                "EN-US",
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(TranslationOutcomeStatus.Failed, outcome.Status);
        AssertEx.Equal(TranslationFailureCode.Unavailable, outcome.FailureCode);
        AssertEx.False(
            outcome.ToString().Contains("private clipboard text", StringComparison.Ordinal),
            "Translation outcome leaked clipboard exception text.");
    }

    [KeyinaTest("translation coordinator returns provider failure codes without text leakage")]
    private static void ProviderFailureIsStable()
    {
        const string selectedText = "private selected text";
        var accessor = new FakeSelectionAccessor(
            new SelectedTextCapture(selectedText, (nint)42, (nint)420));
        var provider = new ThrowingProvider();
        using var coordinator = new TranslationCoordinator(accessor, provider);

        var outcome = coordinator.TranslateSelectionAsync(
                "key",
                "EN-US",
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(TranslationOutcomeStatus.Failed, outcome.Status);
        AssertEx.Equal(TranslationFailureCode.QuotaExceeded, outcome.FailureCode);
        AssertEx.False(
            outcome.ToString().Contains(selectedText, StringComparison.Ordinal),
            "Translation outcome leaked selected text.");
    }

    private sealed class ThrowingSelectionAccessor : ISelectedTextAccessor
    {
        public Task<SelectedTextCapture?> CaptureAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("private clipboard text");

        public bool TryReplace(SelectedTextCapture selectedText, string translatedText) => false;
    }

    private sealed class FakeSelectionAccessor(SelectedTextCapture? capture) : ISelectedTextAccessor
    {
        public bool ReplacementResult { get; init; } = true;

        public bool RestoreResult { get; init; } = true;

        public int ReplaceCount { get; private set; }

        public int RestoreCount { get; private set; }

        public int PreviewReplaceCount { get; private set; }

        public string? LastReplacement { get; private set; }

        public string? ExpectedTranslatedText { get; private set; }

        public string? OriginalText { get; private set; }

        public Task<SelectedTextCapture?> CaptureAsync(CancellationToken cancellationToken) =>
            Task.FromResult(capture);

        public bool TryReplace(SelectedTextCapture selectedText, string translatedText)
        {
            ReplaceCount++;
            LastReplacement = translatedText;
            return ReplacementResult;
        }

        public bool TryReplaceFromPreview(
            SelectedTextCapture selectedText,
            string translatedText)
        {
            PreviewReplaceCount++;
            LastReplacement = translatedText;
            return ReplacementResult;
        }

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

    private sealed class FakeProvider(TranslationResult result) : ITranslationProvider
    {
        public int CallCount { get; private set; }

        public TranslationRequest? LastRequest { get; private set; }

        public Task<TranslationResult> TranslateAsync(
            string apiKey,
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingProvider : ITranslationProvider
    {
        public Task<TranslationResult> TranslateAsync(
            string apiKey,
            TranslationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<TranslationResult>(new TranslationException(
                TranslationFailureCode.QuotaExceeded,
                "Quota exhausted."));
    }

    private sealed class SupersedingProvider : ITranslationProvider
    {
        public TaskCompletionSource FirstCallStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async Task<TranslationResult> TranslateAsync(
            string apiKey,
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                FirstCallStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new TranslationResult("second", "VI", "Fake");
        }
    }
}
