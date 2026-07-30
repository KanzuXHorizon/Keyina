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
}

public enum TranslationOutcomeStatus
{
    Succeeded,
    Cancelled,
    Failed,
}

public sealed record TranslationOutcome(
    TranslationOutcomeStatus Status,
    TranslationFailureCode? FailureCode)
{
    public static TranslationOutcome Succeeded { get; } =
        new(TranslationOutcomeStatus.Succeeded, null);

    public static TranslationOutcome Cancelled { get; } =
        new(TranslationOutcomeStatus.Cancelled, null);

    public static TranslationOutcome Failed(TranslationFailureCode failureCode) =>
        new(TranslationOutcomeStatus.Failed, failureCode);
}

public sealed class TranslationCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly ISelectedTextAccessor selectionAccessor;
    private readonly ITranslationProvider provider;
    private CancellationTokenSource? activeOperation;
    private bool disposed;

    public TranslationCoordinator(
        ISelectedTextAccessor selectionAccessor,
        ITranslationProvider provider)
    {
        this.selectionAccessor = selectionAccessor ??
            throw new ArgumentNullException(nameof(selectionAccessor));
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<TranslationOutcome> TranslateSelectionAsync(
        string apiKey,
        string targetLanguage,
        CancellationToken cancellationToken)
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

            return selectionAccessor.TryReplace(capture, result.Text)
                ? TranslationOutcome.Succeeded
                : TranslationOutcome.Failed(TranslationFailureCode.FocusChanged);
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
    }
}
