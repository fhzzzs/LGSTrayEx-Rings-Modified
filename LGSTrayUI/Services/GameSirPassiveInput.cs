using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// Listen only to the vendor collection. Existing keyboard, mouse and gamepad
// registrations, device modes and foreground delivery belong to their owners.
internal sealed class GameSirPassiveInput : IDisposable
{
    private const ushort Page = 0xFFF0, Usage = 0x0040;
    [StructLayout(LayoutKind.Sequential)]
    private struct Registration { internal ushort Page, Usage; internal uint Flags; internal nint Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header { internal uint Type, Size; internal nint Device, WParam; }
    [DllImport("user32", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(ref Registration device, uint count, uint size);
    [DllImport("user32")]
    private static extern unsafe uint GetRawInputData(nint input, uint command, void* buffer, ref uint size, uint headerSize);
    [DllImport("user32", EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern unsafe uint GetRawInputDeviceInfo(nint device, uint command, void* buffer, ref uint size);

    private sealed class Channel(string path, ushort pid) {
        internal readonly string Path = path;
        internal readonly ushort Pid = pid;
        internal TaskbarBattery? Reading;
    }
    private readonly object gate = new();
    private readonly Dictionary<nint,Channel?> channels = new();
    private readonly Dictionary<string,TaskbarBattery> previous = new(StringComparer.OrdinalIgnoreCase);
    private HwndSource? source;
    private bool requested,registered;
    private int generation;
    private long packets,valid;
    private string registrationStatus = "disabled";
    internal string Diagnostics { get { lock(gate) return $"{registrationStatus}; vendor reports={packets}; valid battery reports={valid}"; } }

    internal void Attach(HwndSource window) { source = window; window.AddHook(Message); }
    internal void Enable(bool enable)
    {
        if (source == null || enable == requested) return;
        requested=enable;
        var device = new Registration { Page=Page, Usage=Usage, Flags=enable ? 0x2100u : 1u, Target=enable ? source.Handle : 0 };
        bool ok = (!enable && !registered) || RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<Registration>());
        lock (gate) {
            registrationStatus = ok ? enable ? "listening FFF0:0040" : "disabled" : $"registration error {Marshal.GetLastWin32Error()}";
            registered = enable && ok;
            generation++;
            channels.Clear();
            previous.Clear();
        }
    }
    private static unsafe Channel? Identify(nint handle)
    {
        uint infoSize=32;
        uint* info=stackalloc uint[8]; info[0]=infoSize;
        if(GetRawInputDeviceInfo(handle,0x2000000B,info,ref infoSize)==uint.MaxValue ||
           info[1]!=2 || info[2]!=0x3537 || info[3]>ushort.MaxValue ||
           !ControllerInputBattery.Supports(0x3537,(ushort)info[3])) return null;
        uint length=0;
        if(GetRawInputDeviceInfo(handle,0x20000007,null,ref length)==uint.MaxValue || length is 0 or >2048) return null;
        char* path=stackalloc char[(int)length+1];
        if(GetRawInputDeviceInfo(handle,0x20000007,path,ref length)==uint.MaxValue) return null;
        path[length]='\0';
        return new(new string(path),(ushort)info[3]);
    }
    private unsafe nint Message(nint hwnd,int msg,nint wp,nint lp,ref bool handled)
    {
        if(!registered)return 0;
        if(msg==0x00FE && wp==2) {
            lock(gate) { if(channels.Remove(lp,out var old) && old!=null) previous.Remove(old.Path); }
        } else if(msg==0x00FF) {
            const int capacity=4096;
            byte* bytes=stackalloc byte[capacity]; uint size=capacity;
            uint n=GetRawInputData(lp,0x10000003,bytes,ref size,(uint)sizeof(Header));
            if(n>capacity || n<sizeof(Header)+8 || ((Header*)bytes)->Type!=2)return 0;
            var handle=((Header*)bytes)->Device;
            uint* fields=(uint*)(bytes+sizeof(Header));
            uint width=fields[0], count=fields[1];
            if(width is 0 or >256 || (ulong)count*width>n-(uint)sizeof(Header)-8)return 0;
            lock(gate) {
                if(!channels.TryGetValue(handle,out var channel))channels[handle]=channel=Identify(handle);
                if(channel==null)return 0;
                packets+=count;
                for(uint i=0;i<count;i++) {
                    var data=new ReadOnlySpan<byte>(bytes+sizeof(Header)+8+i*width,(int)width);
                    if(ControllerInputBattery.DecodeGameSir(channel.Pid,data) is not {} reading)continue;
                    valid++;
                    var last=channel.Reading;
                    var now=DateTimeOffset.UtcNow;
                    if(last!=null && last.Level==reading.Level && last.Charging==reading.Charging &&
                       last.IsFull==reading.Full && now-last.Updated<TimeSpan.FromSeconds(5))continue;
                    channel.Reading=new(DeviceType.Gamepad,ControllerIdentity.KnownName(0x3537,channel.Pid)!,reading.Level,
                        reading.Full?"已充满":reading.Charging?"充电中":"已连接",reading.Charging,now,
                        $"controller-hid:3537:{channel.Pid:X4}:{channel.Path.ToLowerInvariant()}"){
                        HardwareVendorId=0x3537,HardwareProductId=channel.Pid};
                }
            }
        }
        return 0; // WPF/DefWindowProc still owns normal WM_INPUT cleanup.
    }

    internal static TaskbarBattery WhilePresent(TaskbarBattery reading, DateTimeOffset now) =>
        now - reading.Updated <= TimeSpan.FromSeconds(35) ? reading :
        reading with { Level=null, LastKnownLevel=reading.Level ?? reading.LastKnownLevel, Stale=true, Charging=false, ShortLabel=null };

    internal IReadOnlyList<TaskbarBattery> Read(CancellationToken token)
    {
        int scanGeneration;
        lock(gate) scanGeneration=generation;
        var present = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var polled = AsusBatteryReader.ReadControllerInput(token,true,present);
        token.ThrowIfCancellationRequested();
        lock(gate) {
            if(scanGeneration!=generation)return [];
            var rows=polled.ToDictionary(r=>r.Key,StringComparer.Ordinal);
            var now=DateTimeOffset.UtcNow;
            foreach(var c in channels.Values) {
                if(c?.Reading is not {} reading)continue;
                var key=present.GetValueOrDefault(c.Path) ?? previous.GetValueOrDefault(c.Path)?.Key ?? reading.Key;
                present[c.Path]=key;
                if(!rows.TryGetValue(key,out var other)||other.Updated<reading.Updated)
                    rows[key]=WhilePresent(reading with {Key=key},now);
            }
            foreach(var pair in present) {
                if(rows.TryGetValue(pair.Value,out var row))previous[pair.Key]=row;
                else if(previous.TryGetValue(pair.Key,out var old))rows[pair.Value]=WhilePresent(old,now);
            }
            foreach(var path in previous.Keys.Where(k=>!present.ContainsKey(k)).ToArray())previous.Remove(path);
            return rows.Values.OrderBy(r=>r.Key,StringComparer.Ordinal).ToArray();
        }
    }
    public void Dispose() { Enable(false); source?.RemoveHook(Message); source=null; }
}
