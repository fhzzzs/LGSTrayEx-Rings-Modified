using System;
using System.Collections.Generic;
using System.Linq;
namespace LGSTrayUI;
internal static class ConfirmedHistoryRetention
{
    internal static string[] Expired(IEnumerable<TaskbarBattery> inventory,IReadOnlyDictionary<string,TaskbarBattery> current,RingPreferences c,DateTimeOffset now){
        var online=current.Values.Where(d=>d.IsConnected&&d.Updated>=now.AddDays(-1)&&d.Updated<=now.AddSeconds(5)&&!string.IsNullOrWhiteSpace(d.PhysicalId)).Select(d=>(d.Type,d.PhysicalId)).ToHashSet();
        if(online.Count==0)return [];
        // Keep explicit/manual settings, remembered user choices, and
        // unidentified devices. Source ID/evidence preferences remain intact.
        return inventory.Where(d=>d.Offline&&!current.ContainsKey(d.Key)&&d.Updated!=DateTimeOffset.MinValue&&d.Updated<now.AddDays(-30)&&!string.IsNullOrWhiteSpace(d.PhysicalId)&&online.Contains((d.Type,d.PhysicalId))&&
            !c.SeparateDeviceSources.Contains(d.Key)&&!c.DeviceAliases.ContainsKey(d.Key)&&!c.DeviceAliases.ContainsValue(d.Key)&&!c.PreferredSources.ContainsValue(d.Key)&&!c.Names.ContainsKey(d.Key)&&!c.Hidden.Contains(d.Key)&&!c.IgnoredDevices.Contains(d.Key)&&!c.Order.Contains(d.Key)&&!c.DeviceRules.ContainsKey(d.Key)&&!c.DeviceColors.ContainsKey(d.Key)&&!c.DevicePalette.ContainsKey(d.Key)&&!c.AlertSounds.ContainsKey(d.Key))
            .Select(d=>d.Key).ToArray();
    }
}
