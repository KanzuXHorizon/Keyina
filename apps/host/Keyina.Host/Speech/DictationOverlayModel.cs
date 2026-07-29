using Keyina.Host.Core.Speech;

namespace Keyina.Host.Speech;

public sealed class DictationOverlayModel
{
    private readonly object gate = new();
    private DictationState state = DictationState.Initial;

    public DictationState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public event EventHandler<DictationState>? StateChanged;

    public DictationState Apply(DictationEvent @event)
    {
        DictationState next;
        lock (gate)
        {
            next = DictationReducer.Apply(state, @event);
            state = next;
        }

        StateChanged?.Invoke(this, next);
        return next;
    }
}
