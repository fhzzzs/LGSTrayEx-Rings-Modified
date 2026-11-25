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
    private string _blockedReason="";
    private long _requestedAt, _unusableSince;
    private Task<int>? _attach;
    private uint _attemptedPid;
    private bool _disposed;
    private string _status="等待设备连接";
    internal string Status {get=>_status;private set{if(_status==value)return;_status=value;DiagnosticLogger.Log("NativeLayout status: "+value);}}
    internal string PlacementStatus { get; private set; } = "";
    internal void ReportPlacement(string value){if(PlacementStatus==value)return;PlacementStatus=value;DiagnosticLogger.Log("NativeLayout placement: "+value);}
    private bool? _lastLeft;
    internal bool Acknowledged { get; private set; }
    internal bool Verified {get;private set;}

    // Restrict the first experimental revision to known horizontal Windows 11 x64 builds.
    internal static bool SupportedBuild(int build) => build is 22621 or 22631 or 26100 or 26200;
    internal static bool FreshReply(long now,long reply,long since,int request,int applied,int state) =>
        request>0 && request==applied && state==1 && reply>=since && reply<=now && now-reply<1500;
    internal void Update(double logicalWidth)
    {
        if (_disposed) return;
        var c=RingConfig.Current;
        if(_lastLeft!=c.Left){
            if(c.Left&&_attach?.IsCompletedSuccessfully==true&&_attach.Result<0){_attemptedPid=0;_attach=null;}
            _lastLeft=c.Left;
            DiagnosticLogger.Log($"NativeLayout mode: {(c.Left?"Left":"Right")}, Windows build {Environment.OSVersion.Version.Build}, monitors {GetSystemMetrics(80)}, architecture {RuntimeInformation.ProcessArchitecture}");
        }
        if(!c.Left)ReportPlacement("");
        int wanted=c.Left?TaskbarLayoutGeometry.Dimensions(logicalWidth,c.LeftGap,c.RightGap,c.X).total:0;
        if(wanted==0){ Release();_blockedWidth=0;_lastWanted=0;Status=c.Left?"等待设备连接 · 已释放占位":"靠右 · 原生左侧占位已释放";return; }
        if(!SupportedBuild(Environment.OSVersion.Version.Build)||RuntimeInformation.ProcessArchitecture!=Architecture.X64){Release();Status=$"Windows build {Environment.OSVersion.Version.Build} 或架构不在原生支持范围 · 安全定位";return;}
        var bar=FindWindow("Shell_TrayWnd",null);GetWindowThreadProcessId(bar,out uint pid);
        if(bar==0||pid==0){Release();Status="等待 Windows 任务栏";return;}
        if(pid!=_explorerPid){Release();_explorerPid=pid;_blockedWidth=0;_lastWanted=0;_attemptedPid=0;_attach=null;}
        if(wanted!=_lastWanted){_lastWanted=wanted;_blockedWidth=0;_unusableSince=0;DiagnosticLogger.Log($"NativeLayout request: {wanted} DIP, Explorer PID {pid}");}
        if(_blockedWidth==wanted){Status=_blockedReason+"（切换左右可重试）";return;}
        string dll=Path.Combine(AppContext.BaseDirectory,"NativeLayout.dll");
        if(!File.Exists(dll)){Release();Status="NativeLayout.dll 缺失 · 安全定位";return;}
        try{
            if(_view==null){
                _map=MemoryMappedFile.CreateOrOpen($"Local\\LGSTray.Rings9.NativeLayout.3.{pid}",32,MemoryMappedFileAccess.ReadWrite);
                _view=_map.CreateViewAccessor(0,32,MemoryMappedFileAccess.ReadWrite);
                _request=0;_requestedAt=Environment.TickCount64;
                _view.Write(16,0);_view.Write(20,0);_view.Write(24,0L);
            }
            long now=Environment.TickCount64;
            if(_request!=wanted){_request=wanted;_requestedAt=now;_unusableSince=0;Acknowledged=false;Verified=false;}
            _view.Write(0,wanted);_view.Write(4,Environment.ProcessId);Thread.MemoryBarrier();_view.Write(8,now);
            long reply=_view.ReadInt64(24);int state=_view.ReadInt32(20),applied=_view.ReadInt32(16);Thread.MemoryBarrier();
            Acknowledged=FreshReply(now,reply,_requestedAt,wanted,applied,state);
            if(Acknowledged){if(!Verified)Status="原生模块已确认布局 · 正在核验实际位置";return;}
            Verified=false;
            if(reply>=_requestedAt&&reply<=now&&now-reply<1500){
                if(state<0){Block(wanted,"原生控件不兼容或存在布局冲突 · 安全定位");return;}
                if(now-_requestedAt>12000){Block(wanted,$"原生布局等待超时（状态 {state}）");return;}
                Status=state switch {2=>"原生模块已连接 · 未找到主任务栏 Start 控件",3=>"原生模块已连接 · 等待原生宽度生效",4=>"原生模块已连接 · 等待 Start 位移生效",_=>"原生模块已连接 · 等待布局"};
                return;
            }
            // Give an already-loaded provider time to acknowledge a restarted application.
            if(_attemptedPid!=pid&&now-_requestedAt>=750){
                _attemptedPid=pid;
                DiagnosticLogger.Log($"NativeLayout attaching to Explorer PID {pid}");
                _attach=Task.Run(()=>Attach(pid,dll));
            }
            if(_attach?.IsCompletedSuccessfully==true&&_attach.Result<0){Status=$"原生接口连接失败 (0x{_attach.Result:X8}) · 安全定位";return;}
            if(now-_requestedAt>12000){Status=$"原生接口未返回任务栏控件（Windows build {Environment.OSVersion.Version.Build}） · 安全定位";return;}
            Status="正在连接内置原生布局模块";
        }catch(Exception ex){Release();Status="原生布局不可用 · 安全定位";DiagnosticLogger.LogWarning("NativeLayout: "+ex.Message);}
    }
    internal void Verify(bool usable)
    {
        if(!Acknowledged)return;
        if(usable){Verified=true;_unusableSince=0;Status="原生占位生效 · 已核验实际任务栏";return;}
        Verified=false;long now=Environment.TickCount64;if(_unusableSince==0)_unusableSince=now;
        if(now-_unusableSince>=2200)Block(_request,"原生占位与实际控件冲突 · 已释放占位，使用安全定位");
    }
    private void Block(int width,string message){Release();_blockedWidth=width;_blockedReason=message;Status=message;DiagnosticLogger.LogWarning("NativeLayout: "+message);}
    private void Release(){
        try{if(_view!=null){_view.Write(0,0);Thread.MemoryBarrier();_view.Write(8,0L);}}catch{}
        _view?.Dispose();_map?.Dispose();_view=null;_map=null;_request=0;Acknowledged=false;Verified=false;_unusableSince=0;
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
