using System;
using System.Collections.Generic;
using System.Linq;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// XInput slots are not hardware identities. The Xbox 360 compatibility view
// exposed by WGI and a single USB XInput slot are a useful, reversible hint,
// not serial-number proof. Never persist an automatic live slot binding.
internal static class ControllerAliasMatcher
{
    internal static bool IsGip(TaskbarBattery d)=>d.Type==DeviceType.Gamepad&&d.Key.StartsWith("gip:",StringComparison.Ordinal);
    internal static bool IsXInput(TaskbarBattery d)=>d.Type==DeviceType.Gamepad&&d.Key.StartsWith("xinput:",StringComparison.Ordinal);
    private static bool Xbox360(TaskbarBattery d)=>IsGip(d)&&string.Equals(d.Name.Trim(),"Xbox 360 Controller for Windows",StringComparison.OrdinalIgnoreCase);
    internal static void Apply(TaskbarBattery[] raw,RingPreferences c,Dictionary<string,string> roots,HashSet<string> inferred)
    {
        if(!c.AutoDeduplicate)return;
        var gip=raw.Where(IsGip).ToArray();
        var slots=raw.Where(IsXInput).ToArray();
        var activeGip=gip.Where(d=>d.IsConnected).ToArray();
        var activeSlots=slots.Where(d=>d.IsConnected).ToArray();
        bool CanPair(TaskbarBattery g,TaskbarBattery x)
            =>Xbox360(g)&&!c.SeparateDeviceSources.Contains(g.Key)&&!c.SeparateDeviceSources.Contains(x.Key)
                &&roots[g.Key]==g.Key&&roots[x.Key]==x.Key
                &&!c.DeviceAliases.ContainsKey(g.Key)&&!c.DeviceAliases.ContainsKey(x.Key)
                &&!roots.Any(p=>p.Key!=x.Key&&p.Value==x.Key)
                &&(string.IsNullOrWhiteSpace(g.PhysicalId)||string.IsNullOrWhiteSpace(x.PhysicalId)||g.PhysicalId==x.PhysicalId);
        void Pair(TaskbarBattery g,TaskbarBattery x)
        {
            roots[x.Key]=g.Key;
            inferred.Add(g.Key);
        }
        if(activeGip.Length==1&&activeSlots.Length==1){
            var g=activeGip[0];var x=activeSlots[0];
            if(x.ShortLabel=="USB"&&g.Level is >=0 and <=100&&CanPair(g,x)){
                Pair(g,x);
                // Only used to keep an inactive historical placeholder attached.
                // A live slot must always be checked anew.
                c.ControllerHistoryHints[x.Key]=g.Key;
            }
        }
        foreach(var x in slots.Where(d=>d.Offline)){
            if(!c.ControllerHistoryHints.TryGetValue(x.Key,out var key))continue;
            var g=gip.FirstOrDefault(d=>d.Key==key);
            if(g!=null&&CanPair(g,x))Pair(g,x);
        }
    }
}
