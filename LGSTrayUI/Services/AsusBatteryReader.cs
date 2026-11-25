using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace LGSTrayUI;

// Read-only status requests for the ROG OMNI receiver. Protocol reference:
// https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md
// Harpe Ace Mini product ID: https://github.com/seerge/g-helper/discussions/4321
internal static class AsusBatteryReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct HidInfo
    {
        public nint Path; public ushort Vid, Pid; public nint Serial; public ushort Release;
        public nint Manufacturer, Product; public ushort UsagePage, Usage; public int Interface;
        public nint Next; public int Bus;
    }
    [DllImport("hidapi", CallingConvention=CallingConvention.Cdecl)] private static extern nint hid_enumerate(ushort vid,ushort pid);
    [DllImport("hidapi", CallingConvention=CallingConvention.Cdecl)] private static extern void hid_free_enumeration(nint p);
    [DllImport("hidapi", CallingConvention=CallingConvention.Cdecl)] private static extern nint hid_open_path([MarshalAs(UnmanagedType.LPStr)]string path);
    [DllImport("hidapi", CallingConvention=CallingConvention.Cdecl)] private static extern void hid_close(nint h);
    [DllImport("hidapi", CallingConvention=CallingConvention.Cdecl)] private static extern int hid_write(nint h,byte[] data,nuint len);
    [DllImport("hidapi", CallingConvention=CallingConvention.Cdecl)] private static extern int hid_read_timeout(nint h,[Out]byte[] data,nuint len,int ms);
    [DllImport("hidapi", CallingConvention=CallingConvention.Cdecl)] private static extern int hid_winapi_get_container_id(nint h,out Guid id);
    private sealed record Channel(string Path,Guid Container,ushort Page,ushort Usage);
    private static string diagnostics="not scanned";
    internal static string Diagnostics=>System.Threading.Volatile.Read(ref diagnostics);
    internal static string? ReceiverInstance(string path)
    {
        // The receiver's HID collections have different collection suffixes but
        // share the instance stem. Never group by VID/PID alone.
        var parts=path.ToLowerInvariant().Split('#');
        if(parts.Length<3||!parts[1].Contains("vid_0b05&pid_1ace")||!parts[1].Contains("mi_02"))return null;
        int last=parts[2].LastIndexOf('&');
        if(last<=0||parts[2].Length-last!=5||!ushort.TryParse(parts[2][(last+1)..],System.Globalization.NumberStyles.HexNumber,null,out _))return null;
        return parts[2][..last];
    }
    internal static bool ExpectedReply(byte[] data,int length,byte report,byte command,byte sub)
        =>length>=9&&data[0]==report&&data[1]==command&&(command==0xA0||data[2]==sub);
    private static byte[]? Exchange(string path,byte report,byte command,byte sub)
    {
        var h=hid_open_path(path);if(h==0)return null;
        try {
            var req=new byte[64];req[0]=report;req[1]=command;req[2]=sub;
            var data=new byte[64];
            // Discard stale input, bounded; never consume an unbounded stream.
            for(int i=0;i<16;i++)if(hid_read_timeout(h,data,(nuint)data.Length,0)<=0)break;
            for(int attempt=0;attempt<3;attempt++){
                if(hid_write(h,req,(nuint)req.Length)!=req.Length)return null;
                var until=Environment.TickCount64+350;
                while(Environment.TickCount64<until){
                    int n=hid_read_timeout(h,data,(nuint)data.Length,(int)Math.Max(1,until-Environment.TickCount64));
                    if(n<=0)break;
                    if(ExpectedReply(data,n,report,command,sub))return data[..n];
                }
            }
            return null;
        }finally{hid_close(h);}
    }
    internal static bool PairedHarpe(byte[]? reply)
    {
        if(reply==null||reply.Length<7||reply[0]!=1||reply[1]!=0xA0)return false;
        for(int i=5;i+1<reply.Length;i+=4){int pid=reply[i]|(reply[i+1]<<8);if(pid==0)break;if(pid==0x1B65)return true;}return false;
    }
    internal static (int Level,bool Charging)? Decode(byte[]? reply)
    {
        if(reply==null||reply.Length<11||reply[0]!=3||reply[1]!=0x12||reply[2]!=0x07||reply[5]>100||reply[10]>1)return null;
        if(reply[5]==0&&reply[10]==0)return null;return(reply[5],reply[10]==1);
    }
    internal static IReadOnlyList<TaskbarBattery> Read()
    {
        var result=new List<TaskbarBattery>();var channels=new List<Channel>();var notes=new List<string>();
        var head=hid_enumerate(0x0B05,0x1ACE);
        try{
            for(var p=head;p!=0;){var d=Marshal.PtrToStructure<HidInfo>(p);p=d.Next;
                notes.Add($"HID mi={d.Interface} page={d.UsagePage:X4} usage={d.Usage:X4}");
                if(d.Interface!=2||!((d.UsagePage==0xFF02&&d.Usage==1)||(d.UsagePage==0xFF01&&d.Usage==3)))continue;
                var path=Marshal.PtrToStringAnsi(d.Path);if(string.IsNullOrEmpty(path))continue;
                Guid id=Guid.Empty;
                var h=hid_open_path(path);
                if(h!=0)try{if(hid_winapi_get_container_id(h,out id)!=0)id=Guid.Empty;}
                    catch(EntryPointNotFoundException){id=Guid.Empty;}finally{hid_close(h);}
                else notes.Add($"open failed page={d.UsagePage:X4}");
                if(id!=Guid.Empty||ReceiverInstance(path)!=null)channels.Add(new(path,id,d.UsagePage,d.Usage));
            }
        }finally{if(head!=0)hid_free_enumeration(head);}
        // Prefer a validated HID instance stem; fall back to an exact container ID.
        foreach(var group in channels.GroupBy(c=>ReceiverInstance(c.Path) is string stem?"instance:"+stem:"container:"+c.Container.ToString("N"))){
            var pairing=group.Where(c=>c.Page==0xFF02).ToArray();var mouse=group.Where(c=>c.Page==0xFF01).ToArray();
            if(pairing.Length!=1||mouse.Length!=1){notes.Add($"incomplete/ambiguous receiver: pair={pairing.Length}, mouse={mouse.Length}");continue;}
            var pairs=Exchange(pairing[0].Path,1,0xA0,0);
            if(!PairedHarpe(pairs)){notes.Add(pairs==null?"pair list timeout":"pair list did not identify Harpe Ace Mini");continue;}
            notes.Add("Harpe Ace Mini pairing confirmed");
            var reading=Decode(Exchange(mouse[0].Path,3,0x12,0x07));
            var id=pairing[0].Container!=Guid.Empty?pairing[0].Container:mouse[0].Container;
            string key="asus:harpe-mini:"+(id!=Guid.Empty?id.ToString("N"):group.Key);
            notes.Add(reading.HasValue?"battery reply accepted":"battery reply absent/invalid");
            if(reading is { } value){var (level,charging)=value;result.Add(new(DeviceType.Mouse,"ROG Harpe Ace Mini",level,charging?(level==100?"已充满":"充电中"):"已连接",charging,DateTimeOffset.Now,key));}
            else result.Add(new(DeviceType.Mouse,"ROG Harpe Ace Mini",null,"离线",false,DateTimeOffset.MinValue,key));
        }
        System.Threading.Volatile.Write(ref diagnostics,notes.Count==0?"No OMNI 0B05:1ACE HID interfaces found":string.Join("; ",notes));
        return result;
    }
}
