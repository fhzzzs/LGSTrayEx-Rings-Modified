using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;
internal sealed class RingAlerts
{
    private readonly Dictionary<string,(int threshold,DateTimeOffset time)> history=new();
    private readonly HashSet<string> muted=new();
    private readonly Dictionary<string,bool> full=new();
    internal static IEnumerable<(int p,int minutes)> Parse(string rules){foreach(var item in rules.Split(',',StringSplitOptions.RemoveEmptyEntries)){var a=item.Split(':');if(a.Length==2&&int.TryParse(a[0],out int p)&&int.TryParse(a[1],out int m)&&p>=1&&p<=100&&m>=0&&m<=1440)yield return(p,m);}}
    internal void Check(IEnumerable<TaskbarBattery> rows,bool suppressed=false,ISet<string>? deferred=null)
    {
        var c=RingConfig.Current;var now=DateTimeOffset.UtcNow;
        bool allowed=!suppressed&&RingNotificationPolicy.CanNotify(c);
        if(!c.Alerts)history.Clear();
        foreach(var d in rows){
            if(!d.IsConnected||d.Stale||d.Level is not int n)continue;
            bool isFull=n>=100&&(d.ChargeState is BatteryChargeState.Charging or BatteryChargeState.Full);
            if(full.TryGetValue(d.Key,out var wasFull)&&!wasFull&&isFull&&c.FullChargeAlerts&&allowed)
                PrimaryNotificationPopup.Show(d.Name,"充电完成","#237344",TimeSpan.FromSeconds(c.NotificationSeconds),deviceKey:d.Key);
            full[d.Key]=isFull;
            if(d.Charging||isFull){history.Remove(d.Key);muted.Remove(d.Key);continue;}
            if(!c.Alerts||!allowed||muted.Contains(d.Key)||deferred?.Contains(d.Key)==true)continue;
            var rules=Parse(c.DeviceRules.TryGetValue(d.Key,out var r)?r:c.Rules).OrderBy(x=>x.p);
            var rule=rules.FirstOrDefault(x=>n<=x.p);
            if(rule.p==0){if(!history.TryGetValue(d.Key,out var past)||n>=past.threshold+2)history.Remove(d.Key);continue;}
            if(!RingNotificationPolicy.ShouldNotifyLow(history.TryGetValue(d.Key,out var old)?old:null,rule.p,rule.minutes,now))continue;
            history[d.Key]=(rule.p,now);
            if(c.Sound)RingSounds.Play(c.Audio(d.Key,rule.p));
            PrimaryNotificationPopup.Show(d.Name+" · "+d.Percent,"电量不足，请及时充电。","#9A520C",
                TimeSpan.FromSeconds(Math.Max(8,c.NotificationSeconds)),"本次充电前不再提醒",()=>muted.Add(d.Key),deviceKey:d.Key);
        }
    }
}
