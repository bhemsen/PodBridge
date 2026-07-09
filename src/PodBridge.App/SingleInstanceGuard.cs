using System.Threading;

namespace PodBridge.App;

/// <summary>
/// Enforces a single running PodBridge instance via a named <see cref="Mutex"/>.
/// The first launch owns the mutex (<see cref="IsPrimaryInstance"/> is
/// <c>true</c>); a second launch fails to create it and must surface an
/// "already running" notice and exit.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex? mutex, bool isPrimaryInstance)
    {
        _mutex = mutex;
        IsPrimaryInstance = isPrimaryInstance;
    }

    /// <summary>True when this process is the first (owning) instance.</summary>
    public bool IsPrimaryInstance { get; }

    /// <summary>
    /// Attempts to claim the single-instance mutex identified by
    /// <paramref name="mutexName"/>.
    /// </summary>
    public static SingleInstanceGuard Acquire(string mutexName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, isPrimaryInstance: true);
        }

        mutex.Dispose();
        return new SingleInstanceGuard(mutex: null, isPrimaryInstance: false);
    }

    /// <summary>Releases the mutex if this instance owns it.</summary>
    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _mutex = null;
    }
}
