using Keyina.Host.Core.Translation;

namespace Keyina.Host.Translation;

public sealed record SelectedTextCapture(
    string Text,
    nint ForegroundWindow,
    nint FocusedWindow);

public interface ISelectedTextAccessor
{
    Task<SelectedTextCapture?> CaptureAsync(CancellationToken cancellationToken);

    bool TryReplace(SelectedTextCapture selectedText, string translatedText);

    bool TryReplaceFromPreview(
        SelectedTextCapture selectedText,
        string translatedText) => TryReplace(selectedText, translatedText);

    Task<bool> TryRestoreAsync(
        SelectedTextCapture selectedText,
        string expectedTranslatedText,
        string originalText,
        CancellationToken cancellationToken) => Task.FromResult(false);
}

public enum TranslationOutcomeStatus
{
    Succeeded,
    PreviewReady,
    Cancelled,
    Failed,
}

public sealed record TranslationPreview(
    SelectedTextCapture Capture,
    string OriginalText,
    string TranslatedText,
    string DetectedSourceLanguage,
    string Provider,
    DateTimeOffset ExpiresAt);

public sealed record TranslationOutcome(
    TranslationOutcomeStatus Status,
    TranslationFailureCode? FailureCode,
    TranslationPreview? Preview = null)
{
    public static TranslationOutcome Succeeded { get; } =
        new(TranslationOutcomeStatus.Succeeded, null);

    public static TranslationOutcome Cancelled { get; } =
        new(TranslationOutcomeStatus.Cancelled, null);

    public static TranslationOutcome PreviewReady(TranslationPreview preview) =>
        new(TranslationOutcomeStatus.PreviewReady, null, preview);

    public static TranslationOutcome Failed(TranslationFailureCode failureCode) =>
        new(TranslationOutcomeStatus.Failed, failureCode);
}

public sealed class TranslationCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly ISelectedTextAccessor selectionAccessor;
    private static readonly TimeSpan DefaultPreviewLifetime = TimeSpan.FromMinutes(2);
    private readonly ITranslationProvider provider;
    private readonly TranslationUndoManager undoManager;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan previewLifetime;
    private CancellationTokenSource? activeOperation;
    private bool disposed;

    public TranslationCoordinator(
        ISelectedTextAccessor selectionAccessor,
        ITranslationProvider provider,
        TranslationUndoManager? undoManager = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? previewLifetime = null)
    {
        this.selectionAccessor = selectionAccessor ??
            throw new ArgumentNullException(nameof(selectionAccessor));
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.undoManager = undoManager ?? new TranslationUndoManager(selectionAccessor);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.previewLifetime = previewLifetime ?? DefaultPreviewLifetime;
        if (this.previewLifetime <= TimeSpan.Zero ||
            this.previewLifetime > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(previewLifetime),
                "Translation preview lifetime must be positive and at most ten minutes.");
        }
    }

    public bool CanUndo => undoManager.CanUndo;

    public async Task<TranslationOutcome> TranslateSelectionAsync(
        string apiKey,
        string targetLanguage,
        CancellationToken cancellationToken,
        bool preview = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeOperation?.Cancel();
            activeOperation = operation;
        }

        try
        {
            undoManager.Clear();
            var capture = await selectionAccessor.CaptureAsync(operation.Token)
                .ConfigureAwait(false);
            if (capture is null || string.IsNullOrWhiteSpace(capture.Text))
            {
                return TranslationOutcome.Failed(TranslationFailureCode.NoSelection);
            }

            var request = new TranslationRequest(capture.Text, targetLanguage);
            var result = await provider.TranslateAsync(apiKey, request, operation.Token)
                .ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();

            if (preview)
            {
                return TranslationOutcome.PreviewReady(new TranslationPreview(
                    capture,
                    capture.Text,
                    result.Text,
                    result.DetectedSourceLanguage,
                    result.Provider,
                    clock() + previewLifetime));
            }

            if (!selectionAccessor.TryReplace(capture, result.Text))
            {
                return TranslationOutcome.Failed(TranslationFailureCode.FocusChanged);
            }

            undoManager.Record(capture, capture.Text, result.Text);
            return TranslationOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return TranslationOutcome.Cancelled;
        }
        catch (TranslationException exception)
        {
            return TranslationOutcome.Failed(exception.FailureCode);
        }
        catch (Exception)
        {
            return TranslationOutcome.Failed(TranslationFailureCode.Unavailable);
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(activeOperation, operation))
                {
                    activeOperation = null;
                }
            }
            operation.Dispose();
        }
    }

    public TranslationOutcome ApplyPreview(TranslationPreview preview)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.ExpiresAt <= clock())
        {
            return TranslationOutcome.Failed(TranslationFailureCode.PreviewExpired);
        }
        if (!selectionAccessor.TryReplaceFromPreview(
                preview.Capture,
                preview.TranslatedText))
        {
            return TranslationOutcome.Failed(TranslationFailureCode.FocusChanged);
        }

        undoManager.Record(
            preview.Capture,
            preview.OriginalText,
            preview.TranslatedText);
        return TranslationOutcome.Succeeded;
    }

    public Task<bool> UndoAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return undoManager.UndoAsync(cancellationToken);
    }

    public void Cancel()
    {
        lock (sync)
        {
            activeOperation?.Cancel();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? operation;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            operation = activeOperation;
            activeOperation = null;
        }
        operation?.Cancel();
        undoManager.Clear();
    }
}
