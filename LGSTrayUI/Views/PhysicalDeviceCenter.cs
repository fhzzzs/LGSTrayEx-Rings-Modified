using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

internal sealed record PhysicalDeviceSnapshot(DeviceGroup[] Groups,TaskbarBattery[] Rows,ConnectionChange[] Changes,bool Suppressed);

// Own the raw inventory, source selection and confirmed lifecycle independently
// of the WPF window. Views consume the same immutable-by-convention snapshot.
internal sealed class PhysicalDeviceCenter
{
    internal Dictionary<string,TaskbarBattery> Inventory {get;}=new(StringComparer.Ordinal);
    private readonly BatterySourceSelector selector=new();
    private readonly GroupedConnectionTracker connections;
    private DeviceGroup[] rawGroups=[];
    internal PhysicalDeviceCenter(DateTimeOffset now){connections=new(now);}
    internal void Suppress(DateTimeOffset now,TimeSpan duration)=>connections.Suppress(now,duration);
    internal void Regroup(RingPreferences c){rawGroups=DeviceIdentityManager.Group(Inventory.Values,c);}
    internal PhysicalDeviceSnapshot Advance(RingPreferences c,DateTimeOffset now)
    {
        var selected=selector.Select(rawGroups,c,now);
        var snapshot=connections.ObserveSnapshot(selected,now);
        bool gipActive=Inventory.Values.Any(d=>!d.Offline&&d.Key.StartsWith("gip:",StringComparison.Ordinal));
        var rows=snapshot.Groups.Where(g=>!g.Ignored&&!(c.AutoDeduplicate&&gipActive&&g.Sources.All(d=>d.Offline&&d.Key.StartsWith("xinput:",StringComparison.Ordinal))))
            .Select(g=>g.Reading).Select(d=>c.Names.TryGetValue(d.Key,out var name)&&!string.IsNullOrWhiteSpace(name)?d with{Name=name}:d).ToArray();
        return new(snapshot.Groups,rows,snapshot.Changes.Where(x=>!snapshot.Groups.Any(g=>g.Ignored&&g.Reading.Key==x.Device.Key)).ToArray(),snapshot.Suppressed);
    }
}
