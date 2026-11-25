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
    private static bool GenericXbox(TaskbarBattery d)=>IsGip(d)&&(string.Equals(d.Name.Trim(),"HID-compliant game controller",StringComparison.OrdinalIgnoreCase)||string.Equals(d.Name.Trim(),"Xbox Wireless Controller",StringComparison.OrdinalIgnoreCase));
    private static bool SupportedPair(TaskbarBattery g,TaskbarBattery x)=>ControllerIdentity.IsG7(g)||(Xbox360(g)||GenericXbox(g))&&(x.ShortLabel=="USB"||x.ShortLabel=="—");

    internal static bool IsXboxBle(TaskbarBattery d)=>d.Type==DeviceType.Gamepad&&d.Key.StartsWith("ble:",StringComparison.Ordinal)&&d.Name.Equals("Xbox Wireless Controller",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(d.PhysicalId);
    internal static void Apply(TaskbarBattery[] raw,RingPreferences c,Dictionary<string,string> roots,HashSet<string> inferred)
    {
        if(!c.AutoDeduplicate)return;
        var gip=raw.Where(IsGip).ToArray();
        var slots=raw.Where(IsXInput).ToArray();
        var activeGip=gip.Where(d=>d.IsConnected).ToArray();
        // A re-used XInput slot may not inherit another connected unit's old history.
        foreach(var slot in slots.Where(d=>d.IsConnected))if(activeGip.Length>1)c.ControllerHistoryHints.Remove(slot.Key);
        var activeSlots=slots.Where(d=>d.IsConnected).ToArray();
        bool CanPair(TaskbarBattery g,TaskbarBattery x)
            => (Xbox360(g)||GenericXbox(g)||ControllerIdentity.IsG7(g))&&!c.SeparateDeviceSources.Contains(g.Key)&&!c.SeparateDeviceSources.Contains(x.Key)
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
            if(SupportedPair(g,x)&&(ControllerIdentity.IsG7(g)||g.Level is >=0 and <=100)&&CanPair(g,x)){
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
        // A model-based history is only a reversible display hint, not serial proof.
        // Once two instances are seen concurrently, never guess for this model again.
        const string g7Hint="model:3537:10A9";
        var g7=gip.Where(ControllerIdentity.IsG7).ToArray();
        if(g7.Count(d=>d.IsConnected)>1)c.ControllerHistoryHints[g7Hint]="ambiguous";
        if(g7.Length>1&&c.ControllerHistoryHints.GetValueOrDefault(g7Hint)!="ambiguous"&&
           g7.Count(d=>d.IsConnected)<=1&&g7.Select(d=>d.PhysicalId).Where(v=>!string.IsNullOrWhiteSpace(v)).Distinct().Count()<=1&&
           g7.All(d=>roots[d.Key]==d.Key&&!c.SeparateDeviceSources.Contains(d.Key)&&!c.DeviceAliases.ContainsKey(d.Key))){
            string root=c.ControllerHistoryHints.GetValueOrDefault(g7Hint)??"";
            if(!g7.Any(d=>d.Key==root))root=g7.OrderByDescending(d=>c.PhysicalSourceIds.ContainsKey(d.Key)).ThenBy(d=>d.Updated).ThenBy(d=>d.Key,StringComparer.Ordinal).First().Key;
            c.ControllerHistoryHints[g7Hint]=root;
            var members=g7.Select(d=>d.Key).ToHashSet(StringComparer.Ordinal);
            foreach(var key in roots.Where(p=>members.Contains(p.Value)).Select(p=>p.Key).ToArray())roots[key]=root;
            foreach(var old in members)inferred.Remove(old);
            inferred.Add(root);
        }else if(g7.Length==1&&!c.ControllerHistoryHints.ContainsKey(g7Hint))c.ControllerHistoryHints[g7Hint]=g7[0].Key;
        // A unique Microsoft WGI controller and a unique connected Xbox BLE
        // identity can provisionally share one display. VID is not a serial;
        // never guess when multiple candidates exist or explicit links conflict.
        var bluetooth=raw.Where(IsXboxBle).ToArray();
        var activeBle=bluetooth.Where(d=>d.IsConnected).ToArray();
        bool Free(TaskbarBattery d)=>!c.SeparateDeviceSources.Contains(d.Key)&&!c.DeviceAliases.ContainsKey(d.Key)&&roots[d.Key]==d.Key;
        bool Eligible(TaskbarBattery g,TaskbarBattery ble)=>g.HardwareVendorId==0x045e&&(GenericXbox(g)||Xbox360(g))&&Free(g)&&Free(ble)&&
            (string.IsNullOrEmpty(g.PhysicalId)||g.PhysicalId==ble.PhysicalId);
        void Link(TaskbarBattery g,TaskbarBattery ble){
            foreach(var source in roots.Where(p=>p.Value==g.Key).Select(p=>p.Key).ToArray())roots[source]=ble.Key;
            inferred.Remove(g.Key);inferred.Add(ble.Key);
        }
        if(activeGip.Length==1&&activeBle.Length==1&&activeSlots.Length<=1&&
           raw.Count(d=>d.Type==DeviceType.Gamepad&&d.IsConnected&&d.Key.StartsWith("ble:",StringComparison.Ordinal))==1){
            var g=activeGip[0];var ble=activeBle[0];
            if(Eligible(g,ble)){
                Link(g,ble);
                c.ControllerHistoryHints[g.Key]=ble.Key;
            }
        }
        foreach(var g in gip.Where(d=>d.Offline)){
            if(!c.ControllerHistoryHints.TryGetValue(g.Key,out var key))continue;
            var ble=bluetooth.FirstOrDefault(d=>d.Key==key);
            if(ble!=null&&Eligible(g,ble))Link(g,ble);
        }
    }
}
