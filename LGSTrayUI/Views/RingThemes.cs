using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
namespace LGSTrayUI;

// Appearance, layout and animation only. Never overwrite a user's inventory or alerts.
internal static class RingThemes
{
    private static readonly string[] Fields=["Normal","Low","Critical","Offline","Icon","Text","Track","Charging","Background","DividerColor","DividerHeight","DividerWidth","RingWidth","Gradient","TrackOpacity","IconScale","IconStroke","IconX","IconY","TextX","TextY","FontSize","Font","Bold","Rounded","Size","Gap","LeftGap","RightGap","X","Y","Left","Divider","Animate","Motion","Speed","GrayMs","WaitMs","OutMs","InMs","BatteryMs","ColorMs","SpringMs","StaggerMs","Bounce","LowAt","CriticalAt","DeviceColors","DevicePalette","AutoContrast","ChargeHighlight","ChargeBolt","ChargeAnimate","ChargeShowBolt","ChargePeriod","ChargeBrightness","ChargeLength","ChargeBoltScale","ChargeBoltX","ChargeBoltY","ChargeBoltOpacity"];
    private static readonly PropertyInfo[] Properties=Fields.Select(n=>typeof(RingPreferences).GetProperty(n)!).ToArray();
    private static string Store=>Path.Combine(AppDataPaths.LocalAppDataDir,"rings-themes.json");
    internal static Dictionary<string,RingPreferences> All {get;}=Load();
    private static Dictionary<string,RingPreferences> Load(){
        foreach(var file in new[]{Store,Store+".bak"})try{
            if(!File.Exists(file))continue;
            var values=JsonSerializer.Deserialize<Dictionary<string,RingPreferences>>(File.ReadAllText(file))??new();
            return values.Where(x=>x.Value!=null).ToDictionary(x=>x.Key,x=>Snapshot(RingSettingsStore.Parse(JsonSerializer.Serialize(x.Value))));
        }catch(Exception ex){DiagnosticLogger.LogWarning("Theme load: "+ex.Message);}
        return new();
    }
    internal static RingPreferences Snapshot(RingPreferences source){var dest=new RingPreferences();Copy(source,dest);return dest;}
    internal static void Copy(RingPreferences source,RingPreferences target){foreach(var prop in Properties){var value=prop.GetValue(source);if(value is Dictionary<string,string> map)value=new Dictionary<string,string>(map);if(value is Dictionary<string,Dictionary<string,string>> nested)value=nested.ToDictionary(x=>x.Key,x=>new Dictionary<string,string>(x.Value));prop.SetValue(target,value);}}
    internal static void Apply(RingPreferences source)=>Copy(source,RingConfig.Current);
    internal static void ResetAppearance(){var defaults=new RingPreferences();var target=RingConfig.Current;var layoutAndMotion=new HashSet<string>{"DividerColor","DividerHeight","DividerWidth","Gap","LeftGap","RightGap","X","Y","Left","Divider","Animate","Motion","Speed","GrayMs","WaitMs","OutMs","InMs","BatteryMs","ColorMs","SpringMs","StaggerMs","Bounce","ChargeAnimate","ChargePeriod","ChargeBrightness","ChargeLength"};foreach(var prop in Properties.Where(p=>!layoutAndMotion.Contains(p.Name)))prop.SetValue(target,prop.GetValue(defaults));}
    internal static void Save(string name,RingPreferences source){All[name]=Snapshot(source);Persist();}
    internal static void Delete(string name){All.Remove(name);Persist();}
    private static void Persist()=>RingSettingsStore.WriteAtomic(Store,JsonSerializer.Serialize(All,new JsonSerializerOptions{WriteIndented=true}));
}
