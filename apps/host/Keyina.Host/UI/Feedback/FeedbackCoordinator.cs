using Keyina.Host.Core.Feedback;
using Keyina.Host.Windows.Feedback;

namespace Keyina.Host.UI.Feedback;

public interface IFeedbackOverlay : IDisposable
{
    void Present(FeedbackEvent feedbackEvent);

    void HideFeedback();
}

public sealed class FeedbackCoordinator : IDisposable
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(150);

    private readonly IForegroundPresentationProbe foregroundProbe;
    private readonly IFeedbackOverlay overlay;
    private readonly IFeedbackSoundPlayer soundPlayer;
    private readonly Func<DateTimeOffset> clock;
    private FeedbackPreferences preferences;
    private FeedbackEventKind? lastEventKind;
    private DateTimeOffset lastEventAt;
    private bool disposed;

    public FeedbackCoordinator(
        FeedbackPreferences preferences,
        IForegroundPresentationProbe foregroundProbe,
        IFeedbackOverlay overlay,
        IFeedbackSoundPlayer soundPlayer,
        Func<DateTimeOffset>? clock = null)
    {
        this.preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        this.foregroundProbe = foregroundProbe ??
            throw new ArgumentNullException(nameof(foregroundProbe));
        this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        this.soundPlayer = soundPlayer ?? throw new ArgumentNullException(nameof(soundPlayer));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void UpdatePreferences(FeedbackPreferences updatedPreferences)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        preferences = updatedPreferences ?? throw new ArgumentNullException(nameof(updatedPreferences));
        if (preferences.Mode == FeedbackMode.Off)
        {
            TryHideOverlay();
        }
    }

    public void Publish(FeedbackEvent feedbackEvent)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(feedbackEvent);

        var now = clock();
        if (lastEventKind == feedbackEvent.Kind &&
            now - lastEventAt <= DuplicateWindow)
        {
            return;
        }

        lastEventKind = feedbackEvent.Kind;
        lastEventAt = now;

        ForegroundPresentationState foregroundState;
        try
        {
            foregroundState = foregroundProbe.GetState();
        }
        catch (Exception)
        {
            foregroundState = ForegroundPresentationState.Unknown;
        }

        FeedbackPresentation presentation;
        try
        {
            presentation = FeedbackPresentationPolicy.Resolve(preferences, foregroundState);
        }
        catch (Exception)
        {
            return;
        }

        if (presentation.ShowOverlay)
        {
            try
            {
                overlay.Present(feedbackEvent);
            }
            catch (Exception)
            {
                // Visual feedback must not block audio or the originating command.
            }
        }
        else
        {
            TryHideOverlay();
        }

        if (presentation.PlaySound && feedbackEvent.SoundCue != FeedbackSoundCue.None)
        {
            try
            {
                soundPlayer.Play(feedbackEvent.SoundCue);
            }
            catch (Exception)
            {
                // Audio feedback is best-effort and isolated from all commands.
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            overlay.Dispose();
        }
        catch (Exception)
        {
            // Shutdown must remain reliable even when feedback cleanup fails.
        }
    }

    private void TryHideOverlay()
    {
        try
        {
            overlay.HideFeedback();
        }
        catch (Exception)
        {
            // Hiding a best-effort surface must not escape to the caller.
        }
    }
}
