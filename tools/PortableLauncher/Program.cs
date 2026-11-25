using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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
            string executable=Path.Combine(folder,"LGSTray.exe");
            if(!File.Exists(executable)||!File.Exists(Path.Combine(folder,"LGSTrayHID.exe"))||
                !File.Exists(Path.Combine(folder,"appsettings.toml"))||!File.Exists(Path.Combine(folder,"hidapi.dll")))
                throw new FileNotFoundException("Please extract the complete ZIP and keep the app folder beside LGSTray.exe.");
            var arguments=new StringBuilder();
            foreach(string arg in args)
            {
                if(arguments.Length!=0)arguments.Append(' ');
                arguments.Append(Quote(arg));
            }
            var start=new ProcessStartInfo(executable,arguments.ToString()){
                WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true
            };
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
