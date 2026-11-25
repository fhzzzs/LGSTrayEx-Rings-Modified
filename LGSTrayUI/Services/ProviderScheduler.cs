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
        internal DateTimeOffset Next;
        internal CancellationTokenSource? Flight;
        internal int Generation,Failures;
        internal bool Enabled;
    }
    private readonly Slot[] slots;
    private readonly DispatcherTimer timer;
    private readonly Action changed;
    private bool disposed;
    internal ProviderScheduler(IEnumerable<IBatteryProvider> providers,Dispatcher dispatcher,Action changed)
    {
        this.changed=changed;slots=providers.Select(p=>new Slot(p)).ToArray();
        timer=new DispatcherTimer(DispatcherPriority.Background,dispatcher){Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=Tick;timer.Start();
    }
    internal IEnumerable<TaskbarBattery> Readings=>slots.SelectMany(s=>s.Rows);
    private void Tick(object? sender,EventArgs e)=>Poll();
    internal void Rescan(string? id=null)
    {
        foreach(var s in slots.Where(s=>id==null||s.Provider.Id==id)){s.Generation++;s.Next=DateTimeOffset.MinValue;s.Flight?.Cancel();}
        Poll();
    }
    internal void Poll()
    {
        if(disposed)return;var c=RingConfig.Current;var now=DateTimeOffset.UtcNow;
        foreach(var s in slots){
            bool enabled=s.Provider.Enabled(c);
            if(enabled!=s.Enabled){s.Enabled=enabled;s.Generation++;s.Next=DateTimeOffset.MinValue;s.Flight?.Cancel();
                if(!enabled&&s.Rows.Count>0){s.Rows=[];changed();}}
            if(enabled&&s.Flight==null&&now>=s.Next)_=Run(s);
        }
    }
    private async Task Run(Slot s)
    {
        int generation=s.Generation;using var cts=new CancellationTokenSource(s.Provider.Timeout);
        s.Flight=cts;
        try{
            var rows=await s.Provider.ReadAsync(cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if(disposed||generation!=s.Generation)return;
            s.Failures=0;if(!s.Rows.SequenceEqual(rows)){s.Rows=rows;changed();}
        }catch(OperationCanceledException){if(!disposed&&generation==s.Generation)s.Failures++;}
        catch(Exception ex){if(!disposed&&generation==s.Generation){s.Failures++;DiagnosticLogger.LogWarning(s.Provider.Id+" scan: "+ex.Message);}}
        finally{
            s.Flight=null;
            if(!disposed)s.Next=generation!=s.Generation?DateTimeOffset.MinValue:DateTimeOffset.UtcNow+
                TimeSpan.FromSeconds(Math.Min(120,s.Provider.Interval(RingConfig.Current).TotalSeconds*Math.Pow(2,Math.Min(4,s.Failures))));
        }
    }
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;foreach(var s in slots)s.Flight?.Cancel();}
}
