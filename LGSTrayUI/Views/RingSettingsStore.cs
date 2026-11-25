using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// A single atomic preference transaction avoids partially updated identity,
// notification and layout files. Existing flat JSON remains importable.
internal static class RingSettingsStore
{
    internal const int Version=2;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static RingPreferences Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new FormatException("配置必须为 JSON 对象。");
        if(doc.RootElement.TryGetProperty("SchemaVersion",out var schema)&&(!schema.TryGetInt32(out var v)||v>Version||v<0))
            throw new FormatException("不支持的配置版本。");
        var c=JsonSerializer.Deserialize<RingPreferences>(json)??throw new FormatException("配置为空。");
        // Null containers from imports must never reach the identity or UI engines.
        c.AutomaticAliases??=new();c.DeviceAliases??=new();c.PreferredSources??=new();
        c.ModelGroupRoots??=[];c.IgnoredDevices??=[];c.SeparateDeviceSources??=[];
        c.Order??=[];c.Hidden??=[];c.Names??=new();c.DeviceRules??=new();c.DeviceColors??=new();
        c.DevicePalette??=new();c.AlertSounds??=new();
        foreach(var key in c.DevicePalette.Where(x=>x.Value==null).Select(x=>x.Key).ToArray())c.DevicePalette.Remove(key);
        foreach(var key in c.AlertSounds.Where(x=>x.Value==null).Select(x=>x.Key).ToArray())c.AlertSounds.Remove(key);
        foreach(var map in c.AlertSounds.Values)foreach(var key in map.Where(x=>x.Value==null).Select(x=>x.Key).ToArray())map.Remove(key);
        c.Rules??="20:30,10:15,5:5";
        c.SchemaVersion=Version;
        c.NotificationWidth=Math.Clamp(c.NotificationWidth,240,420);c.NotificationSeconds=Math.Clamp(c.NotificationSeconds,2,15);
        c.NotificationOpacity=Math.Clamp(c.NotificationOpacity,60,100);
        if(c.NotificationCorner is not ("BottomRight" or "BottomLeft" or "TopRight" or "TopLeft"))c.NotificationCorner="BottomRight";
        if(c.NotificationMonitor is not ("Primary" or "Active"))c.NotificationMonitor="Primary";
        return c;
    }
    internal static RingPreferences Load(string path)
    {
        foreach(var file in new[]{path,path+".bak"}){
            if(!File.Exists(file))continue;
            try{return Parse(File.ReadAllText(file));}
            catch(Exception ex){DiagnosticLogger.LogWarning("Settings load "+Path.GetFileName(file)+": "+ex.Message);}
        }
        return new();
    }
    internal static void Save(string path,RingPreferences c)
    {
        c.SchemaVersion=Version;
        WriteAtomic(path,JsonSerializer.Serialize(c,Json));
    }
    internal static void WriteAtomic(string path,string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            File.WriteAllText(temp,json);
            if(File.Exists(path)){
                // Do not overwrite a good backup with a damaged/unreadable JSON file.
                bool valid=false;
                try{using var doc=JsonDocument.Parse(File.ReadAllText(path));valid=doc.RootElement.ValueKind==JsonValueKind.Object||doc.RootElement.ValueKind==JsonValueKind.Array;}catch{}
                if(valid)File.Replace(temp,path,path+".bak",true);
                else File.Move(temp,path,true);
            }else File.Move(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
