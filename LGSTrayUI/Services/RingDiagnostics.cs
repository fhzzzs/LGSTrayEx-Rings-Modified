using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal sealed record ProviderDiagnostic(string Id,bool Enabled,bool Busy,int Failures,double LastMs,int Count,DateTimeOffset Updated,string? Error);
internal static class RingDiagnostics
{
    private static readonly ConcurrentDictionary<string,ProviderDiagnostic> providers=new(StringComparer.Ordinal);
    private static DeviceGroup[] groups=[];
    private static readonly ConcurrentDictionary<string,string> selected=new(StringComparer.Ordinal);
    internal static void Provider(ProviderDiagnostic value)=>providers[value.Id]=value;
    internal static void Observe(DeviceGroup[] snapshot){groups=snapshot;var keys=snapshot.Select(g=>g.Reading.Key).ToHashSet(StringComparer.Ordinal);foreach(var k in selected.Keys)if(!keys.Contains(k))selected.TryRemove(k,out _);}
    internal static void Source(string root,string key)=>selected[root]=key;
    internal static string Report(DateTimeOffset now){
        var b=new StringBuilder();
        b.AppendLine("Battery Monitor · Rings "+RingsUpdateService.Current);
        b.AppendLine("Windows "+Environment.OSVersion.Version+" · "+System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        using(var p=Process.GetCurrentProcess())b.AppendLine($"Working set: {p.WorkingSet64/1048576d:F1} MiB · Threads: {p.Threads.Count}");
        b.AppendLine($"Animation channels: {RingFrameClock.ActiveCount} · Devices: {groups.Length}");
        b.AppendLine($"Diagnostic log: {AppDataPaths.LocalAppDataDir}\\diagnostic.log");
        b.AppendLine();b.AppendLine("Providers:");
        foreach(var p in providers.Values.OrderBy(x=>x.Id))
            b.AppendLine($"  {p.Id}: {(p.Enabled?(p.Busy?"scanning":"idle"):"disabled")} · {p.Count} sources · last {p.LastMs:F0} ms · failures {p.Failures}"+(p.Error==null?"":" · "+p.Error));
        b.AppendLine();b.AppendLine("Logical devices:");
        foreach(var g in groups){
            var r=g.Reading;
            b.AppendLine($"{r.Name} · {r.Percent} · {r.Connection} · {r.ChargeState} · {r.Quality}");
            b.AppendLine($"  ID: {r.Key} · {(g.Inferred?"inferred / reversible":g.Sources.Length>1?"linked / identity evidence":"single source")}");
            foreach(var s in g.Sources){
                string chosen=selected.TryGetValue(r.Key,out var key)&&key==s.Key?"[selected] ":"";
                string age=s.Updated==DateTimeOffset.MinValue?"never":Math.Max(0,(now-s.Updated).TotalSeconds).ToString("F0")+"s ago";
                b.AppendLine($"  {chosen}{DeviceIdentityManager.Source(s.Key)} · {s.Percent} · {s.Connection} · {age}");
                b.AppendLine("    "+s.Key);
            }
        }
        return b.ToString();
    }
}
