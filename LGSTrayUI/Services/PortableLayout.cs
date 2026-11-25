using System;
using System.IO;

namespace LGSTrayUI;

internal static class PortableLayout
{
    internal const string EntryName="LGSTray.exe";
    internal const string AppFolder="app";

    // From a running inner executable resolve the user-visible entry point.
    // Old flat builds and source/debug launches still use their own path.
    internal static string EntryExecutable(string processPath)
    {
        string full=Path.GetFullPath(processPath);
        var directory=Path.GetDirectoryName(full);
        if(directory!=null&&Path.GetFileName(directory).Equals(AppFolder,StringComparison.OrdinalIgnoreCase)
           &&Path.GetFileName(full).Equals(EntryName,StringComparison.OrdinalIgnoreCase)){
            var parent=Directory.GetParent(directory);
            if(parent!=null){
                string launcher=Path.Combine(parent.FullName,EntryName);
                if(File.Exists(launcher)&&File.Exists(Path.Combine(directory,"LGSTrayHID.exe"))&&
                   File.Exists(Path.Combine(directory,"appsettings.toml")))return launcher;
            }
        }
        return full;
    }

    internal static string CurrentEntry=>EntryExecutable(Environment.ProcessPath??Path.Combine(AppContext.BaseDirectory,EntryName));

    // New packages use app\; legacy flat packages remain valid for rollback.
    internal static bool IsCompletePackage(string folder)
    {
        if(!File.Exists(Path.Combine(folder,EntryName)))return false;
        string inner=Path.Combine(folder,AppFolder);
        if(!Directory.Exists(inner))inner=folder;
        foreach(string name in new[]{EntryName,"LGSTrayHID.exe","hidapi.dll","appsettings.toml"})
            if(!File.Exists(Path.Combine(inner,name)))return false;
        return true;
    }
}
