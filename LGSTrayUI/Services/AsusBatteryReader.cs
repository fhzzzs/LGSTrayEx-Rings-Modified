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
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern nint hid_enumerate(ushort vid,ushort pid);
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern void hid_free_enumeration(nint p);
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern nint hid_open_path([MarshalAs(UnmanagedType.LPStr)]string path);
    [DllImport("hidapi",CallingConvention=CallingConvention.Cdecl)] private static extern void hid_close(nint h);
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
            return length>=i+10&&data[i]==command&&data[i+1]==sub;
        }
        return length>=(command==0xA0?7:command==0x12&&sub==1?10:11)&&data[0]==report&&data[1]==command&&(command==0xA0||data[2]==sub);
    }
    private static byte[]? Exchange(string path,byte report,byte command,byte sub,CancellationToken token,int packet=64){
        token.ThrowIfCancellationRequested();
        var h=hid_open_path(path);if(h==0)return null;
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
        }finally{hid_close(h);}
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
    internal static (int Level,bool Charging)? DecodeMouse(byte[]? reply,int scale){
        if(reply==null||scale is not (1 or 25))return null;
        int i=reply.Length>0&&reply[0]==0?1:0;
        if(reply.Length<i+10||reply[i]!=0x12||reply[i+1]!=0x07)return null;
        int raw=reply[i+4],charge=reply[i+9];
        if(charge>1||raw>100/scale||(raw==0&&charge==0))return null;
        return(raw*scale,charge==1);
    }
    internal static (int Level,bool Charging)? DecodeKeyboard(byte[]? reply,int batteryByte){
        if(reply==null||batteryByte is not (6 or 11)||reply.Length<=Math.Max(9,batteryByte)||reply[0]!=2||reply[1]!=0x12||reply[2]!=1)return null;
        int level=reply[batteryByte];bool charging=reply[9]!=0;
        return level>100||(level==0&&!charging)?null:(level,charging);
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
        token.ThrowIfCancellationRequested();var h=hid_open_path(path);if(h==0)return null;
        try{
            byte[] request=new byte[64];byte[] cmd=[0xFF,0x08,0,0xFD,4,0x12,0xF1,3,0x52,1];cmd.CopyTo(request,0);
            if(hid_send_feature_report(h,request,(nuint)request.Length)<0)return null;
            if(token.WaitHandle.WaitOne(35))token.ThrowIfCancellationRequested();
            byte[] reply=new byte[64];reply[0]=0xFF;
            int n=hid_get_feature_report(h,reply,(nuint)reply.Length);
            return n>0?DecodeHeadset(reply[..n]):null;
        }finally{hid_close(h);}
    }
    // Read-only inventory. Does not open a device or send a report. No serial
    // numbers, Bluetooth addresses, instance paths or container IDs exported.
    internal static string InspectHardware(){
        var seen=new HashSet<string>(StringComparer.Ordinal);var head=hid_enumerate(0,0);
        try{
            for(var p=head;p!=0;){
                var d=Marshal.PtrToStructure<HidInfo>(p);p=d.Next;
                if(d.Vid is not (0x046D or 0x0B05 or 0x045E or 0x3537 or 0x054C or 0x057E or 0x2DC8))continue;
                string product=d.Product==0?"":ControllerIdentity.Clean(Marshal.PtrToStringUni(d.Product));
                seen.Add($"{d.Vid:X4}:{d.Pid:X4} · {product} · mi={d.Interface} · {d.UsagePage:X4}:{d.Usage:X4}");
                if(seen.Count>=256)break;
            }
        }finally{if(head!=0)hid_free_enumeration(head);}
        return seen.Count==0?"No matching HID collections":string.Join(Environment.NewLine,seen.OrderBy(x=>x,StringComparer.Ordinal));
    }
    internal static IReadOnlyList<TaskbarBattery> Read()=>Read(CancellationToken.None);
    internal static IReadOnlyList<TaskbarBattery> Read(CancellationToken token){
        var result=new List<TaskbarBattery>();var channels=new List<Channel>();var notes=new List<string>();
        var head=hid_enumerate(0x0B05,0);
        try{
            for(var p=head;p!=0;){
                token.ThrowIfCancellationRequested();
                var d=Marshal.PtrToStructure<HidInfo>(p);p=d.Next;
                if(notes.Count<80)notes.Add($"PID={d.Pid:X4} mi={d.Interface} page={d.UsagePage:X4} usage={d.Usage:X4}");
                var path=Marshal.PtrToStringAnsi(d.Path);if(string.IsNullOrEmpty(path))continue;
                var product=d.Product==0?"":Marshal.PtrToStringUni(d.Product)??"";
                bool candidate=d.Pid==0x1ACE&&IsOmniChannel(d.Interface,d.UsagePage,d.Usage)||
                    PeripheralCatalog.Find(d.Pid,AsusProtocol.DirectMouse)!=null&&d.Interface==0&&d.UsagePage>=0xFF00||
                    d.Pid==0x1C69&&d.Interface==0&&d.UsagePage>=0xFF00||
                    d.Pid==0x1AD0&&path.Contains("mi_02&col03",StringComparison.OrdinalIgnoreCase)&&product.Contains("Harpe",StringComparison.OrdinalIgnoreCase)||
                    PeripheralCatalog.Find(d.Pid,AsusProtocol.StrixGoHeadset)!=null&&HasFeatureChannel(path);
                if(!candidate)continue;
                Guid id=Guid.Empty;var h=hid_open_path(path);
                if(h!=0)try{if(hid_winapi_get_container_id(h,out id)!=0)id=Guid.Empty;}
                catch(EntryPointNotFoundException){id=Guid.Empty;}finally{hid_close(h);}
                if(d.Pid==0x1ACE&&id==Guid.Empty&&ReceiverInstance(path)==null)continue;
                channels.Add(new(path,id,d.Pid,d.UsagePage,d.Usage,d.Interface,product));
            }
        }finally{if(head!=0)hid_free_enumeration(head);}
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
            var c=family.First();var profile=PeripheralCatalog.Find(c.Pid,AsusProtocol.DirectMouse)??PeripheralCatalog.Find(c.Pid,AsusProtocol.SpeednovaMouse)??PeripheralCatalog.Find(c.Pid,AsusProtocol.StrixGoHeadset);
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
                        AsusProtocol.SpeednovaMouse when c.Pid==0x1AD0=>Decode(Exchange(channel.Path,3,0x12,7,token)),
                        AsusProtocol.SpeednovaMouse=>DecodeMouse(Exchange(channel.Path,0,0x12,7,token,64),1),
                        _=>DecodeMouse(Exchange(channel.Path,0,0x12,7,token,65),profile.Scale)
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
