namespace Keyina.Host.Core.Speech;

public static class DictationReducer
{
    public static DictationState Apply(DictationState state, DictationEvent @event)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(@event);

        return @event switch
        {
            DictationEvent.StartRequested => Start(state),
            DictationEvent.RecognitionStarted => RecognitionStarted(state),
            DictationEvent.PartialUpdated partial => PartialUpdated(state, partial.Text),
            DictationEvent.FinalReceived final => FinalReceived(state, final.CommittedText),
            DictationEvent.StopRequested => StopRequested(state),
            DictationEvent.FinalInserted => FinalInserted(state),
            DictationEvent.Failed failed => Failed(state, failed.ErrorCode),
            DictationEvent.Cancelled => Cancelled(state),
            DictationEvent.Reset => Reset(state),
            _ => throw new InvalidOperationException($"Unknown dictation event {@event.GetType().Name}."),
        };
    }

    private static DictationState Start(DictationState state)
    {
        RequireStatus(state, DictationStatus.Idle);
        return new DictationState(
            DictationStatus.Connecting,
            string.Empty,
            string.Empty,
            0,
            null);
    }

    private static DictationState RecognitionStarted(DictationState state)
    {
        RequireStatus(state, DictationStatus.Connecting);
        return state with { Status = DictationStatus.Listening };
    }

    private static DictationState PartialUpdated(DictationState state, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (state.Status is not DictationStatus.Listening and not DictationStatus.Finalizing)
        {
            throw InvalidTransition(state, nameof(DictationEvent.PartialUpdated));
        }

        return state with { PartialText = text };
    }

    private static DictationState FinalReceived(
        DictationState state,
        string committedText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(committedText);
        if (state.Status is not DictationStatus.Listening and not DictationStatus.Finalizing)
        {
            throw InvalidTransition(state, nameof(DictationEvent.FinalReceived));
        }

        return state with
        {
            PartialText = string.Empty,
            CommittedText = committedText,
            FinalSegments = checked(state.FinalSegments + 1),
        };
    }

    private static DictationState StopRequested(DictationState state)
    {
        RequireStatus(state, DictationStatus.Listening);
        return state with { Status = DictationStatus.Finalizing };
    }

    private static DictationState FinalInserted(DictationState state)
    {
        RequireStatus(state, DictationStatus.Finalizing);
        return state with
        {
            Status = DictationStatus.Inserted,
            PartialText = string.Empty,
            CommittedText = string.Empty,
        };
    }

    private static DictationState Failed(DictationState state, string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (state.Status == DictationStatus.Idle)
        {
            throw InvalidTransition(state, nameof(DictationEvent.Failed));
        }

        return state with
        {
            Status = DictationStatus.Error,
            PartialText = string.Empty,
            CommittedText = string.Empty,
            ErrorCode = errorCode,
        };
    }

    private static DictationState Cancelled(DictationState state)
    {
        if (state.Status is not DictationStatus.Connecting and
            not DictationStatus.Listening and
            not DictationStatus.Finalizing)
        {
            throw InvalidTransition(state, nameof(DictationEvent.Cancelled));
        }

        return state with
        {
            Status = DictationStatus.Cancelled,
            PartialText = string.Empty,
            CommittedText = string.Empty,
        };
    }

    private static DictationState Reset(DictationState state)
    {
        if (state.Status is not DictationStatus.Error and
            not DictationStatus.Cancelled and
            not DictationStatus.Inserted)
        {
            throw InvalidTransition(state, nameof(DictationEvent.Reset));
        }

        return DictationState.Initial;
    }

    private static void RequireStatus(DictationState state, DictationStatus required)
    {
        if (state.Status != required)
        {
            throw InvalidTransition(state, required.ToString());
        }
    }

    private static InvalidOperationException InvalidTransition(
        DictationState state,
        string operation) =>
        new($"Cannot apply {operation} while dictation state is {state.Status}.");
}
