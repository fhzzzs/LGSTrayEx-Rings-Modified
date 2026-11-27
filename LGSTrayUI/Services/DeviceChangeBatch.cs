using System;
using System.Threading;
using System.Windows.Threading;

namespace LGSTrayUI;

internal enum DeviceChangeScope { Controllers = 1, All = 2 }

// Windows can report one attachment through several device interfaces and WGI.
// The first notification opens a fixed window; later events cannot postpone it.
internal sealed class DeviceChangeBatch : IDisposable
{
    private const int Paused = -1;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private readonly Action<DeviceChangeScope> onChange;
    private int pending;

    internal DeviceChangeBatch(Dispatcher dispatcher, Action<DeviceChangeScope> onChange)
    {
        this.dispatcher = dispatcher;
        this.onChange = onChange;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += Elapsed;
    }

    // WGI callbacks can arrive off the dispatcher. Post once per batch.
    internal void Queue(DeviceChangeScope scope)
    {
        if (dispatcher.HasShutdownStarted || Interlocked.Or(ref pending, (int)scope) != 0) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(StartTimer));
    }

    private void StartTimer()
    {
        if (Volatile.Read(ref pending) > 0) timer.Start();
    }

    private void Elapsed(object? sender, EventArgs e)
    {
        timer.Stop();
        if (Volatile.Read(ref pending) <= 0) return;
        int changes = Interlocked.Exchange(ref pending, 0);
        onChange((changes & (int)DeviceChangeScope.All) != 0 ? DeviceChangeScope.All : DeviceChangeScope.Controllers);
    }

    // Called on the dispatcher. Native notifications arriving during sleep are
    // covered by the full inventory refresh on wake.
    internal void Reset(bool paused = false)
    {
        Interlocked.Exchange(ref pending, paused ? Paused : 0);
        timer.Stop();
    }

    public void Dispose()
    {
        Reset(paused: true);
        timer.Tick -= Elapsed;
    }
}
