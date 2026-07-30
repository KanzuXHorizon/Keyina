namespace Keyina.Host.Translation;

public sealed record TranslationUndoEntry(
    SelectedTextCapture Capture,
    string OriginalText,
    string TranslatedText,
    DateTimeOffset ExpiresAt);

public sealed class TranslationUndoManager
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(30);
    private readonly object sync = new();
    private readonly ISelectedTextAccessor selectionAccessor;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan lifetime;
    private TranslationUndoEntry? entry;

    public TranslationUndoManager(
        ISelectedTextAccessor selectionAccessor,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? lifetime = null)
    {
        this.selectionAccessor = selectionAccessor ??
            throw new ArgumentNullException(nameof(selectionAccessor));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.lifetime = lifetime ?? DefaultLifetime;
        if (this.lifetime <= TimeSpan.Zero ||
            this.lifetime > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "Translation undo lifetime must be positive and at most five minutes.");
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (sync)
            {
                if (entry is null)
                {
                    return false;
                }
                if (entry.ExpiresAt <= clock())
                {
                    entry = null;
                    return false;
                }
                return true;
            }
        }
    }

    public void Record(
        SelectedTextCapture capture,
        string originalText,
        string translatedText)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalText);
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedText);
        lock (sync)
        {
            entry = new TranslationUndoEntry(
                capture,
                originalText,
                translatedText,
                clock() + lifetime);
        }
    }

    public async Task<bool> UndoAsync(CancellationToken cancellationToken)
    {
        TranslationUndoEntry? pending;
        lock (sync)
        {
            pending = entry;
            entry = null;
        }
        if (pending is null || pending.ExpiresAt <= clock())
        {
            return false;
        }

        return await selectionAccessor.TryRestoreAsync(
                pending.Capture,
                pending.TranslatedText,
                pending.OriginalText,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Clear()
    {
        lock (sync)
        {
            entry = null;
        }
    }
}
