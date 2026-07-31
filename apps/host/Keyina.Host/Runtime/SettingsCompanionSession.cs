namespace Keyina.Host.Runtime;

public static class SettingsCompanionProtocol
{
    public const string MutexName = CompanionCommandProtocol.MutexName;
    public const string EventName = "Local\\Keyina.OpenSettings";
}

public sealed class SettingsCompanionSession : IDisposable
{
    private static readonly TimeSpan SignalRetryWindow = TimeSpan.FromSeconds(2);
    private readonly KeyinaApplicationContext context;
    private readonly Control dispatcher = new();
    private readonly EventWaitHandle signal;
    private readonly RegisteredWaitHandle registration;
    private bool disposed;

    public SettingsCompanionSession(KeyinaApplicationContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        dispatcher.CreateControl();
        signal = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            SettingsCompanionProtocol.EventName);
        registration = ThreadPool.RegisterWaitForSingleObject(
            signal,
            (_, _) => PostOpen(),
            state: null,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    public void PostOpen()
    {
        if (disposed || dispatcher.IsDisposed)
        {
            return;
        }

        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (!disposed)
                {
                    context.OpenSettings();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The managed companion is shutting down.
        }
    }

    public static bool SignalExisting()
    {
        var deadline = DateTime.UtcNow + SignalRetryWindow;
        do
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(
                    SettingsCompanionProtocol.EventName);
                return existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(25);
            }
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        registration.Unregister(waitObject: null);
        signal.Dispose();
        dispatcher.Dispose();
    }
}
