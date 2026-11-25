using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

// Logical IDs are independent of provider keys. Evidence is only used to pick
// an ID for an already validated group; it never merges unrelated groups.
internal static class PhysicalIdentityRegistry
{
    internal static bool IsId(string key)=>key.StartsWith("device:",StringComparison.Ordinal);
    private static string NewId()=>"device:"+Guid.NewGuid().ToString("N");
    internal static DeviceGroup[] Assign(DeviceGroup[] groups,RingPreferences c)
    {
        // If a provider reuses a source key for a serial-confirmed different
        // unit, do not inherit the old unit's logical ID from that key.
        foreach(var s in groups.SelectMany(g=>g.Sources).Where(s=>!string.IsNullOrWhiteSpace(s.PhysicalId))){
            string evidence=s.Type+":"+s.PhysicalId;
            if(c.PhysicalSourceEvidence.TryGetValue(s.Key,out var old)&&old!=evidence)
                c.PhysicalSourceIds.Remove(s.Key);
            c.PhysicalSourceEvidence[s.Key]=evidence;
        }
        var used=new HashSet<string>(StringComparer.Ordinal);
        var output=new List<DeviceGroup>();
        // Explicit primary choice wins; otherwise existing primary source wins.
        foreach(var g in groups.OrderByDescending(g=>IsId(g.Reading.Key))
            .ThenByDescending(g=>c.PhysicalSourceIds.ContainsKey(g.Reading.Key))
            .ThenBy(g=>g.Reading.Key,StringComparer.Ordinal))
        {
            string root=g.Reading.Key;
            bool provisionalController=g.Inferred&&root.StartsWith("gip:",StringComparison.Ordinal);
            bool provisionalBluetooth=g.Inferred&&root.StartsWith("ble:",StringComparison.Ordinal)&&g.Sources.Any(ControllerAliasMatcher.IsXboxBle);
            var identitySources=g.Sources.Where(s=>(!provisionalController||!ControllerAliasMatcher.IsXInput(s))&&(!provisionalBluetooth||(!ControllerAliasMatcher.IsXInput(s)&&!ControllerAliasMatcher.IsGip(s)))).ToArray();
            // Do not store inferred G7 model aliases as confirmed physical IDs.
            if(provisionalController&&g.Sources.Any(ControllerIdentity.IsG7))identitySources=identitySources.Where(s=>s.Key==root||!ControllerIdentity.IsG7(s)).ToArray();
            var candidates=identitySources.Select(s=>c.PhysicalSourceIds.GetValueOrDefault(s.Key))
                .Where(s=>s!=null).Distinct().ToArray();
            var evidence=identitySources.Where(s=>!string.IsNullOrWhiteSpace(s.PhysicalId))
                .Select(s=>s.Type+":"+s.PhysicalId).Distinct().ToArray();
            string? id=IsId(root)?root:c.PhysicalSourceIds.GetValueOrDefault(root);
            if(id==null&&candidates.Length==1)id=candidates[0];
            if(id==null&&evidence.Length==1&&!g.Sources.Any(s=>c.SeparateDeviceSources.Contains(s.Key)))
                id=c.PhysicalEvidenceIds.GetValueOrDefault(evidence[0]);
            if(id==null||!IsId(id)||used.Contains(id))id=NewId();
            used.Add(id);
            void Transfer(string key){
                DeviceIdentityManager.Inherit(c,key,id);
                if(c.Hidden.Contains(key)&&!c.Hidden.Contains(id))c.Hidden.Add(id);
                if(c.IgnoredDevices.Contains(key)&&!c.IgnoredDevices.Contains(id))c.IgnoredDevices.Add(id);
                if(!c.PreferredSources.ContainsKey(id)&&c.PreferredSources.TryGetValue(key,out var preferred))c.PreferredSources[id]=preferred;
            }
            // Only migrate legacy source settings on first association. A
            // later unhide/unignore must not be undone by old source settings.
            if(!IsId(root)&&!c.PhysicalSourceIds.ContainsKey(root))Transfer(root);
            foreach(var s in identitySources){
                if(!c.PhysicalSourceIds.ContainsKey(s.Key))Transfer(s.Key);
                c.PhysicalSourceIds[s.Key]=id;
            }
            if(evidence.Length==1&&!g.Sources.Any(s=>c.SeparateDeviceSources.Contains(s.Key)))
                c.PhysicalEvidenceIds[evidence[0]]=id;
            output.Add(g with{Reading=g.Reading with{Key=id},Ignored=c.IgnoredDevices.Contains(id)});
        }
        return output.OrderBy(g=>TaskbarBattery.Rank(g.Reading.Type)).ThenBy(g=>g.Reading.Key,StringComparer.Ordinal).ToArray();
    }
}
