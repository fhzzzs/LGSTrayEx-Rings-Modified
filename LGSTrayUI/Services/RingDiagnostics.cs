using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal sealed record ProviderDiagnostic(string Id,bool Enabled,bool Busy,int Failures,double LastMs,int Count,DateTimeOffset Updated,string? Error);
internal static class RingDiagnostics
{
    private static readonly ConcurrentDictionary<string,ProviderDiagnostic> providers=new(StringComparer.Ordinal);
    private static DeviceGroup[] groups=[];
    private static long startupMs=-1;
    internal static void Startup(long elapsedMs)=>startupMs=elapsedMs;
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process,uint flags);
    private static readonly ConcurrentDictionary<string,string> selected=new(StringComparer.Ordinal);
    internal static void Provider(ProviderDiagnostic value)=>providers[value.Id]=value;
    private static readonly BatteryHistory history=new(256);
    internal static string HardwareInventory {get;set;}="Not collected; use hardware discovery in diagnostics.";
    internal static void Observe(DeviceGroup[] snapshot){
        if(groups.Length!=snapshot.Length||!groups.Select(g=>g.Reading.Key).SequenceEqual(snapshot.Select(g=>g.Reading.Key))){
            var keys=snapshot.Select(g=>g.Reading.Key).ToHashSet(StringComparer.Ordinal);foreach(var k in selected.Keys)if(!keys.Contains(k))selected.TryRemove(k,out _);
        }
        groups=snapshot;history.Observe(snapshot,DateTimeOffset.UtcNow);
    }
    internal static void Source(string root,string key)=>selected[root]=key;
    internal static string Report(DateTimeOffset now){
        var b=new StringBuilder();
        b.AppendLine("Battery Monitor · v"+RingsUpdateService.Current);
        b.AppendLine("Captured: "+now.ToString("O"));
        b.AppendLine("Protocol catalog: "+PeripheralCatalog.Revision+" · "+PeripheralCatalog.Asus.Count+" ASUS transport profiles");
        b.AppendLine("Windows "+Environment.OSVersion.Version+" · "+System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        using(var p=Process.GetCurrentProcess()){
            b.AppendLine($"Working set: {p.WorkingSet64/1048576d:F1} MiB · Private: {p.PrivateMemorySize64/1048576d:F1} MiB · Threads: {p.Threads.Count} · Handles: {p.HandleCount}");
            b.AppendLine($"GDI objects: {GetGuiResources(p.Handle,0)} · USER objects: {GetGuiResources(p.Handle,1)} · CPU time: {p.TotalProcessorTime.TotalSeconds:F1}s");
            b.AppendLine($"Uptime: {Math.Max(0,(now-p.StartTime.ToUniversalTime()).TotalSeconds):F0}s · Peak working set: {p.PeakWorkingSet64/1048576d:F1} MiB");
        }
        if(startupMs>=0)b.AppendLine($"Startup to host ready: {startupMs} ms");
        b.AppendLine($"Animation channels: {RingFrameClock.ActiveCount} · Devices: {groups.Length}");
        b.AppendLine($"Diagnostic log: {AppDataPaths.LocalAppDataDir}\\diagnostic.log");
        b.AppendLine();b.AppendLine("Providers:");
        foreach(var p in providers.Values.OrderBy(x=>x.Id))
            b.AppendLine($"  {p.Id}: {(p.Enabled?(p.Busy?"scanning":"idle"):"disabled")} · {p.Count} sources · last {p.LastMs:F0} ms · failures {p.Failures}"+(p.Error==null?"":" · "+p.Error));
        b.AppendLine("  ASUS OMNI: "+AsusBatteryReader.Diagnostics);
        b.AppendLine("  GIP battery: "+GipBatteryReader.Diagnostics);
        b.AppendLine();b.AppendLine("Hardware discovery (on demand):");b.AppendLine(HardwareInventory);
        b.AppendLine();b.AppendLine("Logical devices:");
        foreach(var g in groups){
            var r=g.Reading;
            b.AppendLine($"{r.Name} · {r.Percent} · {r.Connection} · {r.ChargeState} · {r.Quality}");
            b.AppendLine($"  ID: {r.Key} · {(g.Inferred?"inferred / reversible":g.Sources.Length>1?"linked / identity evidence":"single source")}");
            var exact=g.Sources.Where(s=>s.IsConnected&&SourceReading.Normalize(s,now,RingConfig.Current).Quality==ReadingQuality.Exact&&s.Level.HasValue).Select(s=>s.Level!.Value).ToArray();
            if(exact.Length>1&&exact.Max()-exact.Min()>=15)b.AppendLine($"  Source disagreement: {exact.Min()}–{exact.Max()}%; selected source retained (no averaging).");
            foreach(var s in g.Sources){
                string chosen=selected.TryGetValue(r.Key,out var key)&&key==s.Key?"[selected] ":"";
                string age=s.Updated==DateTimeOffset.MinValue?"never":Math.Max(0,(now-s.Updated).TotalSeconds).ToString("F0")+"s ago";
                b.AppendLine($"  {chosen}{DeviceIdentityManager.Source(s.Key)} · {s.Percent} · {s.Connection} · {age}");
                b.AppendLine("    "+s.Key);
                if(s.HardwareVendorId!=0||s.HardwareProductId!=0)b.AppendLine($"    VID:PID {s.HardwareVendorId:X4}:{s.HardwareProductId:X4}");
                if(!string.IsNullOrWhiteSpace(s.PhysicalId))b.AppendLine("    Physical ID: "+s.PhysicalId);
            }
        }
        b.AppendLine();
        b.AppendLine("Recent battery changes (session only):");
        foreach(var change in history.Snapshot().TakeLast(20))b.AppendLine($"  {change.Time:O} · {change.Name} · {change.Value}");
        b.AppendLine();
        b.AppendLine($"END OF REPORT · {groups.Length} logical devices · {groups.Sum(g=>g.Sources.Length)} sources");
        return ControllerIdentity.Clean(b.ToString());
    }
}
