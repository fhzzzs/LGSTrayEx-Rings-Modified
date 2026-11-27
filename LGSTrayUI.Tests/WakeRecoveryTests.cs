namespace LGSTrayUI.Tests;

public class WakeRecoveryTests
{
    [Fact]
    public async Task SleepDuringRecoveryKeepsOnlyTheNextWakeAndRunsItOnce()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0, refreshed = 0;
        CancellationToken firstToken = default;
        var recovery = new WakeRecovery(async token =>
        {
            if (++starts == 1)
            {
                firstToken = token;
                await release.Task;
            }
            token.ThrowIfCancellationRequested();
            refreshed++;
        }, CancellationToken.None);

        var first = recovery.ResumeAsync();
        Assert.Same(first, recovery.ResumeAsync());
        Assert.True(recovery.Suspend());
        Assert.False(recovery.Suspend());
        Assert.True(firstToken.IsCancellationRequested);
        var second = recovery.ResumeAsync();
        Assert.Same(second, recovery.ResumeAsync());
        Assert.Equal(1, starts);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, starts);
        Assert.Equal(1, refreshed);
    }

    [Fact]
    public async Task StayingAsleepOrShuttingDownDoesNotRestartScanning()
    {
        using var stopping = new CancellationTokenSource();
        int starts = 0, refreshed = 0;
        var recovery = new WakeRecovery(async token =>
        {
            starts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            refreshed++;
        }, stopping.Token);

        var first = recovery.ResumeAsync();
        recovery.Suspend();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, starts);
        var next = recovery.ResumeAsync();
        stopping.Cancel();
        await next.WaitAsync(TimeSpan.FromSeconds(5));
        await recovery.ResumeAsync();
        Assert.Equal(2, starts);
        Assert.Equal(0, refreshed);
    }
}
