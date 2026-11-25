using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using LGSTrayPrimitives;
namespace LGSTrayUI;

// Independent display-only database. External data never authorizes HID writes,
// changes a parser, or merges hardware identities. Explicit local import only.
internal static class PeripheralDisplayCatalog
{
    private const int MaxBytes=131072;
    private static readonly string PathName=Path.Combine(AppDataPaths.LocalAppDataDir,"peripheral-display-catalog.json");
    private static Dictionary<string,DeviceType> entries=Load();
    internal static int Count=>Volatile.Read(ref entries).Count;
    internal static DeviceType? Classify(string name)=>Volatile.Read(ref entries).TryGetValue(name.Trim(),out var kind)?kind:null;
    internal static Dictionary<string,DeviceType> Parse(string json){
        if(System.Text.Encoding.UTF8.GetByteCount(json)>MaxBytes)throw new FormatException("Catalog too large");
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=8});
        var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("schema",out var schema)||schema.GetInt32()!=1||!root.TryGetProperty("models",out var models)||models.ValueKind!=JsonValueKind.Array||models.GetArrayLength()>1000)
            throw new FormatException("Unsupported display catalog schema");
        var result=new Dictionary<string,DeviceType>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in models.EnumerateArray()){
            string name=item.GetProperty("name").GetString()?.Trim()??"";
            string type=item.GetProperty("type").GetString()??"";
            if(name.Length<3||name.Length>120||System.Linq.Enumerable.Any(name,char.IsControl))throw new FormatException("Invalid model name");
            DeviceType kind=type switch{"mouse"=>DeviceType.Mouse,"keyboard"=>DeviceType.Keyboard,"headset"=>DeviceType.Headset,"gamepad"=>DeviceType.Gamepad,_=>throw new FormatException("Invalid device type")};
            if(!result.TryAdd(name,kind))throw new FormatException("Duplicate model");
        }
        return result;
    }
    private static string Read(string path){
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>MaxBytes)throw new FormatException("Catalog too large");
        using var reader=new StreamReader(stream,System.Text.Encoding.UTF8,true);return reader.ReadToEnd();
    }
    private static Dictionary<string,DeviceType> Load(){
        try{return File.Exists(PathName)?Parse(Read(PathName)):new(StringComparer.OrdinalIgnoreCase);}
        catch(Exception ex){DiagnosticLogger.LogWarning("Display catalog: "+ex.Message);return new(StringComparer.OrdinalIgnoreCase);}
    }
    internal static int Import(string file){
        string json=Read(file);var parsed=Parse(json);
        RingSettingsStore.WriteAtomic(PathName,json);Volatile.Write(ref entries,parsed);return parsed.Count;
    }
}
