using Microsoft.Extensions.Time.Testing;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// A <see cref="FakeTimeProvider"/> wrapper that counts the timers the code under test
/// creates. A <c>Task.Delay</c> continuation may resume on the thread pool, so a test that
/// advances the clock in a tight loop could outrun it; waiting for the next timer lets
/// the test advance only once the code is parked on its next delay (exact lock-step).
/// </summary>
internal sealed class TimerCountingTimeProvider : TimeProvider
{
    private readonly FakeTimeProvider _inner = new();
    private int _timersCreated;

    public int TimersCreated => Volatile.Read(ref _timersCreated);

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    public override long GetTimestamp() => _inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = _inner.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timersCreated);
        return timer;
    }

    /// <summary>
    /// Advances by <paramref name="step"/> and waits until <paramref name="pending"/> has
    /// either completed or parked on a new timer.
    /// </summary>
    public void Step(Task pending, TimeSpan step)
    {
        var parked = TimersCreated;
        _inner.Advance(step);
        var settled = SpinWait.SpinUntil(
            () => pending.IsCompleted || TimersCreated != parked, TimeSpan.FromSeconds(10));
        if (!settled)
        {
            throw new TimeoutException("The code under test neither completed nor parked on a new timer.");
        }
    }
}
