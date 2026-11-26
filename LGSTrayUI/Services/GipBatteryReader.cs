using System;
using System.Collections.Generic;
using LGSTrayPrimitives;
using Windows.Gaming.Input;
using Windows.System.Power;
using Windows.Devices.Power;
using System.Threading;
using System.Threading.Tasks;
namespace LGSTrayUI;
internal static class GipBatteryReader
{
    internal static int? CapacityPercent(int? full,int? remaining)
    {
        if(full is not int f||remaining is not int r||f<=0||r<0||r>f)return null;
        return (int)Math.Round(100d*r/f);
    }
    internal static int? ReportPercent(int? full,int? remaining,int? rate)
        =>full==1000&&remaining==1000&&rate==null?null:CapacityPercent(full,remaining);
    private static string diagnostics="not scanned";
    private static string lastGameSir="No GameSir WGI reports captured in this session";
    internal static string LastGameSir=>System.Threading.Volatile.Read(ref lastGameSir);
    internal static string Diagnostics=>System.Threading.Volatile.Read(ref diagnostics);
    internal static List<TaskbarBattery> Read(bool nativeTelemetry = false)
    {
        var rows=new List<TaskbarBattery>();var notes=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
        void Add(RawGameController raw,Gamepad? gamepad){
            var id=ControllerIdentity.Clean(raw.NonRoamableId??"");
            if(string.IsNullOrWhiteSpace(id)||!seen.Add(id))return;
            var key="gip:"+id;ushort vid=raw.HardwareVendorId,pid=raw.HardwareProductId;
            string name=ControllerIdentity.Name(vid,pid,raw.DisplayName);
            try{
                // Keep the original Gamepad API available even when the newer Raw API
                // returns a value. Raw and Gamepad are observations of the same controller.
                BatteryReport? standard=null,rawReport=null;
                if(gamepad!=null)try{standard=gamepad.TryGetBatteryReport();}catch(Exception ex){notes.Add($"{vid:X4}:{pid:X4} gamepad error {ex.GetType().Name}");}
                bool triedRaw=ControllerIdentity.RawControllerAllowed(vid,pid)||gamepad==null||standard==null||standard.Status==BatteryStatus.NotPresent;
                if(triedRaw)
                    try{rawReport=raw.TryGetBatteryReport();}catch(Exception ex){notes.Add($"{vid:X4}:{pid:X4} raw error {ex.GetType().Name}");}
                int? Level(BatteryReport? r)=>r==null||r.Status==BatteryStatus.NotPresent?null:ReportPercent(r.FullChargeCapacityInMilliwattHours,r.RemainingCapacityInMilliwattHours,r.ChargeRateInMilliwatts);
                if(!triedRaw&&!Level(standard).HasValue)
                    try{rawReport=raw.TryGetBatteryReport();}catch(Exception ex){notes.Add($"{vid:X4}:{pid:X4} raw fallback error {ex.GetType().Name}");}
                void Describe(string route,BatteryReport? r){
                    if(r==null)return;
                    notes.Add($"{vid:X4}:{pid:X4} {route}: {r.RemainingCapacityInMilliwattHours}/{r.FullChargeCapacityInMilliwattHours} mWh; rate={r.ChargeRateInMilliwatts} mW; {r.Status}; level={Level(r)?.ToString()??"unknown"}");
                }
                Describe("gamepad",standard);Describe("raw",rawReport);
                // Prefer the known older API if it has usable data, then raw.
                var report=Level(standard).HasValue?standard:Level(rawReport).HasValue?rawReport:standard??rawReport;
                int? level=Level(report);
                var status=report?.Status==BatteryStatus.Idle&&level==100?"已充满":level.HasValue?"已连接（GIP）":"已连接（GIP 电量不可用）";
                if(report==null)notes.Add($"{vid:X4}:{pid:X4} no battery report from either API");
                bool charging = report?.Status == BatteryStatus.Charging;
                string? band = null;
                var updated = level.HasValue ? DateTimeOffset.UtcNow : DateTimeOffset.MinValue;
                if (ControllerIdentity.IsG7(vid,pid) && !level.HasValue) {
                    if (pid == 0x10A9) {
                        status = "1000 Hz 接收器已连接，未取得实时电量";
                        notes.Add("3537:10A9 XUSB fast mode; Windows capacity unavailable or synthetic; polling rate untouched");
                    } else if (nativeTelemetry) {
                        var native = GameSirGipTelemetry.Instance.Read(raw, out var detail);
                        notes.Add($"{vid:X4}:{pid:X4} native GIP: {detail}");
                        if (native is { } sample) {
                            level = sample.Percent;
                            band = sample.Band;
                            updated = sample.Time;
                            charging = sample.Percent.HasValue ? sample.Charging : charging;
                            status = band != null ? "已连接（GIP 电量档位）" : charging ? "充电中" : "已连接（GIP 原生电量）";
                        }
                    }
                }
                rows.Add(new TaskbarBattery(DeviceType.Gamepad,name,level,status,charging,updated,key,band){HardwareVendorId=vid,HardwareProductId=pid});
            }catch(Exception ex){notes.Add($"{vid:X4}:{pid:X4} {ex.GetType().Name}");rows.Add(new TaskbarBattery(DeviceType.Gamepad,name,null,"已连接（GIP 电量不可用）",false,DateTimeOffset.MinValue,key){HardwareVendorId=vid,HardwareProductId=pid});}
        }
        try{
            foreach(var controller in Gamepad.Gamepads){
                try{var raw=RawGameController.FromGameController(controller);if(raw!=null)Add(raw,controller);}
                catch(Exception ex){DiagnosticLogger.LogWarning("GIP controller discovery: "+ex.Message);}
            }
            // Only add raw-only devices with known controller VID/PID.
            // Do not misclassify arbitrary raw joysticks, mice or vendor interfaces.
            foreach(var raw in RawGameController.RawGameControllers)
                if(ControllerIdentity.RawControllerAllowed(raw.HardwareVendorId,raw.HardwareProductId))Add(raw,null);
        }catch(Exception ex){DiagnosticLogger.LogWarning("GIP: "+ex.Message);throw;}
        finally{
            var gameSir=notes.FindAll(n=>n.StartsWith("3537:",StringComparison.Ordinal));
            if(gameSir.Count>0){var value=string.Join("; ",gameSir);var old=System.Threading.Volatile.Read(ref lastGameSir);if(!old.EndsWith(value,StringComparison.Ordinal))System.Threading.Volatile.Write(ref lastGameSir,$"{DateTimeOffset.UtcNow:O} · {value}");}
            var summary=notes.Count==0?"No active controllers":string.Join("; ",notes);
            if(System.Threading.Interlocked.Exchange(ref diagnostics,summary)!=summary)DiagnosticLogger.Log("WGI battery reports: "+summary);
        }
        return rows;
    }
}
