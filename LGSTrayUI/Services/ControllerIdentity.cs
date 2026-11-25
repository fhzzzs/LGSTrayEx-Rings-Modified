using System;
using System.Linq;
using System.Collections.Generic;
namespace LGSTrayUI;
internal static class ControllerIdentity
{
    internal static string Clean(string? value)=>(value??"").Replace("\0","");
    internal static bool IsG7(ushort vid,ushort pid)=>vid==0x3537&&pid==0x10A9;
    internal static bool IsG7(TaskbarBattery d)=>ControllerAliasMatcher.IsGip(d)&&IsG7(d.HardwareVendorId,d.HardwareProductId);
    internal static string Name(ushort vid,ushort pid,string fallback)=>IsG7(vid,pid)?"GameSir G7 Pro":Clean(fallback);
    internal static TaskbarBattery Normalize(TaskbarBattery row)=>row with{Key=Clean(row.Key),Name=Name(row.HardwareVendorId,row.HardwareProductId,row.Name),PhysicalId=row.PhysicalId==null?null:Clean(row.PhysicalId)};
    internal static void Migrate(RingPreferences c){
        void Map<T>(Dictionary<string,T> map,Func<T,T> value){
            foreach(var old in map.Keys.ToArray()){
                string key=Clean(old);T v=value(map[old]);
                if(old!=key){if(key.Length>0&&!map.ContainsKey(key))map[key]=v;map.Remove(old);}
                else map[key]=v;
            }
        }
        foreach(var map in new[]{c.PhysicalSourceIds,c.PhysicalSourceEvidence,c.PhysicalEvidenceIds,c.ControllerHistoryHints,c.DeviceAliases,c.AutomaticAliases,c.PreferredSources})Map(map,Clean);
        Map(c.Names,Clean);Map(c.DeviceRules,x=>x);Map(c.DeviceColors,x=>x);Map(c.DevicePalette,x=>x);Map(c.AlertSounds,x=>x);
        foreach(var list in new[]{c.Order,c.Hidden,c.IgnoredDevices,c.SeparateDeviceSources,c.ModelGroupRoots}){
            var cleaned=list.Select(Clean).Where(x=>x.Length>0).Distinct(StringComparer.Ordinal).ToArray();list.Clear();list.AddRange(cleaned);
        }
    }
}
