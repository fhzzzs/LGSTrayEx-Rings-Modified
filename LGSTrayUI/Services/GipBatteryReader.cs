using System;
using System.Collections.Generic;
using LGSTrayPrimitives;
using Windows.Gaming.Input;
using Windows.System.Power;
using Windows.Devices.Power;
namespace LGSTrayUI;
internal static class GipBatteryReader
{
    internal static int? CapacityPercent(int? full,int? remaining)
    {
        if(full is not int f||remaining is not int r||f<=0||r<0||r>f)return null;
        return (int)Math.Round(100d*r/f);
    }
    private static string diagnostics="not scanned";
    internal static string Diagnostics=>System.Threading.Volatile.Read(ref diagnostics);
    internal static IEnumerable<TaskbarBattery> Read()
    {
        var rows=new List<TaskbarBattery>();var notes=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
        void Add(RawGameController raw,Gamepad? gamepad){
            var id=ControllerIdentity.Clean(raw.NonRoamableId??"");
            if(string.IsNullOrWhiteSpace(id)||!seen.Add(id))return;
            var key="gip:"+id;ushort vid=raw.HardwareVendorId,pid=raw.HardwareProductId;
            string name=ControllerIdentity.Name(vid,pid,raw.DisplayName);
            try{
                // G7 Pro uses the raw Windows Gaming Input battery report.
                // No vendor writes, driver replacement or mode changes.
                BatteryReport? report=null;string route="raw";
                if(ControllerIdentity.RawControllerAllowed(vid,pid)||gamepad==null){
                    try{report=raw.TryGetBatteryReport();}catch(Exception ex){notes.Add($"{vid:X4}:{pid:X4} raw error {ex.GetType().Name}");}
                }
                int? Level(BatteryReport? r)=>r==null||r.Status==BatteryStatus.NotPresent?null:CapacityPercent(r.FullChargeCapacityInMilliwattHours,r.RemainingCapacityInMilliwattHours);
                int? level=Level(report);
                if(!level.HasValue&&gamepad!=null){
                    try{var fallback=gamepad.TryGetBatteryReport();if(report==null||Level(fallback).HasValue){report=fallback;level=Level(report);route="gamepad";}}
                    catch(Exception ex){notes.Add($"{vid:X4}:{pid:X4} gamepad error {ex.GetType().Name}");}
                }
                var status=report?.Status==BatteryStatus.Idle&&level==100?"已充满":level.HasValue?"已连接（GIP）":"已连接（GIP 电量不可用）";
                notes.Add($"{vid:X4}:{pid:X4} {route}: {report?.RemainingCapacityInMilliwattHours}/{report?.FullChargeCapacityInMilliwattHours} mWh; {report?.Status}; level={level?.ToString()??"unknown"}");
                rows.Add(new TaskbarBattery(DeviceType.Gamepad,name,level,status,report?.Status==BatteryStatus.Charging,level.HasValue?DateTimeOffset.UtcNow:DateTimeOffset.MinValue,key){HardwareVendorId=vid,HardwareProductId=pid});
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
        finally{System.Threading.Volatile.Write(ref diagnostics,notes.Count==0?"No active controllers":string.Join("; ",notes));}
        return rows;
    }
}
