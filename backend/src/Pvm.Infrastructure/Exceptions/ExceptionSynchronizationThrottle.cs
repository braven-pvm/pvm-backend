namespace Pvm.Infrastructure.Exceptions;

/// <summary>
/// Limits how often a read of the exception queue runs the full synchronization. The
/// synchronization scans every submission, candidate, dead letter and outbox message, so
/// running it on each page load made the queue slow to open. One instance per process.
/// </summary>
/// <remarks>
/// A read that arrives while another request synchronizes does not wait for it. It shows the
/// queue as it stands, which is at most one interval old. The explicit refresh action still
/// synchronizes every time, and records that it did through <see cref="MarkSynchronized"/>.
/// An admin action calls <see cref="Expire"/>, so the next read synchronizes again.
/// </remarks>
public sealed class ExceptionSynchronizationThrottle(TimeSpan interval)
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _running = new(1, 1);
    private long _lastSynchronizedTicks = long.MinValue;
    private long _expiries;

    public TimeSpan Interval { get; } = interval;

    /// <summary>
    /// Runs <paramref name="synchronize"/> when the last synchronization is older than the
    /// interval and no other synchronization is running. Returns true when it ran.
    /// </summary>
    public async Task<bool> RunIfDueAsync(
        DateTimeOffset now,
        Func<CancellationToken, Task> synchronize,
        CancellationToken cancellationToken)
    {
        if (!IsDue(now) || !await _running.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return false;
        }

        try
        {
            if (!IsDue(now))
            {
                return false;
            }

            // An Expire during the run may concern a change the run did not see. Then the run
            // does not count, and the next read synchronizes again.
            var expiries = Interlocked.Read(ref _expiries);
            await synchronize(cancellationToken);
            if (Interlocked.Read(ref _expiries) == expiries)
            {
                MarkSynchronized(now);
            }

            return true;
        }
        finally
        {
            _running.Release();
        }
    }

    /// <summary>Records a synchronization that ran outside <see cref="RunIfDueAsync"/>.</summary>
    public void MarkSynchronized(DateTimeOffset now)
    {
        var ticks = now.UtcTicks;
        long current;
        do
        {
            current = Interlocked.Read(ref _lastSynchronizedTicks);
            if (current >= ticks)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _lastSynchronizedTicks, ticks, current) != current);
    }

    /// <summary>
    /// Makes the next read synchronize. Call it after an action that changes what the
    /// synchronization derives, so the queue shows the change at once.
    /// </summary>
    public void Expire()
    {
        Interlocked.Increment(ref _expiries);
        Interlocked.Exchange(ref _lastSynchronizedTicks, long.MinValue);
    }

    private bool IsDue(DateTimeOffset now)
    {
        var last = Interlocked.Read(ref _lastSynchronizedTicks);
        return last == long.MinValue || now.UtcTicks - last >= Interval.Ticks;
    }
}
