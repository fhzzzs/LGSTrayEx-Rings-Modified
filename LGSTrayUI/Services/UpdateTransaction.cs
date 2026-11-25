using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal static class UpdateTransaction
{
    internal const string OwnershipMarker=".rings-managed-version";
    internal static void MarkReady()
    {
        string? path=Environment.GetEnvironmentVariable("RINGS_STARTUP_READY");
        if(string.IsNullOrWhiteSpace(path))return;
        try{
            string full=Path.GetFullPath(path);
            if(!string.Equals(Path.GetDirectoryName(full),Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||
               !Regex.IsMatch(Path.GetFileName(full),@"^rings-ready-[a-f0-9]{32}\.txt$"))return;
            File.WriteAllText(full,Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }catch(Exception ex){DiagnosticLogger.LogWarning("Update startup confirmation: "+ex.Message);}
    }
    internal static void PreserveConfiguration(string oldApp,string newRoot)
    {
        string target=Directory.Exists(Path.Combine(newRoot,"app"))?Path.Combine(newRoot,"app"):newRoot;
        // Local overrides are deliberately carried forward. Preserve the old
        // main TOML separately for review rather than silently overriding new defaults.
        foreach(var pair in new[]{("appsettings.local.toml","appsettings.local.toml"),("appsettings.toml","appsettings.previous.toml")}){
            string from=Path.Combine(oldApp,pair.Item1),to=Path.Combine(target,pair.Item2);
            if(File.Exists(from)&&!File.Exists(to))File.Copy(from,to);
        }
    }
    internal static bool CanRemove(string root,string directory,string current,string? previous,DateTimeOffset now)
    {
        var full=Path.GetFullPath(directory);
        if(!string.Equals(Path.GetDirectoryName(full),Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))return false;
        if(!Regex.IsMatch(Path.GetFileName(full),@"^rings-[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{8}$")||
            !File.Exists(Path.Combine(full,OwnershipMarker)))return false;
        if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)return false;
        string entry=Path.Combine(full,"LGSTray.exe");
        if(string.Equals(entry,current,StringComparison.OrdinalIgnoreCase)||string.Equals(entry,previous,StringComparison.OrdinalIgnoreCase))return false;
        return now-Directory.GetLastWriteTimeUtc(full)>TimeSpan.FromDays(7);
    }
    internal static int CleanOldVersions(string root,string current,string? previous,DateTimeOffset now)
    {
        if(!Directory.Exists(root))return 0;
        int count=0;
        foreach(var dir in Directory.EnumerateDirectories(root))try{
            if(!CanRemove(root,dir,current,previous,now))continue;
            // Do not traverse junctions or symbolic links in managed folders.
            if(HasReparsePoint(dir))continue;
            Directory.Delete(dir,true);count++;
        }catch(Exception ex){DiagnosticLogger.LogWarning("Version cleanup: "+ex.Message);}
        return count;
    }
    private static bool HasReparsePoint(string path){
        foreach(var child in Directory.EnumerateFileSystemEntries(path)){
            var attr=File.GetAttributes(child);if((attr&FileAttributes.ReparsePoint)!=0)return true;
            if((attr&FileAttributes.Directory)!=0&&HasReparsePoint(child))return true;
        }
        return false;
    }
}
