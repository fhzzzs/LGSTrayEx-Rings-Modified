using System;
using System.Collections.Generic;
using LGSTrayPrimitives;
using Windows.Gaming.Input;
using Windows.System.Power;
namespace LGSTrayUI;
internal static class GipBatteryReader
{
    internal static int? CapacityPercent(int? full,int? remaining)
    {
        if(full is not int f||remaining is not int r||f<=0||r<0||r>f)return null;
        return (int)Math.Round(100d*r/f);
    }
    internal static IEnumerable<TaskbarBattery> Read()
    {
        var rows=new List<TaskbarBattery>();
        try { foreach(var controller in Gamepad.Gamepads) {
            RawGameController? raw;
            try{raw=RawGameController.FromGameController(controller);}
            catch(Exception ex){DiagnosticLogger.LogWarning("GIP controller discovery: "+ex.Message);continue;}
            if(raw==null)continue;
            var key="gip:"+raw.NonRoamableId;
            try {
                var b=controller.TryGetBatteryReport();
                var full=b.FullChargeCapacityInMilliwattHours; var remaining=b.RemainingCapacityInMilliwattHours;
                int? level=b.Status==BatteryStatus.NotPresent?null:CapacityPercent(full,remaining);
                var status=b.Status==BatteryStatus.Idle&&level==100?"已充满":level.HasValue?"已连接（GIP）":"已连接（GIP 电量不可用）";
                rows.Add(new(DeviceType.Gamepad,raw.DisplayName,level,status,b.Status==BatteryStatus.Charging,level.HasValue?DateTimeOffset.UtcNow:DateTimeOffset.MinValue,key));
            }catch{rows.Add(new(DeviceType.Gamepad,raw.DisplayName,null,"已连接（GIP 电量不可用）",false,DateTimeOffset.MinValue,key));}
        }}catch(Exception ex){DiagnosticLogger.LogWarning("GIP: "+ex.Message);throw;}
        return rows;
    }
}
