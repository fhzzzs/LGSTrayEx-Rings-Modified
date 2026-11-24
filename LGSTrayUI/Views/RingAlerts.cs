using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace LGSTrayUI;
internal sealed class RingAlerts
{
    private readonly Dictionary<string,(int threshold,DateTimeOffset time)> history=new();
    private readonly HashSet<string> muted=new();
    internal static IEnumerable<(int p,int minutes)> Parse(string rules){foreach(var item in rules.Split(',',StringSplitOptions.RemoveEmptyEntries)){var a=item.Split(':');if(a.Length==2&&int.TryParse(a[0],out int p)&&int.TryParse(a[1],out int m)&&p>=1&&p<=100&&m>=0&&m<=1440)yield return(p,m);}}
    internal void Check(IEnumerable<TaskbarBattery> rows){var c=RingConfig.Current;if(!c.Alerts){history.Clear();return;}foreach(var d in rows){if(d.Offline||d.Charging||d.Level is not int n){history.Remove(d.Key);if(d.Charging)muted.Remove(d.Key);continue;}if(muted.Contains(d.Key))continue;var rules=Parse(c.DeviceRules.TryGetValue(d.Key,out var r)?r:c.Rules).OrderBy(x=>x.p).ToArray();var rule=rules.FirstOrDefault(x=>n<=x.p);if(rule.p==0){if(!history.TryGetValue(d.Key,out var past)||n>=past.threshold+2)history.Remove(d.Key);continue;}var now=DateTimeOffset.UtcNow;if(history.TryGetValue(d.Key,out var old)&&!(rule.p<old.threshold)&&!(rule.minutes>0&&(now-old.time).TotalMinutes>=rule.minutes))continue;history[d.Key]=(rule.p,now);Show(d,rule.p);}}
    private void Show(TaskbarBattery d,int threshold)
    {
        if(RingConfig.Current.Sound)RingSounds.Play(RingConfig.Current.Audio(d.Key,threshold));
        PrimaryNotificationPopup.Show(d.Name+" · "+d.Percent,"电量不足，请及时充电。","#20242C",
            TimeSpan.FromSeconds(8),"本次充电前不再提醒",()=>muted.Add(d.Key));
    }
}
