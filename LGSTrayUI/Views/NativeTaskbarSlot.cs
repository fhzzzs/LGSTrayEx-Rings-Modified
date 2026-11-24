using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// One bounded request per primary Explorer process. No registry changes, elevation,
// or Windhawk. The shell-side provider restores the original XAML properties on lease expiry.
internal sealed class NativeTaskbarSlot : IDisposable
{
    internal static NativeTaskbarSlot Instance { get; } = new();
    private static readonly Guid SlotClsid = new("9d6ab7c2-502e-4ae0-a21d-3f771e9b8433");
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private uint _explorerPid;
    private int _request, _blockedWidth, _lastWanted;
    private long _requestedAt, _unusableSince;
    private Task<int>? _attach;
    private uint _attemptedPid;
    private bool _disposed;
    internal string Status { get; private set; } = "等待设备连接";
    internal bool Acknowledged { get; private set; }

    // Restrict the first experimental revision to known horizontal Windows 11 x64 builds.
    internal static bool SupportedBuild(int build) => build is 22621 or 22631 or 26100 or 26200;
    internal static bool FreshReply(long now,long reply,long since,int request,int applied,int state) =>
        request>0 && request==applied && state==1 && reply>=since && reply<=now && now-reply<1500;
    internal void Update(double logicalWidth)
    {
        if (_disposed) return;
        var c=RingConfig.Current;
        int wanted=c.Left?TaskbarLayoutGeometry.Dimensions(logicalWidth,c.LeftGap,c.RightGap,c.X).total:0;
        if(wanted==0){ Release();_blockedWidth=0;_lastWanted=0;Status=c.Left?"等待设备连接 · 已释放占位":"靠右 · 原生左侧占位已释放";return; }
        if(!SupportedBuild(Environment.OSVersion.Version.Build)||RuntimeInformation.ProcessArchitecture!=Architecture.X64||GetSystemMetrics(80)!=1){Release();Status="当前 Windows 版本或显示器布局不在实验性原生支持范围 · 安全定位";return;}
        var bar=FindWindow("Shell_TrayWnd",null);GetWindowThreadProcessId(bar,out uint pid);
        if(bar==0||pid==0){Release();Status="等待 Windows 任务栏";return;}
        if(pid!=_explorerPid){Release();_explorerPid=pid;_blockedWidth=0;_lastWanted=0;_attemptedPid=0;_attach=null;}
        if(wanted!=_lastWanted){_lastWanted=wanted;_blockedWidth=0;_unusableSince=0;}
        if(_blockedWidth==wanted){Status="原生占位未通过实际位置校验 · 安全定位（切换左右可重试布局）";return;}
        string dll=Path.Combine(AppContext.BaseDirectory,"NativeLayout.dll");
        if(!File.Exists(dll)){Release();Status="NativeLayout.dll 缺失 · 安全定位";return;}
        try{
            if(_view==null){
                _map=MemoryMappedFile.CreateOrOpen($"Local\\LGSTray.Rings9.NativeLayout.{pid}",32,MemoryMappedFileAccess.ReadWrite);
                _view=_map.CreateViewAccessor(0,32,MemoryMappedFileAccess.ReadWrite);
                _request=0;_requestedAt=Environment.TickCount64;
                _view.Write(16,0);_view.Write(20,0);_view.Write(24,0L);
            }
            long now=Environment.TickCount64;
            if(_request!=wanted){_request=wanted;_requestedAt=now;_unusableSince=0;Acknowledged=false;}
            _view.Write(0,wanted);_view.Write(4,Environment.ProcessId);Thread.MemoryBarrier();_view.Write(8,now);
            long reply=_view.ReadInt64(24);int state=_view.ReadInt32(20),applied=_view.ReadInt32(16);Thread.MemoryBarrier();
            Acknowledged=FreshReply(now,reply,_requestedAt,wanted,applied,state);
            if(Acknowledged){Status="原生模块已确认布局 · 正在核验实际位置";return;}
            if(reply>=_requestedAt&&reply<=now&&now-reply<1500){
                if(state<0){Block(wanted,"原生控件不兼容或存在布局冲突 · 安全定位");return;}
                Status="原生模块已连接 · 等待布局";return;
            }
            // Give an already-loaded provider time to acknowledge a restarted application.
            if(_attemptedPid!=pid&&now-_requestedAt>=750){
                _attemptedPid=pid;
                _attach=Task.Run(()=>Attach(pid,dll));
            }
            if(_attach?.IsCompletedSuccessfully==true&&_attach.Result<0){Status=$"原生接口连接失败 (0x{_attach.Result:X8}) · 安全定位";return;}
            if(now-_requestedAt>12000){Status="原生接口未返回任务栏控件 · 安全定位";return;}
            Status="正在连接内置原生布局模块";
        }catch(Exception ex){Release();Status="原生布局不可用 · 安全定位";DiagnosticLogger.LogWarning("NativeLayout: "+ex.Message);}
    }
    internal void Verify(bool usable)
    {
        if(!Acknowledged)return;
        if(usable){_unusableSince=0;Status="原生占位生效 · 已核验实际任务栏";return;}
        long now=Environment.TickCount64;if(_unusableSince==0)_unusableSince=now;
        if(now-_unusableSince>=2200)Block(_request,"原生占位与实际控件冲突 · 已释放占位，使用安全定位");
    }
    private void Block(int width,string message){Release();_blockedWidth=width;Status=message;}
    private void Release(){
        try{if(_view!=null){_view.Write(0,0);Thread.MemoryBarrier();_view.Write(8,0L);}}catch{}
        _view?.Dispose();_map?.Dispose();_view=null;_map=null;_request=0;Acknowledged=false;_unusableSince=0;
    }
    public void Dispose(){Release();_disposed=true;}

    // Use the documented Windows XAML Diagnostics attachment API, scoped to Shell_TrayWnd.
    // No remote thread loader or modification of executable files. If the platform rejects
    // an external diagnostics session, report failure and leave the user's shell alone.
    private static int Attach(uint pid,string dll)
    {
        nint module=0;
        try{
            var bar=FindWindow("Shell_TrayWnd",null);GetWindowThreadProcessId(bar,out uint actual);
            if(bar==0||actual!=pid)return unchecked((int)0x80070490);
            // Stage by content hash so Explorer never locks the portable/build output.
            // The staged DLL is retained until Explorer has released it.
            byte[] binary=File.ReadAllBytes(dll);
            string stage=Path.Combine(AppDataPaths.LocalAppDataDir,"NativeLayout",Convert.ToHexString(SHA256.HashData(binary)));
            Directory.CreateDirectory(stage);
            string staged=Path.Combine(stage,"NativeLayout.dll");
            if(!File.Exists(staged)){string tmp=staged+"."+Environment.ProcessId+".tmp";File.WriteAllBytes(tmp,binary);try{File.Move(tmp,staged,false);}catch(IOException) when(File.Exists(staged)){File.Delete(tmp);}}
            if(!SHA256.HashData(File.ReadAllBytes(staged)).AsSpan().SequenceEqual(SHA256.HashData(binary)))return unchecked((int)0x8007000D);
            module=LoadLibraryEx("Windows.UI.Xaml.dll",0,0x00000800); // SYSTEM32 only
            if(module==0)return Marshal.GetHRForLastWin32Error();
            nint proc=GetProcAddress(module,"InitializeXamlDiagnosticsEx");
            if(proc==0)return unchecked((int)0x80004001);
            var init=Marshal.GetDelegateForFunctionPointer<InitializeDiagnostics>(proc);
            int hr=init("VisualDiagConnection1",pid,null,staged,SlotClsid,null);
            // The diagnostics channel number differs between Explorer builds. Do not retry
            // an access-denied/busy session or repeatedly attach to the same process.
            for(int i=2;i<=5&&hr==unchecked((int)0x80070490);i++)hr=init("VisualDiagConnection"+i,pid,null,staged,SlotClsid,null);
            DiagnosticLogger.LogWarning($"NativeLayout attach to primary taskbar: 0x{hr:X8}");return hr;
        }catch(Exception ex){DiagnosticLogger.LogWarning("NativeLayout attach: "+ex.Message);return Marshal.GetHRForException(ex);}
        finally{if(module!=0)FreeLibrary(module);}
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi,CharSet=CharSet.Unicode)]
    private delegate int InitializeDiagnostics(string endpoint,uint pid,string? diagnostics,string provider,Guid clsid,string? initialization);
    [DllImport("kernel32.dll",EntryPoint="LoadLibraryExW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern nint LoadLibraryEx(string path,nint file,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] private static extern nint GetProcAddress(nint module,string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
    [DllImport("user32.dll",EntryPoint="FindWindowW",CharSet=CharSet.Unicode)] private static extern nint FindWindow(string cls,string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
