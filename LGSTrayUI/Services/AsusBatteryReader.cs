using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
namespace LGSTrayUI;

// Only known status queries from the protocol catalog; unknown hardware is
// enumerated for diagnostics, never written to. See DEVICE-COMPATIBILITY.md.
internal static class AsusBatteryReader
{
    [StructLayout(LayoutKind.Sequential)] private struct HidInfo {
        public nint Path; public ushort Vid, Pid; public nint Serial; public ushort Release;
        public nint Manufacturer, Product; public ushort UsagePage, Usage; public int Interface; public nint Next; public int Bus;
    }
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern int hid_write(nint h,byte[] data,nuint len);
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern int hid_read_timeout(nint h,[Out]byte[] data,nuint len,int ms);
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern int hid_winapi_get_container_id(nint h,out Guid id);
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern int hid_send_feature_report(nint h,byte[] data,nuint len);
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern int hid_get_feature_report(nint h,[In,Out]byte[] data,nuint len);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,nint security,uint creation,uint flags,nint template);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle h,out nint data);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(nint data);
    // HIDP_CAPS is 64 bytes; the first five USHORTs include FeatureReportByteLength.
    [StructLayout(LayoutKind.Sequential)] private struct HidCaps {
        public ushort Usage,UsagePage,InputLength,OutputLength,FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=27)] public ushort[] Rest;
    }
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint data,out HidCaps caps);
    private sealed record Channel(string Path,Guid Container,ushort Pid,ushort Page,ushort Usage,int Interface,string Product);
    private static string diagnostics="not scanned";
    internal static string Diagnostics=>Volatile.Read(ref diagnostics);
    internal static string? ReceiverInstance(string path){
        var parts=path.ToLowerInvariant().Split('#');
        if(parts.Length<3||!parts[1].Contains("vid_0b05&pid_1ace")||!parts[1].Contains("mi_02"))return null;
        int last=parts[2].LastIndexOf('&');
        if(last<=0||parts[2].Length-last!=5||!ushort.TryParse(parts[2][(last+1)..],System.Globalization.NumberStyles.HexNumber,null,out _))return null;
        return parts[2][..last];
    }
    internal static bool SameReceiver(string pathA,Guid idA,string pathB,Guid idB){
        if(idA!=Guid.Empty&&idB!=Guid.Empty&&idA==idB)return true;
        var a=ReceiverInstance(pathA);var b=ReceiverInstance(pathB);return a!=null&&a==b;
    }
    internal static bool IsOmniChannel(int iface,ushort page,ushort usage)
        =>iface==2&&((page==0xFF02&&usage==1)||(page==0xFF01&&(usage==1||usage==3))||(page==0xFF00&&(usage==1||usage==2)));
    internal static bool ExpectedReply(byte[] data,int length,byte report,byte command,byte sub){
        if(length>data.Length)return false;
        if(report==0){
            int i=length>0&&data[0]==0?1:0;
            return length>=i+Math.Max(2,sub==1?9:10)&&data[i]==command&&data[i+1]==sub;
        }
        return length>=(command==0xA0?7:report==0xCC?(sub==8?6:7):command==0x12&&sub==1?10:11)&&data[0]==report&&data[1]==command&&(command==0xA0||data[2]==sub);
    }
    private static byte[]? Exchange(string path,byte report,byte command,byte sub,CancellationToken token,int packet=64){
        token.ThrowIfCancellationRequested();
        var h=HidDiscovery.Shared.OpenPath(path);if(h==0)return null;
        try{
            var req=new byte[packet];req[0]=report;req[1]=command;req[2]=sub;var data=new byte[Math.Max(65,packet)];
            for(int i=0;i<16;i++)if(hid_read_timeout(h,data,(nuint)data.Length,0)<=0)break;
            for(int attempt=0;attempt<3;attempt++){
                token.ThrowIfCancellationRequested();
                if(hid_write(h,req,(nuint)req.Length)<0)return null;
                var until=Environment.TickCount64+350;
                while(Environment.TickCount64<until){
                    token.ThrowIfCancellationRequested();
                    int n=hid_read_timeout(h,data,(nuint)data.Length,(int)Math.Max(1,until-Environment.TickCount64));
                    if(n<=0)break;
                    if(ExpectedReply(data,n,report,command,sub))return data[..n];
                }
            }
            return null;
        }finally{HidDiscovery.Shared.Close(h);}
    }
    internal static ushort[] PairedModels(byte[]? reply){
        if(reply==null||reply.Length<7||reply[0]!=1||reply[1]!=0xA0)return [];
        var result=new List<ushort>();
        for(int i=5;i+1<reply.Length;i+=4){ushort pid=(ushort)(reply[i]|(reply[i+1]<<8));if(pid==0)break;result.Add(pid);}
        return result.Distinct().ToArray();
    }
    internal static bool PairedHarpe(byte[]? reply)=>PairedModels(reply).Contains((ushort)0x1B65);
    internal static (int Level,bool Charging)? Decode(byte[]? reply){
        if(reply==null||reply.Length<11||reply[0]!=3||reply[1]!=0x12||reply[2]!=0x07)return null;
        return DecodeMouse(reply[1..],1);
    }
    internal static (int Level,bool Charging)? DecodeMouse(byte[]? reply,int scale,int batteryOffset=4){
        if(reply==null||scale is not (1 or 25)||batteryOffset is not (4 or 6))return null;
        int i=reply.Length>0&&reply[0]==0?1:0;
        if(reply.Length<i+10||reply[i]!=0x12||reply[i+1]!=0x07)return null;
        int raw=reply[i+batteryOffset],charge=reply[i+9];
        if(charge>1||raw>100/scale||(raw==0&&charge==0))return null;
        return(raw*scale,charge==1);
    }
    internal static (int Level,bool Charging)? DecodeKeyboard(byte[]? reply,int batteryByte){
        if(reply==null||batteryByte is not (6 or 11)||reply.Length<=Math.Max(9,batteryByte)||reply[0]!=2||reply[1]!=0x12||reply[2]!=1)return null;
        int level=reply[batteryByte];bool charging=reply[9]!=0;
        return level>100||(level==0&&!charging)?null:(level,charging);
    }
    internal static (int Level,bool Charging)? DecodeDirectKeyboard(byte[]? reply,int batteryByte){
        if(reply==null||batteryByte is not (6 or 11))return null;
        // HIDAPI omits the zero report ID on Windows; normalize the layout.
        var frame=reply.Length>0&&reply[0]==0?reply:new byte[]{0}.Concat(reply).ToArray();
        if(frame.Length<=Math.Max(9,batteryByte)||frame[1]!=0x12||frame[2]!=1||frame[9]>1)return null;
        int level=frame[batteryByte];bool charging=frame[9]==1;
        return level>100||(level==0&&!charging)?null:(level,charging);
    }
    internal static (int Level,bool Charging)? DecodeDirectHeadset(byte[]? battery,byte[]? charge){
        if(battery==null||battery.Length<7||battery[0]!=0xCC||battery[1]!=0x12||battery[2]!=7||battery[6]>100)return null;
        // A missing/invalid charging query must not manufacture charge state.
        if(charge==null||charge.Length<6||charge[0]!=0xCC||charge[1]!=0x12||charge[2]!=8||charge[5]>1)return null;
        bool charging=charge[5]==1;int level=battery[6];
        return level==0&&!charging?null:(level,charging);
    }
    internal static (int Left,int Right,int? Case,bool Charging)? DecodeCetra(byte[]? battery,byte[]? charge){
        if(battery==null||battery.Length<9||battery[0]!=0xCC||battery[1]!=0x12||battery[2]!=7||battery[6]>100||battery[7]>100)return null;
        if(charge==null||charge.Length<6||charge[0]!=0xCC||charge[1]!=0x12||charge[2]!=8)return null;
        bool charging=(charge[5]&3)!=0;
        // A silent receiver can return zeros; do not publish a phantom 0%.
        if(battery[6]==0&&battery[7]==0&&!charging)return null;
        return(battery[6],battery[7],battery[8]<=100?battery[8]:null,charging);
    }
    private static (int Level,bool Charging)? DirectHeadset(string path,CancellationToken token,AsusProfile profile,List<string> notes){
        var battery=Exchange(path,0xCC,0x12,7,token);
        if(battery==null)return null;
        var charge=Exchange(path,0xCC,0x12,8,token);
        if(profile.Pid==0x1AD3){
            if(DecodeCetra(battery,charge) is not { } parts)return null;
            notes.Add($"Cetra components: L={parts.Left}% R={parts.Right}% case={parts.Case?.ToString()??"unknown"}%");
            // Aggregate display is the weaker earbud, never the case.
            return(Math.Min(parts.Left,parts.Right),parts.Charging);
        }
        return DecodeDirectHeadset(battery,charge);
    }
    internal static (int Level,bool Charging)? DecodeHeadset(byte[]? reply){
        if(reply==null||reply.Length<13||reply[0]!=0xFF||reply[1]!=0x1B)return null;
        int mv=reply[11]|(reply[12]<<8);if(mv<2800||mv>4500||reply[9] is not (3 or 10))return null;
        bool charging=reply[9]==10;if(charging&&mv<4100)mv-=160;
        int level=mv>=4100?100:mv>=3800?75:mv>=3700?50:mv>=3500?25:10;
        return(level,charging);
    }
    private static bool HasFeatureChannel(string path){
        using var handle=CreateFile(path,0,3,0,3,0,0);
        if(handle.IsInvalid||!HidD_GetPreparsedData(handle,out var data))return false;
        try{return HidP_GetCaps(data,out var caps)==0x00110000&&caps.FeatureLength>=64;}
        finally{HidD_FreePreparsedData(data);}
    }
    private static (int Level,bool Charging)? Headset(string path,CancellationToken token){
        token.ThrowIfCancellationRequested();var h=HidDiscovery.Shared.OpenPath(path);if(h==0)return null;
        try{
            byte[] request=new byte[64];byte[] cmd=[0xFF,0x08,0,0xFD,4,0x12,0xF1,3,0x52,1];cmd.CopyTo(request,0);
            if(hid_send_feature_report(h,request,(nuint)request.Length)<0)return null;
            if(token.WaitHandle.WaitOne(35))token.ThrowIfCancellationRequested();
            byte[] reply=new byte[64];reply[0]=0xFF;
            int n=hid_get_feature_report(h,reply,(nuint)reply.Length);
            return n>0?DecodeHeadset(reply[..n]):null;
        }finally{HidDiscovery.Shared.Close(h);}
    }
    // Read-only inventory. Does not open a device or send a report. No serial
    // numbers, Bluetooth addresses, instance paths or container IDs exported.
    internal static string InspectHardware(){
        var seen=new HashSet<string>(StringComparer.Ordinal);var head=HidDiscovery.Shared.Enumerate(0,0);
        try{
            for(var p=head;p!=0;){
                var d=Marshal.PtrToStructure<HidInfo>(p);p=d.Next;
                if(d.Vid is not (0x046D or 0x0B05 or 0x045E or 0x3537 or 0x054C or 0x057E or 0x2DC8))continue;
                string product=d.Product==0?"":ControllerIdentity.Clean(Marshal.PtrToStringUni(d.Product));
                seen.Add($"{d.Vid:X4}:{d.Pid:X4} · {product} · mi={d.Interface} · {d.UsagePage:X4}:{d.Usage:X4}");
                if(seen.Count>=256)break;
            }
        }finally{if(head!=0)HidDiscovery.Shared.FreeEnumeration(head);}
        return seen.Count==0?"No matching HID collections":string.Join(Environment.NewLine,seen.OrderBy(x=>x,StringComparer.Ordinal));
    }
    // ASTRO A50 Gen5, verified protocol reference: HeadsetControl / HaloBattery.
    // The base-station collection is an exact VID/PID + usage match.
    internal static byte[] AstroRequest(byte token){
        byte[] frame=new byte[64];frame[0]=2;frame[1]=0x0C;frame[2]=3;frame[4]=6;frame[5]=token;return frame;
    }
    internal static (int Level,bool Charging)? DecodeAstro(byte[] frame,int count,byte token){
        if(count<9||count>frame.Length||frame[0]!=2||frame[1]!=0x0C||frame[2]<6||frame[2]+3>count||frame[3]!=0||frame[4]!=6||frame[5]!=token||frame[6]>100||frame[8]>1)return null;
        return(frame[6],frame[8]==1);
    }
    private static int astroToken;
    internal static IReadOnlyList<TaskbarBattery> ReadAstro(CancellationToken token){
        var rows=new List<TaskbarBattery>();var head=HidDiscovery.Shared.Enumerate(0x046D,0x0B1C);
        var seen=new HashSet<string>(StringComparer.Ordinal);
        byte[]? scanReply=null;
        try{
            for(var p=head;p!=0;){
                token.ThrowIfCancellationRequested();var d=Marshal.PtrToStructure<HidInfo>(p);p=d.Next;
                if(d.Vid!=0x046D||d.Pid!=0x0B1C||d.UsagePage!=0xFF32||d.Usage!=0x0074)continue;
                var path=Marshal.PtrToStringAnsi(d.Path);if(string.IsNullOrEmpty(path))continue;
                var h=HidDiscovery.Shared.OpenPath(path);if(h==0)continue;
                try{
                    Guid id=Guid.Empty;
                    try{if(hid_winapi_get_container_id(h,out id)!=0)id=Guid.Empty;}catch(EntryPointNotFoundException){}
                    string identity=id==Guid.Empty?path:id.ToString("N");if(!seen.Add(identity))continue;
                    byte echo=(byte)((uint)Interlocked.Increment(ref astroToken)%255+1);var request=AstroRequest(echo);var reply=scanReply??=new byte[64];Array.Clear(reply);
                    for(int i=0;i<8;i++)if(hid_read_timeout(h,reply,(nuint)reply.Length,0)<=0)break;
                    if(hid_write(h,request,(nuint)request.Length)<0)continue;
                    for(int i=0;i<8;i++){
                        token.ThrowIfCancellationRequested();int n=hid_read_timeout(h,reply,(nuint)reply.Length,300);
                        if(DecodeAstro(reply,n,echo) is not { } sample)continue;
                        rows.Add(new TaskbarBattery(DeviceType.Headset,"ASTRO A50 Gen 5",sample.Level,sample.Charging?"充电中":"已连接",sample.Charging,DateTimeOffset.UtcNow,"astro:a50-gen5:"+identity){HardwareVendorId=0x046D,HardwareProductId=0x0B1C});
                        break;
                    }
                }finally{HidDiscovery.Shared.Close(h);}
            }
        }finally{if(head!=0)HidDiscovery.Shared.FreeEnumeration(head);}
        return rows;
    }
    private static string gameSirDiagnostics="Not scanned; experimental passive reader disabled by default";
    internal static string GameSirDiagnostics=>Volatile.Read(ref gameSirDiagnostics);
    internal static IReadOnlyList<TaskbarBattery> ReadControllerInput(CancellationToken token,bool gameSirOnly=false,Dictionary<string,string>? present=null){
        var rows=new List<TaskbarBattery>();var seen=new HashSet<string>(StringComparer.Ordinal);var notes=new List<string>();
        // One buffer per scan, never shared between concurrent providers.
        byte[]? scanBuffer=null;
        ReadOnlySpan<ushort> vendors = gameSirOnly ? [0x3537] : [0x054C,0x057E,0x2DC8];
        foreach(ushort vid in vendors){
            var head=HidDiscovery.Shared.Enumerate(vid,0);
            try{
                for(var p=head;p!=0;){
                    token.ThrowIfCancellationRequested();var d=Marshal.PtrToStructure<HidInfo>(p);p=d.Next;
                    if(gameSirOnly&&notes.Count<32)notes.Add($"{d.Vid:X4}:{d.Pid:X4} mi={d.Interface} page={d.UsagePage:X4}:{d.Usage:X4}");
                    if(!ControllerInputBattery.Supports(d.Vid,d.Pid)||(d.Vid==0x3537?(ControllerIdentity.IsG7(d.Vid,d.Pid)?d.UsagePage<0xFF00:d.UsagePage!=0xFFF0):d.UsagePage!=1||d.Usage is not (4 or 5)))continue;
                    var path=Marshal.PtrToStringAnsi(d.Path);if(string.IsNullOrEmpty(path))continue;
                    var h=HidDiscovery.Shared.OpenPath(path);
                    if(h==0){
                        if(gameSirOnly&&notes.Count<32)notes.Add($"{d.Pid:X4} vendor collection open failed");
                        continue;
                    }
                    try{
                        Guid id=Guid.Empty;
                        try{if(hid_winapi_get_container_id(h,out id)!=0)id=Guid.Empty;}catch(EntryPointNotFoundException){}
                        string identity=id==Guid.Empty?path:id.ToString("N");
                        string key=$"controller-hid:{d.Vid:X4}:{d.Pid:X4}:{identity}";
                        if(present!=null)present[path]=key;
                        if(seen.Contains(key))continue;
                        var buffer=scanBuffer??=new byte[128];
                        Array.Clear(buffer); // retain the zero-initialized state of each new collection
                        bool? bluetooth=d.Bus==2?true:d.Bus==1?false:
                            path.Contains("vid&",StringComparison.OrdinalIgnoreCase)||path.Contains("{00001124-0000-1000-8000-00805f9b34fb}",StringComparison.OrdinalIgnoreCase)?true:
                            path.Contains("vid_",StringComparison.OrdinalIgnoreCase)?false:null;
                        // Passive only: never write a GameSir heartbeat or configuration report.
                        for(int i=0;i<3;i++){
                            token.ThrowIfCancellationRequested();
                            int n=hid_read_timeout(h,buffer,(nuint)buffer.Length,120);
                            if(gameSirOnly&&notes.Count<32)notes.Add($"{d.Pid:X4} read length={n}"+(n>0?$" report={buffer[0]:X2}":""));
                            if(n<=0)break;
                            if(d.Vid==0x054C&&bluetooth==null)continue;
                            if(ControllerInputBattery.Decode(d.Vid,d.Pid,buffer,n,bluetooth) is not { } sample)continue;
                            var name=ControllerIdentity.KnownName(d.Vid,d.Pid)!;
                            bool estimated=d.Vid is 0x054C or 0x057E;
                            rows.Add(new TaskbarBattery(DeviceType.Gamepad,name,sample.Level,sample.Full?"已充满":sample.Charging?"充电中":"已连接",sample.Charging,DateTimeOffset.UtcNow,key,estimated?$"≈{sample.Level}%":null){Estimated=estimated,HardwareVendorId=d.Vid,HardwareProductId=d.Pid});
                            seen.Add(key);break;
                        }
                    }finally{HidDiscovery.Shared.Close(h);}
                }
            }finally{if(head!=0)HidDiscovery.Shared.FreeEnumeration(head);}
        }
        if(gameSirOnly)Volatile.Write(ref gameSirDiagnostics,notes.Count==0?"No GameSir HID collections":string.Join("; ",notes));
        return rows;
    }
    internal static IReadOnlyList<TaskbarBattery> Read()=>Read(CancellationToken.None);
    internal static IReadOnlyList<TaskbarBattery> Read(CancellationToken token){
        var result=new List<TaskbarBattery>();var channels=new List<Channel>();var notes=new List<string>();
        var head=HidDiscovery.Shared.Enumerate(0x0B05,0);
        try{
            for(var p=head;p!=0;){
                token.ThrowIfCancellationRequested();
                var d=Marshal.PtrToStructure<HidInfo>(p);p=d.Next;
                if(notes.Count<80)notes.Add($"PID={d.Pid:X4} mi={d.Interface} page={d.UsagePage:X4} usage={d.Usage:X4}");
                var path=Marshal.PtrToStringAnsi(d.Path);if(string.IsNullOrEmpty(path))continue;
                var product=d.Product==0?"":Marshal.PtrToStringUni(d.Product)??"";
                bool candidate=d.Pid==0x1ACE&&IsOmniChannel(d.Interface,d.UsagePage,d.Usage)||
                    PeripheralCatalog.Find(d.Pid,AsusProtocol.DirectMouse) is { } mouseProfile&&d.Interface==mouseProfile.InterfaceNumber&&d.UsagePage>=0xFF00||
                    PeripheralCatalog.Find(d.Pid,AsusProtocol.DirectKeyboard)!=null&&d.UsagePage==0xFF00||
                    PeripheralCatalog.Find(d.Pid,AsusProtocol.DirectHeadset)!=null&&d.UsagePage==0xFF00||
                    d.Pid==0x1C69&&d.Interface==0&&d.UsagePage>=0xFF00||
                    d.Pid==0x1AD0&&path.Contains("mi_02&col03",StringComparison.OrdinalIgnoreCase)&&product.Contains("Harpe",StringComparison.OrdinalIgnoreCase)||
                    PeripheralCatalog.Find(d.Pid,AsusProtocol.StrixGoHeadset)!=null&&HasFeatureChannel(path);
                if(!candidate)continue;
                Guid id=Guid.Empty;var h=HidDiscovery.Shared.OpenPath(path);
                if(h!=0)try{if(hid_winapi_get_container_id(h,out id)!=0)id=Guid.Empty;}
                catch(EntryPointNotFoundException){id=Guid.Empty;}finally{HidDiscovery.Shared.Close(h);}
                if(d.Pid==0x1ACE&&id==Guid.Empty&&ReceiverInstance(path)==null)continue;
                channels.Add(new(path,id,d.Pid,d.UsagePage,d.Usage,d.Interface,product));
            }
        }finally{if(head!=0)HidDiscovery.Shared.FreeEnumeration(head);}
        void Add(AsusProfile profile,string identity,(int Level,bool Charging)? reading,bool remember,ushort pid){
            string model=profile.Pid==0x1B65?"harpe-mini":profile.Name.ToLowerInvariant().Replace(' ','-');
            string key="asus:"+model+":"+identity;
            bool estimate=profile.Scale!=1||profile.Protocol==AsusProtocol.StrixGoHeadset;
            notes.Add($"{profile.Name}: {(reading.HasValue?"battery reply accepted":"no valid battery reply")}");
            if(reading is { } value)result.Add(new TaskbarBattery(profile.Type,profile.Name,value.Level,value.Charging?(value.Level==100?"已充满":"充电中"):"已连接",value.Charging,DateTimeOffset.UtcNow,key,estimate?$"≈{value.Level}%":null){Estimated=estimate,HardwareVendorId=0x0B05,HardwareProductId=pid});
            else if(remember)result.Add(new TaskbarBattery(profile.Type,profile.Name,null,"离线",false,DateTimeOffset.MinValue,key){HardwareVendorId=0x0B05,HardwareProductId=pid});
        }
        foreach(var pair in channels.Where(c=>c.Pid==0x1ACE&&c.Page==0xFF02)){
            var family=channels.Where(c=>c.Pid==0x1ACE&&SameReceiver(pair.Path,pair.Container,c.Path,c.Container)).ToArray();
            if(family.Count(c=>c.Page==0xFF02)!=1){notes.Add("ambiguous OMNI pairing interface");continue;}
            var pids=PairedModels(Exchange(pair.Path,1,0xA0,0,token));
            notes.Add("OMNI paired: "+(pids.Length==0?"none/timeout":string.Join(",",pids.Select(x=>x.ToString("X4")))));
            foreach(var kind in new[]{AsusProtocol.OmniMouse,AsusProtocol.OmniKeyboard}){
                var profiles=pids.Select(pid=>PeripheralCatalog.Find(pid,kind)).OfType<AsusProfile>().ToArray();
                var targets=family.Where(c=>c.Page==(kind==AsusProtocol.OmniMouse?0xFF01:0xFF00)).ToArray();
                if(profiles.Length!=1||targets.Length!=1){if(profiles.Length>0)notes.Add($"OMNI ambiguous/missing {kind}");continue;}
                var profile=profiles[0];var channel=targets[0];var id=pair.Container!=Guid.Empty?pair.Container:channel.Container;
                string identity=id!=Guid.Empty?id.ToString("N"):"instance:"+ReceiverInstance(pair.Path);
                var reading=kind==AsusProtocol.OmniMouse?Decode(Exchange(channel.Path,3,0x12,7,token)):DecodeKeyboard(Exchange(channel.Path,2,0x12,1,token),profile.BatteryByte);
                Add(profile,identity,reading,true,profile.Pid);
            }
        }
        // Only group duplicate collections with exact container evidence, never
        // two devices merely because their model names or PIDs match.
        foreach(var family in channels.Where(c=>c.Pid!=0x1ACE).GroupBy(c=>(c.Pid,Id:c.Container!=Guid.Empty?c.Container.ToString("N"):c.Path))){
            token.ThrowIfCancellationRequested();
            var c=family.First();var profile=PeripheralCatalog.Find(c.Pid,AsusProtocol.DirectMouse)??PeripheralCatalog.Find(c.Pid,AsusProtocol.SpeednovaMouse)??PeripheralCatalog.Find(c.Pid,AsusProtocol.StrixGoHeadset)??PeripheralCatalog.Find(c.Pid,AsusProtocol.DirectKeyboard)??PeripheralCatalog.Find(c.Pid,AsusProtocol.DirectHeadset);
            if(profile==null)continue;
            if(profile.Protocol==AsusProtocol.SpeednovaMouse){
                string name=c.Product.Contains("Extreme",StringComparison.OrdinalIgnoreCase)?"ROG Harpe II Extreme":"ROG Harpe II Ace";
                if(!c.Product.Contains("Harpe",StringComparison.OrdinalIgnoreCase)){notes.Add("SPEEDNOVA model unconfirmed; skipped");continue;}
                profile=profile with{Name=name};
            }
            (int Level,bool Charging)? reading=null;
            foreach(var channel in family.Take(3)){
                try{
                    reading=profile.Protocol switch{
                        AsusProtocol.StrixGoHeadset=>Headset(channel.Path,token),
                        AsusProtocol.DirectKeyboard=>DecodeDirectKeyboard(Exchange(channel.Path,0,0x12,1,token,65),profile.BatteryByte),
                        AsusProtocol.DirectHeadset=>DirectHeadset(channel.Path,token,profile,notes),
                        AsusProtocol.SpeednovaMouse when c.Pid==0x1AD0=>Decode(Exchange(channel.Path,3,0x12,7,token)),
                        AsusProtocol.SpeednovaMouse=>DecodeMouse(Exchange(channel.Path,0,0x12,7,token,64),1),
                        _=>DecodeMouse(Exchange(channel.Path,0,0x12,7,token,65),profile.Scale,profile.BatteryByte)
                    };
                }catch(OperationCanceledException){throw;}catch(Exception ex){notes.Add(profile.Name+": "+ex.GetType().Name);}
                if(reading.HasValue)break;
            }
            // Device is physically enumerated; no reply is unknown/offline,
            // not a fabricated 0%. Avoid persistent receiver-only ghosts.
            Add(profile,family.Key.Id,reading,false,c.Pid);
        }
        Volatile.Write(ref diagnostics,notes.Count==0?"No ASUS HID interfaces found":string.Join("; ",notes.Take(100)));
        return result;
    }
}
