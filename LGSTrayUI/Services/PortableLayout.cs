using System;
using System.IO;
namespace LGSTrayUI;
internal static class PortableLayout
{
    internal const string EntryName="Battery Monitor.exe";
    internal const string LegacyEntryName="LGSTray.exe";
    internal const string AppFolder="app";
    internal static bool IsEntryName(string? name)=>string.Equals(name,EntryName,StringComparison.OrdinalIgnoreCase)||string.Equals(name,LegacyEntryName,StringComparison.OrdinalIgnoreCase);
    internal static string FindEntry(string folder)=>File.Exists(Path.Combine(folder,EntryName))?Path.Combine(folder,EntryName):Path.Combine(folder,LegacyEntryName);
    internal static string FindWorker(string folder)=>File.Exists(Path.Combine(folder,"Battery Monitor HID.exe"))?Path.Combine(folder,"Battery Monitor HID.exe"):Path.Combine(folder,"LGSTrayHID.exe");
    internal static string EntryExecutable(string processPath)
    {
        string full=Path.GetFullPath(processPath);
        var directory=Path.GetDirectoryName(full);
        if(directory!=null&&Path.GetFileName(directory).Equals(AppFolder,StringComparison.OrdinalIgnoreCase)&&IsEntryName(Path.GetFileName(full))){
            var parent=Directory.GetParent(directory);
            if(parent!=null&&File.Exists(FindEntry(parent.FullName))&&File.Exists(FindWorker(directory))&&File.Exists(Path.Combine(directory,"appsettings.toml")))
                return FindEntry(parent.FullName);
        }
        return full;
    }
    internal static string CurrentEntry=>EntryExecutable(Environment.ProcessPath??FindEntry(AppContext.BaseDirectory));
    internal static bool IsCompletePackage(string folder)
    {
        if(!File.Exists(FindEntry(folder)))return false;
        string inner=Path.Combine(folder,AppFolder);
        if(!Directory.Exists(inner))inner=folder;
        return File.Exists(FindEntry(inner))&&File.Exists(FindWorker(inner))&&File.Exists(Path.Combine(inner,"hidapi.dll"))&&File.Exists(Path.Combine(inner,"appsettings.toml"));
    }
}
