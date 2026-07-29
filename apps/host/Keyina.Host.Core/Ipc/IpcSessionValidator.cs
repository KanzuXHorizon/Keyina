namespace Keyina.Host.Core.Ipc;

public enum IpcSessionValidation
{
    Valid,
    WrongSession,
    StaleFocus,
}

public sealed class IpcSessionValidator
{
    private readonly IpcSessionId _sessionId;
    private ulong _minimumFocusGeneration;

    public IpcSessionValidator(
        IpcSessionId sessionId,
        ulong minimumFocusGeneration)
    {
        _sessionId = sessionId;
        _minimumFocusGeneration = minimumFocusGeneration;
    }

    public IpcSessionValidation Validate(IpcEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.SessionId != _sessionId)
        {
            return IpcSessionValidation.WrongSession;
        }
        if (envelope.FocusGeneration < _minimumFocusGeneration)
        {
            return IpcSessionValidation.StaleFocus;
        }
        return IpcSessionValidation.Valid;
    }

    public void AdvanceFocusGeneration(ulong focusGeneration)
    {
        if (focusGeneration < _minimumFocusGeneration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(focusGeneration),
                "Focus generation must not move backwards.");
        }

        _minimumFocusGeneration = focusGeneration;
    }
}
