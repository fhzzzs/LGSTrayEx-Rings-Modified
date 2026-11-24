using System;
using System.Collections.Generic;
using System.Linq;
using LGSTrayPrimitives;
namespace LGSTrayUI;

// Raw providers remain separate; exact identity and unique Logitech cross-provider model pairs
// are grouped before rendering. Inferred model links remain reversible, not hardware proof.
internal sealed record DeviceGroup(TaskbarBattery Reading, TaskbarBattery[] Sources, bool Ignored, bool Inferred=false);
internal static class DeviceIdentityManager
{
    internal static string Source(string key)=>key.Split(':')[0] switch {"logi"=>key.Contains(":GHub:",StringComparison.OrdinalIgnoreCase)?"G HUB":"Logitech HID", "gip"=>"Windows Gaming Input / GIP","xinput"=>"XInput","ble"=>"Bluetooth LE","asus"=>"ASUS HID",_=>"历史记录"};
    internal static string Resolve(RingPreferences c,string key){var seen=new HashSet<string>(StringComparer.Ordinal);while(seen.Add(key)){if(c.DeviceAliases.TryGetValue(key,out var next)&&next!=key){key=next;continue;}if(c.AutoDeduplicate&&!c.SeparateDeviceSources.Contains(key)&&c.AutomaticAliases.TryGetValue(key,out next)&&next!=key&&!c.SeparateDeviceSources.Contains(next)){key=next;continue;}break;}return key;}
    private static string? LogitechProvider(TaskbarBattery d){
        var parts=d.Key.Split(':',5);
        return parts.Length>=5&&parts[0]=="logi"&&(parts[3] is "Native" or "GHub")?parts[3]:null;
    }
    private static string LogitechModel(TaskbarBattery d){
        var n=new string(d.Name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if(d.Type==DeviceType.Mouse&&(n is "PROX2" or "PROXSUPERLIGHT2" or "PROXSUPERLIGHT2WIRELESSMOUSE" or "PROXSUPERLIGHT2GAMINGMOUSE" or "PROXSUPERLIGHT2WIRELESSGAMINGMOUSE"))return "PROXSUPERLIGHT2";
        if(d.Type==DeviceType.Headset&&(n is "PROX2LIGHTSPEED" or "PROX2LIGHTSPEEDGAMINGHEADSET" or "PROX2LIGHTSPEEDWIRELESSGAMINGHEADSET" or "PROX2LIGHTSPEEDWIRELESSHEADSET"))return "PROX2LIGHTSPEED";
        return n;
    }
    // A base G HUB model and its _ext1 variant can describe the wireless/USB
    // modes of one unit. Never infer identity for two reporting G HUB records,
    // multiple native units, distinct serials or arbitrary same-name devices.
    private static string? GHubModeFamily(TaskbarBattery d){
        if(LogitechProvider(d)!="GHub")return null;
        string id=d.Key.Split(':',5)[4];
        if(!id.StartsWith("GHUB.",StringComparison.OrdinalIgnoreCase))return null;
        return id.EndsWith("_ext1",StringComparison.OrdinalIgnoreCase)?id[..^5]:id;
    }
    private static bool CompatibleModelSources(TaskbarBattery[] rows){
        int native=rows.Count(d=>LogitechProvider(d)=="Native");
        var ghub=rows.Where(d=>LogitechProvider(d)=="GHub").ToArray();
        if(native>1||ghub.Length<1||ghub.Length>2)return false;
        if(rows.Select(d=>d.PhysicalId).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Count()>1)return false;
        if(ghub.Length==1)return native==1;
        var family=GHubModeFamily(ghub[0]);
        if(family==null||!string.Equals(family,GHubModeFamily(ghub[1]),StringComparison.OrdinalIgnoreCase))return false;
        string a=ghub[0].Key.Split(':',5)[4],b=ghub[1].Key.Split(':',5)[4];
        if(string.Equals(a,b,StringComparison.OrdinalIgnoreCase))return false;
        // A second source which has ever supplied a reading is not a phantom.
        return ghub.Count(d=>d.Level.HasValue||d.Charging||d.Updated!=DateTimeOffset.MinValue)<=1;
    }
    internal static DeviceGroup[] Group(IEnumerable<TaskbarBattery> readings,RingPreferences c){
        var raw=readings.GroupBy(d=>d.Key,StringComparer.Ordinal).Select(g=>Best(g,null)).ToArray();
        var byKey=raw.ToDictionary(d=>d.Key,StringComparer.Ordinal);
        foreach(var pair in c.AutomaticAliases.ToArray()){
            if(byKey.TryGetValue(pair.Key,out var a)&&byKey.TryGetValue(pair.Value,out var b)&&
               !string.IsNullOrWhiteSpace(a.PhysicalId)&&!string.IsNullOrWhiteSpace(b.PhysicalId)&&
               (a.Type!=b.Type||a.PhysicalId!=b.PhysicalId))c.AutomaticAliases.Remove(pair.Key);
        }
        var roots=raw.ToDictionary(d=>d.Key,d=>Resolve(c,d.Key),StringComparer.Ordinal);
        if(c.AutoDeduplicate){foreach(var set in raw.Where(d=>!string.IsNullOrWhiteSpace(d.PhysicalId)&&!c.SeparateDeviceSources.Contains(d.Key)).GroupBy(d=>(d.Type,d.PhysicalId))){
            var keys=set.Select(d=>roots[d.Key]).Distinct().ToArray();if(keys.Length<2)continue;
            // Explicit user choices take priority; otherwise use persisted order then stable key.
            string root=keys.OrderByDescending(k=>c.DeviceAliases.ContainsValue(k)).ThenBy(k=>c.Order.IndexOf(k) is int n&&n>=0?n:int.MaxValue).ThenBy(k=>k,StringComparer.Ordinal).First();
            foreach(var key in roots.Keys.ToArray())if(keys.Contains(roots[key])){
                Inherit(c,roots[key],root); if(c.Hidden.Contains(roots[key])&&!c.Hidden.Contains(root))c.Hidden.Add(root);
                if(key!=root&&!c.DeviceAliases.ContainsKey(key))c.AutomaticAliases[key]=root;
                roots[key]=root;
            }
        }}
        var inferred=new HashSet<string>(StringComparer.Ordinal);
        if(c.AutoDeduplicate){
            // Include remembered offline records: rescan and reconnect must not undo grouping.
            foreach(var set in raw.Where(d=>LogitechProvider(d)!=null).GroupBy(d=>(d.Type,Model:LogitechModel(d)))){
                var pair=set.ToArray();
                if(set.Key.Model.Length==0||!CompatibleModelSources(pair))continue;
                if(pair.Any(d=>c.SeparateDeviceSources.Contains(d.Key)))continue;
                var keys=pair.Select(d=>roots[d.Key]).Distinct(StringComparer.Ordinal).ToArray();
                if(keys.Length<2)continue;
                var memberKeys=pair.Select(d=>d.Key).ToHashSet(StringComparer.Ordinal);
                // Respect explicit links; only extend a group when every linked member
                // belongs to this unambiguous model family. Do not merge two user groups.
                if(roots.Any(p=>keys.Contains(p.Value)&&!memberKeys.Contains(p.Key)))continue;
                var explicitRoots=keys.Where(k=>c.DeviceAliases.ContainsValue(k)).ToArray();
                if(explicitRoots.Length>1)continue;
                var root=keys.OrderByDescending(k=>explicitRoots.Contains(k))
                    .ThenByDescending(k=>c.ModelGroupRoots.Contains(k))
                    .ThenBy(k=>c.Order.IndexOf(k) is int n&&n>=0?n:int.MaxValue)
                    .ThenBy(k=>k,StringComparer.Ordinal).First();
                foreach(var secondary in keys.Where(k=>k!=root)){
                    Inherit(c,secondary,root);
                    if(c.Hidden.Contains(secondary)&&!c.Hidden.Contains(root))c.Hidden.Add(root);
                }
                if(!c.ModelGroupRoots.Contains(root))c.ModelGroupRoots.Add(root);
                foreach(var d in pair)roots[d.Key]=root;
                inferred.Add(root);
            }
        }
        return raw.GroupBy(d=>roots[d.Key],StringComparer.Ordinal).Select(g=>{
            var sources=g.OrderBy(d=>d.Key,StringComparer.Ordinal).ToArray();var best=Best(sources,c.PreferredSources.GetValueOrDefault(g.Key));
            var primary=sources.FirstOrDefault(d=>d.Key==g.Key)??best;
            return new DeviceGroup(best with{Key=g.Key,Name=primary.Name},sources,c.IgnoredDevices.Contains(g.Key),inferred.Contains(g.Key));
        }).OrderBy(g=>TaskbarBattery.Rank(g.Reading.Type)).ThenBy(g=>g.Reading.Key,StringComparer.Ordinal).ToArray();
    }
    internal static TaskbarBattery Best(IEnumerable<TaskbarBattery> sources,string? preferred)=>sources.OrderBy(d=>d.Offline).ThenByDescending(d=>d.Level.HasValue).ThenByDescending(d=>d.Key==preferred).ThenByDescending(d=>d.ShortLabel!=null&&d.ShortLabel!="—"&&d.ShortLabel!="USB").ThenBy(d=>d.Key.StartsWith("xinput:")?2:d.Key.StartsWith("ble:")?1:0).ThenByDescending(d=>d.Updated).ThenBy(d=>d.Key,StringComparer.Ordinal).First();
    // Preserve existing primary settings; inherit missing ones from secondary. Keep the
    // secondary settings so undoing a manual link does not destroy customization.
    internal static void Inherit(RingPreferences c,string from,string to){
        if(from==to)return;
        void Copy<T>(Dictionary<string,T> map,Func<T,T> clone){if(!map.ContainsKey(to)&&map.TryGetValue(from,out var val))map[to]=clone(val);}
        Copy(c.Names,v=>v);Copy(c.DeviceRules,v=>v);Copy(c.DeviceColors,v=>v);Copy(c.DevicePalette,v=>new Dictionary<string,string>(v));Copy(c.AlertSounds,v=>v.ToDictionary(x=>x.Key,x=>new AlertAudio{Sound=x.Value.Sound,Volume=x.Value.Volume}));
        if(!c.Order.Contains(to)&&c.Order.IndexOf(from) is int i&&i>=0)c.Order.Insert(i,to);
    }
    internal static void Merge(RingPreferences c,DeviceGroup primary,DeviceGroup secondary){
        var target=primary.Reading.Key;if(target==secondary.Reading.Key||primary.Reading.Type!=secondary.Reading.Type)return;
        Inherit(c,secondary.Reading.Key,target);
        c.DeviceAliases.Remove(target);c.AutomaticAliases.Remove(target);
        foreach(var d in primary.Sources.Concat(secondary.Sources)){c.SeparateDeviceSources.Remove(d.Key);c.AutomaticAliases.Remove(d.Key);if(d.Key!=target)c.DeviceAliases[d.Key]=target;}
        if(secondary.Reading.Key!=target)c.DeviceAliases[secondary.Reading.Key]=target;
    }
    internal static void Split(RingPreferences c,DeviceGroup group){var keys=group.Sources.Select(d=>d.Key).Append(group.Reading.Key).ToHashSet();foreach(var key in c.DeviceAliases.Keys.ToArray())if(keys.Contains(key)||keys.Contains(Resolve(c,key)))c.DeviceAliases.Remove(key);foreach(var key in c.AutomaticAliases.Keys.ToArray())if(keys.Contains(key)||keys.Contains(c.AutomaticAliases[key]))c.AutomaticAliases.Remove(key);c.PreferredSources.Remove(group.Reading.Key);foreach(var key in keys)if(!c.SeparateDeviceSources.Contains(key))c.SeparateDeviceSources.Add(key);}
}
