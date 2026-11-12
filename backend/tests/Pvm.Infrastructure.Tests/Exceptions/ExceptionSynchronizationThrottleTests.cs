using Pvm.Infrastructure.Exceptions;

namespace Pvm.Infrastructure.Tests.Exceptions;

public sealed class ExceptionSynchronizationThrottleTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_read_synchronizes_and_reads_within_the_interval_do_not()
    {
        var throttle = new ExceptionSynchronizationThrottle(TimeSpan.FromMinutes(1));
        var runs = 0;

        Assert.True(await throttle.RunIfDueAsync(Start, Count, CancellationToken.None));
        Assert.False(await throttle.RunIfDueAsync(Start.AddSeconds(30), Count, CancellationToken.None));
        Assert.False(await throttle.RunIfDueAsync(Start.AddSeconds(59), Count, CancellationToken.None));
        Assert.True(await throttle.RunIfDueAsync(Start.AddSeconds(60), Count, CancellationToken.None));
        Assert.Equal(2, runs);

        Task Count(CancellationToken _)
        {
            runs++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_read_during_a_running_synchronization_does_not_wait_or_run_again()
    {
        var throttle = new ExceptionSynchronizationThrottle(TimeSpan.FromMinutes(1));
        var release = new TaskCompletionSource();
        var runs = 0;

        var first = throttle.RunIfDueAsync(
            Start,
            async _ =>
            {
                Interlocked.Increment(ref runs);
                await release.Task;
            },
            CancellationToken.None);
        var second = await throttle.RunIfDueAsync(
            Start,
            _ =>
            {
                Interlocked.Increment(ref runs);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        release.SetResult();

        Assert.False(second);
        Assert.True(await first);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task A_failed_synchronization_does_not_count()
    {
        var throttle = new ExceptionSynchronizationThrottle(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => throttle.RunIfDueAsync(
            Start,
            _ => throw new InvalidOperationException("database unavailable"),
            CancellationToken.None));

        Assert.True(await throttle.RunIfDueAsync(Start.AddSeconds(1), _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task An_admin_action_makes_the_next_read_synchronize()
    {
        var throttle = new ExceptionSynchronizationThrottle(TimeSpan.FromMinutes(1));
        await throttle.RunIfDueAsync(Start, _ => Task.CompletedTask, CancellationToken.None);

        throttle.Expire();

        Assert.True(await throttle.RunIfDueAsync(Start.AddSeconds(5), _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task An_admin_action_during_a_synchronization_is_not_lost()
    {
        var throttle = new ExceptionSynchronizationThrottle(TimeSpan.FromMinutes(1));

        await throttle.RunIfDueAsync(
            Start,
            _ =>
            {
                throttle.Expire();
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(await throttle.RunIfDueAsync(Start.AddSeconds(5), _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task An_explicit_refresh_counts_as_a_synchronization()
    {
        var throttle = new ExceptionSynchronizationThrottle(TimeSpan.FromMinutes(1));

        throttle.MarkSynchronized(Start);

        Assert.False(await throttle.RunIfDueAsync(Start.AddSeconds(10), _ => Task.CompletedTask, CancellationToken.None));
    }
}
