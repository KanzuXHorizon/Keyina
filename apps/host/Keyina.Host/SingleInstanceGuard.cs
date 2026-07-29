namespace Keyina.Host;

public sealed class SingleInstanceGuard : IDisposable
{
    private static readonly object OwnershipLock = new();
    private static readonly HashSet<string> ProcessOwnedNames = new(StringComparer.Ordinal);

    private readonly string _name;
    private Mutex? _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(string name, Mutex mutex)
    {
        _name = name;
        _mutex = mutex;
        _ownsMutex = true;
    }

    public static bool TryAcquire(string name, out SingleInstanceGuard? guard)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Mutex name must not be empty.", nameof(name));
        }

        lock (OwnershipLock)
        {
            if (!ProcessOwnedNames.Add(name))
            {
                guard = null;
                return false;
            }
        }

        var mutex = new Mutex(initiallyOwned: false, name);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                lock (OwnershipLock)
                {
                    ProcessOwnedNames.Remove(name);
                }
                guard = null;
                return false;
            }

            guard = new SingleInstanceGuard(name, mutex);
            return true;
        }
        catch
        {
            if (!acquired)
            {
                mutex.Dispose();
            }
            lock (OwnershipLock)
            {
                ProcessOwnedNames.Remove(name);
            }
            throw;
        }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            if (_ownsMutex)
            {
                mutex.ReleaseMutex();
                _ownsMutex = false;
            }
        }
        finally
        {
            mutex.Dispose();
            lock (OwnershipLock)
            {
                ProcessOwnedNames.Remove(_name);
            }
        }
    }
}
