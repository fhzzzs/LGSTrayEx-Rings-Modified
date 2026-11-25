using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

internal sealed class DeviceNotificationCenter
{
    private readonly RingAlerts alerts=new();
    private readonly Dictionary<string,(bool online,DateTimeOffset at)> last=new(StringComparer.Ordinal);
    internal static bool IsDuplicate((bool online,DateTimeOffset at)? previous,bool online,DateTimeOffset now)
        =>previous is { } p&&p.online==online&&now-p.at<TimeSpan.FromSeconds(5);
    internal void Observe(PhysicalDeviceSnapshot snapshot,DateTimeOffset now)
    {
        var c=RingConfig.Current;
        bool allowed=!snapshot.Suppressed&&RingNotificationPolicy.CanNotify(c);
        foreach(var change in snapshot.Changes){
            var d=change.Device;
            if(IsDuplicate(last.TryGetValue(d.Key,out var previous)?previous:null,change.Online,now))continue;
            last[d.Key]=(change.Online,now);
            if(!allowed||!c.ConnectionAlerts)continue;
            string name=c.Names.TryGetValue(d.Key,out var custom)&&!string.IsNullOrWhiteSpace(custom)?custom:d.Name;
            PrimaryNotificationPopup.Show(name,change.Online?"已连接"+(d.Level is int n?$" · {n}%":""):"已断开连接",
                change.Online?"#356A95":"#9A520C",TimeSpan.FromSeconds(c.NotificationSeconds),deviceKey:d.Key);
        }
        foreach(var key in last.Where(p=>now-p.Value.at>TimeSpan.FromHours(1)).Select(p=>p.Key).ToArray())last.Remove(key);
        alerts.Check(snapshot.Rows,!allowed);
    }
}
