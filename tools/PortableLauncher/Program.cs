using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

// Tiny Windows 11 inbox .NET Framework bootstrapper. The actual UI remains
// self-contained .NET 10 in app\. The launcher does not remain in memory.
internal static class Program
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hwnd,string text,string caption,uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        bool check=Array.IndexOf(args,"--package-check")>=0;
        try
        {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app");
            string executable=FindEntry(folder);
            if(!File.Exists(executable)||!File.Exists(FindWorker(folder))||
                !File.Exists(Path.Combine(folder,"appsettings.toml"))||!File.Exists(Path.Combine(folder,"hidapi.dll")))
                throw new FileNotFoundException("Please extract the complete ZIP and keep the app folder beside Battery Monitor.exe.");
            string rollback=null;
            var arguments=new StringBuilder();
            foreach(string arg in args)
            {
                if(arg.StartsWith("--update-from=",StringComparison.Ordinal)){rollback=arg.Substring(14);continue;}
                if(arguments.Length!=0)arguments.Append(' ');
                arguments.Append(Quote(arg));
            }
            var start=new ProcessStartInfo(executable,arguments.ToString()){
                WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true
            };
            // Normal launches never leave a supervising process running.
            if(!check&&rollback!=null){if(!ValidPrevious(rollback,folder))throw new InvalidOperationException("Invalid previous-version path.");return LaunchUpdate(start,rollback);}
            using(var process=Process.Start(start)){
                if(process==null)throw new InvalidOperationException("Could not start Battery Monitor.");
                if(!check)return 0;
                if(!process.WaitForExit(30000)){try{process.Kill();}catch{}return 3;}
                return process.ExitCode;
            }
        }
        catch(Exception ex)
        {
            if(!check)MessageBox(IntPtr.Zero,ex.Message,"Battery Monitor",0x10);
            return 1;
        }
    }

    private static string FindEntry(string folder){string path=Path.Combine(folder,"Battery Monitor.exe");return File.Exists(path)?path:Path.Combine(folder,"LGSTray.exe");}
    private static string FindWorker(string folder){string path=Path.Combine(folder,"Battery Monitor HID.exe");return File.Exists(path)?path:Path.Combine(folder,"LGSTrayHID.exe");}
    private static bool IsEntry(string path){string name=Path.GetFileName(path);return string.Equals(name,"Battery Monitor.exe",StringComparison.OrdinalIgnoreCase)||string.Equals(name,"LGSTray.exe",StringComparison.OrdinalIgnoreCase);}
    private static bool ValidPrevious(string path,string inner)
    {
        try{
            string full=Path.GetFullPath(path);
            string here=FindEntry(Directory.GetParent(inner).FullName);
            if(string.Equals(full,here,StringComparison.OrdinalIgnoreCase)||!File.Exists(full)||
                !IsEntry(full))return false;
            string dir=Path.GetDirectoryName(full);
            if(Directory.Exists(Path.Combine(dir,"app")))dir=Path.Combine(dir,"app");
            return File.Exists(FindEntry(dir))&&File.Exists(FindWorker(dir))&&File.Exists(Path.Combine(dir,"appsettings.toml"));
        }catch{return false;}
    }
    private static int LaunchUpdate(ProcessStartInfo start,string rollback)
    {
        string marker=Path.Combine(Path.GetTempPath(),"rings-ready-"+Guid.NewGuid().ToString("N")+".txt");
        start.EnvironmentVariables["RINGS_STARTUP_READY"]=marker;
        Process child=null;
        try{
            child=Process.Start(start);
            if(child==null)throw new InvalidOperationException("New version could not start.");
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<45000&&!child.HasExited){
                if(Ready(marker,child.Id)){
                    UpdateStartup(rollback,FindEntry(AppDomain.CurrentDomain.BaseDirectory));
                    return 0;
                }
                Thread.Sleep(150);
            }
            // Only terminate the exact process created by this transaction.
            // Never kill a process by name or touch another installed version.
            if(!child.HasExited){child.Kill();if(!child.WaitForExit(5000))throw new InvalidOperationException("New version did not exit; rollback was not started.");}
            return Rollback(rollback);
        }catch(Exception ex){
            if(child==null||child.HasExited)return Rollback(rollback);
            MessageBox(IntPtr.Zero,"Update recovery: "+ex.Message,"Battery Monitor",0x10);return 5;
        }finally{
            if(child!=null)child.Dispose();
            try{if(File.Exists(marker))File.Delete(marker);}catch{}
        }
    }
    private static bool Ready(string marker,int pid){try{return File.Exists(marker)&&File.ReadAllText(marker)==pid.ToString(System.Globalization.CultureInfo.InvariantCulture);}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}
    private static int Rollback(string previous){
        var start=new ProcessStartInfo(previous){WorkingDirectory=Path.GetDirectoryName(previous),UseShellExecute=false,CreateNoWindow=true};
        start.EnvironmentVariables.Remove("RINGS_STARTUP_READY");
        using(var p=Process.Start(start)){if(p==null)throw new InvalidOperationException("Rollback could not launch the previous version.");}
        MessageBox(IntPtr.Zero,"The new version did not report successful startup. The previous version has been launched. Your data and both program folders were retained.","Battery Monitor",0x30);
        return 4;
    }
    private static void UpdateStartup(string previous,string next){
        try{
            using(var key=Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",true)){
                if(key==null)return;
                foreach(string name in new[]{"Battery Monitor","LGSTrayGUI"}){
                    string value=key.GetValue(name) as string;
                    if(value!=null&&string.Equals(value.Trim().Trim('"'),previous,StringComparison.OrdinalIgnoreCase)){
                        key.SetValue("Battery Monitor","\""+next+"\"");
                        if(name!="Battery Monitor")key.DeleteValue(name,false);
                    }
                }
            }
        }catch{}
    }

    // Windows CommandLineToArgvW-compatible escaping, including empty args
    // and trailing backslashes. No shell or command interpreter is involved.
    private static string Quote(string value)
    {
        var b=new StringBuilder("\"");int slashes=0;
        foreach(char ch in value)
        {
            if(ch=='\\'){slashes++;continue;}
            if(ch=='"'){b.Append('\\',slashes*2+1);b.Append('"');}
            else{b.Append('\\',slashes);b.Append(ch);}
            slashes=0;
        }
        b.Append('\\',slashes*2);b.Append('"');return b.ToString();
    }
}
