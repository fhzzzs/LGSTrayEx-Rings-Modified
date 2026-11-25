using System;
using System.Linq;
using System.Collections.Generic;
namespace LGSTrayUI;
internal static class ControllerIdentity
{
    internal static string Clean(string? value)=>(value??"").Replace("\0","");
    internal static bool IsG7(ushort vid,ushort pid)=>vid==0x3537&&pid is 0x10A7 or 0x10A8 or 0x10A9 or 0x109B or 0x109C or 0x10BA or 0x10BB or 0x1003 or 0x105D or 0x105E or 0x106A or 0x106B;
    internal static bool IsCyclone2(ushort vid,ushort pid)=>vid==0x3537&&pid is 0x100B or 0x102A or 0x101D or 0x1053;
    internal static bool IsG7(TaskbarBattery d)=>ControllerAliasMatcher.IsGip(d)&&IsG7(d.HardwareVendorId,d.HardwareProductId);
    internal static string? KnownName(ushort vid,ushort pid)=>(vid,pid) switch{
        (0x3537,_) when IsG7(vid,pid)=>"GameSir G7 Pro",
        (0x3537,_) when IsCyclone2(vid,pid)=>"GameSir Cyclone 2",
        (0x054C,0x0CE6)=>"Sony DualSense",
        (0x054C,0x0DF2)=>"Sony DualSense Edge",
        (0x054C,0x05C4) or (0x054C,0x09CC) or (0x054C,0x05C5)=>"Sony DualShock 4",
        (0x057E,0x2006)=>"Nintendo Joy-Con (L)",
        (0x057E,0x2007)=>"Nintendo Joy-Con (R)",
        (0x057E,0x2009)=>"Nintendo Switch Pro Controller",
        (0x2DC8,0x3820)=>"8BitDo N30 Pro",
        (0x2DC8,0x6003) or (0x2DC8,0x6006)=>"8BitDo Pro 2",
        (0x2DC8,0x6009)=>"8BitDo Pro 3",
        (0x2DC8,0x6000) or (0x2DC8,0x6100)=>"8BitDo SF30 Pro",
        (0x2DC8,0x6001) or (0x2DC8,0x6101)=>"8BitDo SN30 Pro",
        _=>null
    };
    internal static string Name(ushort vid,ushort pid,string fallback)=>KnownName(vid,pid)??Clean(fallback);
    internal static bool RawControllerAllowed(ushort vid,ushort pid)=>KnownName(vid,pid)!=null;

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
