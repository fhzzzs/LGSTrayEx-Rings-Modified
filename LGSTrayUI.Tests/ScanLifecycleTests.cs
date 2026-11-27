using System.Windows.Threading;
using LGSTrayPrimitives;

namespace LGSTrayUI.Tests;

public class ScanLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static TaskbarBattery Pad(int level) =>
        new(DeviceType.Gamepad, "Pad", level, "已连接", false, DateTimeOffset.UtcNow, "gip:pad");

    private static Task OnDispatcher(Func<Task> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            async void Run()
            {
                try { await test(); done.TrySetResult(); }
                catch (Exception ex) { done.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            }
            dispatcher.BeginInvoke(new Action(Run));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public Task NativeAndControllerEventsFormOneBatchAndSleepDropsPendingEvents() => OnDispatcher(async () =>
    {
        var scopes = new List<DeviceChangeScope>();
        var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var batch = new DeviceChangeBatch(Dispatcher.CurrentDispatcher, scope =>
        {
            scopes.Add(scope);
            notification.TrySetResult();
        });

        Parallel.For(0, 32, i => batch.Queue(i == 16 ? DeviceChangeScope.All : DeviceChangeScope.Controllers));
        await notification.Task.WaitAsync(Timeout);
        Assert.Equal(DeviceChangeScope.All, Assert.Single(scopes));

        notification = new(TaskCreationOptions.RunContinuationsAsynchronously);
        batch.Queue(DeviceChangeScope.All);
        batch.Reset(paused: true);
        batch.Queue(DeviceChangeScope.All);
        batch.Reset();
        batch.Queue(DeviceChangeScope.Controllers);
        await notification.Task.WaitAsync(Timeout);
        Assert.Equal(new[] { DeviceChangeScope.All, DeviceChangeScope.Controllers }, scopes);
    });

    [Fact]
    public Task ChangesDuringAReadRequestOneFollowUpWithoutCancellingIt() => OnDispatcher(async () =>
    {
        var first = new TaskCompletionSource<IReadOnlyList<TaskbarBattery>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        int scans = 0, otherScans = 0;
        var gamepad = new BatteryProvider("gip", _ => true, _ => TimeSpan.FromMinutes(1), Timeout, token =>
        {
            if (++scans == 1) { firstToken = token; return first.Task; }
            refreshed.TrySetResult();
            return Task.FromResult<IReadOnlyList<TaskbarBattery>>([Pad(80)]);
        });
        var other = new BatteryProvider("other", _ => true, _ => TimeSpan.FromMinutes(1), Timeout, _ =>
        {
            otherScans++;
            return Task.FromResult<IReadOnlyList<TaskbarBattery>>([]);
        });
        using var scheduler = new ProviderScheduler([gamepad, other], Dispatcher.CurrentDispatcher, () => { });
        scheduler.Poll();
        for (int i = 0; i < 20; i++) scheduler.HardwareChanged(DeviceChangeScope.Controllers);
        Assert.Equal(1, scans);
        Assert.False(firstToken.IsCancellationRequested);
        first.SetResult([Pad(70)]);
        await refreshed.Task.WaitAsync(Timeout);
        Assert.Equal(80, Assert.Single(scheduler.Readings).Level);
        Assert.Equal(2, scans);
        Assert.Equal(1, otherScans);
    });

    [Fact]
    public Task TimeoutKeepsOfflineRowsOfflineAndDiscardsALateReading() => OnDispatcher(async () =>
    {
        var offline = Pad(22) with { Key = "gip:offline", Status = "离线", Level = null };
        var waiting = TaskbarBattery.Waiting(DeviceType.Gamepad, "Other", "gip:waiting");
        var stalled = new TaskCompletionSource<IReadOnlyList<TaskbarBattery>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int scans = 0;
        var provider = new BatteryProvider("gip", _ => true, _ => TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(80),
            _ => ++scans switch
            {
                1 => Task.FromResult<IReadOnlyList<TaskbarBattery>>([Pad(70), offline, waiting]),
                2 => stalled.Task,
                _ => Task.FromResult<IReadOnlyList<TaskbarBattery>>([Pad(60), offline, waiting])
            });
        ProviderScheduler? observing = null;
        var published = new List<int?>();
        using var scheduler = new ProviderScheduler([provider], Dispatcher.CurrentDispatcher, () =>
        {
            var row = observing!.Readings.First();
            published.Add(row.Level);
            if (row.Stale) stale.TrySetResult();
            if (row.Level == 60) restored.TrySetResult();
        });
        observing = scheduler;
        scheduler.Poll();
        scheduler.Rescan();
        await stale.Task.WaitAsync(Timeout);
        var rows = scheduler.Readings.ToArray();
        Assert.Equal(ConnectionState.Reconnecting, rows[0].Connection);
        Assert.Equal(70, rows[0].LastKnownLevel);
        Assert.Null(rows[0].Level);
        Assert.Same(offline, rows[1]);
        Assert.Same(waiting, rows[2]);
        scheduler.HardwareChanged(DeviceChangeScope.All);
        Assert.Equal(2, scans);
        stalled.SetResult([Pad(99)]);
        await restored.Task.WaitAsync(Timeout);
        Assert.DoesNotContain((int?)99, published);
        Assert.Equal(3, scans);
    });

    [Fact]
    public Task DisabledProvidersStayIdleAndShorteningTheIntervalRearmsTheTimer() => OnDispatcher(async () =>
    {
        bool enabled = false;
        int checks = 0, scans = 0;
        var period = TimeSpan.FromMinutes(1);
        var secondScan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new BatteryProvider("gip", _ => { checks++; return enabled; }, _ => period, Timeout, _ =>
        {
            if (++scans == 2) secondScan.TrySetResult();
            return Task.FromResult<IReadOnlyList<TaskbarBattery>>([Pad(60)]);
        });
        using var scheduler = new ProviderScheduler([provider], Dispatcher.CurrentDispatcher, () => { });
        scheduler.Poll();
        await Task.Delay(1250);
        Assert.Equal(1, checks);
        Assert.Equal(0, scans);
        enabled = true;
        scheduler.Poll();
        Assert.Equal(1, scans);
        period = TimeSpan.FromMilliseconds(250);
        scheduler.Poll();
        await secondScan.Task.WaitAsync(Timeout);
        Assert.Equal(2, scans);
    });
}
