namespace Keyina.Host.Core;

public static class HostReducer
{
    public static HostState Reduce(HostState state, HostEvent @event)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(@event);

        return @event switch
        {
            InputModeChanged inputMode => state with
            {
                VietnameseEnabled = inputMode.Enabled,
            },
            ListeningStarted => state with
            {
                Listening = true,
            },
            ListeningStopped => state with
            {
                Listening = false,
            },
            HostFailed failed => state with
            {
                ErrorCode = ValidateErrorCode(failed.ErrorCode),
            },
            HostRecovered => state with
            {
                ErrorCode = null,
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(@event),
                @event.GetType().FullName,
                "Unsupported host event."),
        };
    }

    private static string ValidateErrorCode(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code must not be empty.", nameof(errorCode));
        }

        return errorCode;
    }
}
