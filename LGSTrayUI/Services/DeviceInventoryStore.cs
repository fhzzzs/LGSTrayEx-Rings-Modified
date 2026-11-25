using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal static class DeviceInventoryStore
{
    private sealed record KnownDevice(DeviceType Type,string Name,string Key,string? PhysicalId=null,DateTimeOffset Updated=default);
    internal static TaskbarBattery[] Load(string path)
    {
        foreach(var file in new[]{path,path+".bak"}){
            if(!File.Exists(file))continue;
            try{
                return (JsonSerializer.Deserialize<KnownDevice[]>(File.ReadAllText(file))??[])
                    .Where(x=>x!=null&&!string.IsNullOrWhiteSpace(x.Key)&&TaskbarBattery.Rank(x.Type)<4)
                    .Select(x=>new TaskbarBattery(x.Type,x.Name??x.Key,null,"离线",false,x.Updated,x.Key,PhysicalId:x.PhysicalId)).ToArray();
            }catch(Exception ex){DiagnosticLogger.LogWarning("Device inventory: "+ex.Message);}
        }
        return [];
    }
    internal static void Save(string path,IEnumerable<TaskbarBattery> rows)
        =>RingSettingsStore.WriteAtomic(path,JsonSerializer.Serialize(rows.Select(d=>new KnownDevice(d.Type,d.Name,d.Key,d.PhysicalId,d.Updated)).ToArray()));
}
