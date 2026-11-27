using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal interface IBatteryProvider
{
    string Id {get;}
    bool Enabled(RingPreferences settings);
    TimeSpan Interval(RingPreferences settings);
    TimeSpan Timeout {get;}
    Task<IReadOnlyList<TaskbarBattery>> ReadAsync(CancellationToken token);
}
internal sealed record BatteryProvider(string Id,Func<RingPreferences,bool> IsEnabled,
    Func<RingPreferences,TimeSpan> Period,TimeSpan Timeout,
    Func<CancellationToken,Task<IReadOnlyList<TaskbarBattery>>> Read):IBatteryProvider
{
    public bool Enabled(RingPreferences settings)=>IsEnabled(settings);
    public TimeSpan Interval(RingPreferences settings)=>Period(settings);
    public Task<IReadOnlyList<TaskbarBattery>> ReadAsync(CancellationToken token)=>Read(token);
}
internal sealed class ProviderScheduler:IDisposable
{
    private sealed class Slot(IBatteryProvider provider)
    {
        internal IBatteryProvider Provider=provider;
        internal IReadOnlyList<TaskbarBattery> Rows=[];
        internal long DueAt;
        internal long? LastFinishedAt;
        internal TimeSpan Period;
        internal CancellationTokenSource? Flight;
        internal int Generation,Failures,StablePolls;
        internal bool Enabled,EverEnabled,RefreshAfterFlight;
        internal double LastMs;
        internal DateTimeOffset Updated;
        internal string? Error;
    }
    private readonly Slot[] slots;
    private readonly DispatcherTimer timer;
    private readonly Action changed;
    private bool disposed,suspended;
    private readonly long created=Environment.TickCount64;
    private long scheduledAt;
    internal static int InitialDelayMs(string id)=>id switch{"asus"=>750,"astro"=>1500,"controller-hid"=>2250,"gamesir-hid"=>3000,"ble"=>500,_=>0};
    internal static TimeSpan NextDelay(TimeSpan period,int failures,bool online)
    {
        double seconds=Math.Clamp(period.TotalSeconds,.25,120);
        if(failures>0)return TimeSpan.FromSeconds(Math.Min(120,seconds*Math.Pow(2,Math.Min(4,failures))));
        return TimeSpan.FromSeconds(online?seconds:Math.Max(seconds,Math.Min(15,seconds*2)));
    }
    internal ProviderScheduler(IEnumerable<IBatteryProvider> providers,Dispatcher dispatcher,Action changed)
    {
        this.changed=changed;slots=providers.Select(p=>new Slot(p)).ToArray();
        timer=new DispatcherTimer(DispatcherPriority.Background,dispatcher){Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=Tick;timer.Start();
    }
    internal IEnumerable<TaskbarBattery> Readings=>slots.SelectMany(s=>s.Rows);
    private void Publish(Slot s)=>RingDiagnostics.Provider(new(s.Provider.Id,s.Enabled,s.Flight!=null,s.Failures,s.LastMs,s.Rows.Count,s.Updated,s.Error));
    private void Tick(object? sender,EventArgs e)
    {
        timer.Stop();
        Poll();
    }

    internal void Rescan()
    {
        if (disposed || suspended) return;
        foreach (var s in slots)
        {
            s.Generation++;
            s.StablePolls = 0;
            s.DueAt = 0;
            s.Flight?.Cancel();
        }
        Poll();
    }

    internal void HardwareChanged(DeviceChangeScope scope)
    {
        if (disposed || suspended) return;
        foreach (var s in slots)
        {
            if (scope == DeviceChangeScope.Controllers &&
                s.Provider.Id is not ("gip" or "xinput" or "controller-hid" or "gamesir-hid")) continue;
            s.StablePolls = 0;
            s.DueAt = 0;
            s.RefreshAfterFlight = s.Flight != null;
        }
        Poll();
    }

    internal void Suspend(){if(disposed||suspended)return;suspended=true;timer.Stop();foreach(var s in slots){s.Generation++;s.StablePolls=0;s.Flight?.Cancel();}}
    internal void Resume()
    {
        if (disposed) return;
        suspended = false;
        long now = Environment.TickCount64;
        bool invalidated = false;
        foreach (var s in slots)
        {
            s.Generation++;
            s.StablePolls = 0;
            s.LastFinishedAt = null;
            s.DueAt = now + InitialDelayMs(s.Provider.Id);
            s.Flight?.Cancel();
            invalidated |= InvalidateConnectedRows(s);
        }
        if (invalidated) changed();
        Poll();
    }

    internal static TaskbarBattery InvalidateForResume(TaskbarBattery r)=>r.IsConnected?r.WithConnection(ConnectionState.Reconnecting) with{LastKnownLevel=r.Level??r.LastKnownLevel,Level=null,ShortLabel=null,Stale=true,Charging=false}:r;
    private static bool InvalidateConnectedRows(Slot s)
    {
        if (!s.Rows.Any(r => r.IsConnected)) return false;
        s.Rows = s.Rows.Select(InvalidateForResume).ToArray();
        return true;
    }

    internal void Poll()
    {
        if (disposed || suspended) return;
        var settings = RingConfig.Current;
        long now = Environment.TickCount64;
        foreach (var s in slots)
        {
            bool enabled = s.Provider.Enabled(settings);
            if (enabled != s.Enabled)
            {
                s.Enabled = enabled;
                s.Generation++;
                s.DueAt = enabled && !s.EverEnabled ? created + InitialDelayMs(s.Provider.Id) : 0;
                s.EverEnabled |= enabled;
                s.LastFinishedAt = null;
                s.StablePolls = 0;
                s.Failures = 0;
                s.Error = null;
                s.Flight?.Cancel();
                if (!enabled && s.Rows.Count > 0) { s.Rows = []; changed(); }
                Publish(s);
            }
            if (!enabled) continue;
            var period = s.Provider.Interval(settings);
            if (period != s.Period)
            {
                s.Period = period;
                if (s.Flight == null && s.DueAt != 0 && s.LastFinishedAt is long completed)
                    s.DueAt = NextDue(s, completed);
            }
            if (s.Flight == null && now >= s.DueAt) _ = Run(s);
        }
        ScheduleNextPoll();
    }

    private static long NextDue(Slot s, long finishedAt)
    {
        var interval = AdaptiveInterval(s.Provider.Id, s.Period, s.StablePolls);
        return finishedAt + (long)NextDelay(interval, s.Failures, s.Rows.Any(r => r.IsConnected)).TotalMilliseconds;
    }

    private void ScheduleNextPoll()
    {
        if (disposed || suspended) return;
        long next = long.MaxValue;
        foreach (var s in slots)
            if (s.Enabled && s.Flight == null) next = Math.Min(next, s.DueAt);

        if (next == long.MaxValue)
        {
            timer.Stop();
            return;
        }
        if (timer.IsEnabled && scheduledAt == next) return;
        timer.Stop();
        scheduledAt = next;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, next - Environment.TickCount64));
        timer.Start();
    }
    internal static TimeSpan AdaptiveInterval(string id,TimeSpan period,int stablePolls)
        => (id is "ble" or "asus" or "astro" or "controller-hid" or "gamesir-hid")&&stablePolls>=3?TimeSpan.FromSeconds(Math.Min(id is "controller-hid" or "gamesir-hid"?15:120,period.TotalSeconds*2)):period;
    internal static bool SameReadings(IReadOnlyList<TaskbarBattery> a,IReadOnlyList<TaskbarBattery> b){
        if(a.Count!=b.Count)return false;
        for(int i=0;i<a.Count;i++)if(!TaskbarBattery.SamePresentation(a[i],b[i]))return false;
        return true;
    }
    private async Task Run(Slot s)
    {
        int generation=s.Generation;using var cts=new CancellationTokenSource(s.Provider.Timeout);
        s.RefreshAfterFlight=false;
        s.Flight=cts;long started=System.Diagnostics.Stopwatch.GetTimestamp();Publish(s);
        Task<IReadOnlyList<TaskbarBattery>>? work=null;
        try{
            work=s.Provider.ReadAsync(cts.Token);
            var rows=await work.WaitAsync(cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if(disposed||generation!=s.Generation)return;
            s.StablePolls=SameReadings(s.Rows,rows)?Math.Min(100,s.StablePolls+1):0;
            s.Failures=0;s.Error=null;s.Updated=DateTimeOffset.UtcNow;if(!s.Rows.SequenceEqual(rows)){s.Rows=rows;changed();}
        }catch(OperationCanceledException){if(!disposed&&generation==s.Generation){s.Failures=Math.Min(s.Failures+1,100);s.Error="Scan timed out";}}
        catch(Exception ex){if(!disposed&&generation==s.Generation){s.Failures=Math.Min(s.Failures+1,100);s.Error=ex.Message;DiagnosticLogger.LogWarning(s.Provider.Id+" scan: "+ex.Message);}}
        finally{
            bool lingering=work!=null&&!work.IsCompleted;
            if(s.Failures>0)s.StablePolls=0;
            if(!disposed&&generation==s.Generation&&(s.Failures>=2||lingering&&s.Failures>0)&&InvalidateConnectedRows(s))
                changed();
            s.LastMs=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            // A native I/O call may ignore cancellation. Publish the timeout
            // immediately but never start overlapping calls on that provider.
            if(lingering){Publish(s);try{await work!;}catch{}}
            s.Flight=null;Publish(s);
            if(!disposed&&generation==s.Generation)
            {
                long now = Environment.TickCount64;
                s.LastFinishedAt = now;
                s.DueAt = s.RefreshAfterFlight ? 0 : NextDue(s, now);
            }
            ScheduleNextPoll();
        }
    }
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;foreach(var s in slots)s.Flight?.Cancel();}
}
