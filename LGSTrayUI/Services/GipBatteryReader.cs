using System;
using System.Collections.Generic;
using LGSTrayPrimitives;
using Windows.Gaming.Input;
using Windows.System.Power;
namespace LGSTrayUI;
internal static class GipBatteryReader
{
    internal static IEnumerable<TaskbarBattery> Read()
    {
        var rows=new List<TaskbarBattery>();
        try { foreach(var controller in Gamepad.Gamepads) {
            var raw=RawGameController.FromGameController(controller);
            if(raw==null)continue;
            var key="gip:"+raw.NonRoamableId;
            try {
                var b=controller.TryGetBatteryReport();
                var full=b.FullChargeCapacityInMilliwattHours; var remaining=b.RemainingCapacityInMilliwattHours;
                int? level=full is int f && f>0 && remaining is int r && r>=0 && b.Status!=BatteryStatus.NotPresent ? (int)Math.Clamp(Math.Round(100d*r/f),0,100):null;
                rows.Add(new(DeviceType.Gamepad,raw.DisplayName,level,level.HasValue?"已连接（GIP）":"已连接（GIP 电量不可用）",b.Status==BatteryStatus.Charging,level.HasValue?DateTimeOffset.UtcNow:DateTimeOffset.MinValue,key));
            }catch{rows.Add(new(DeviceType.Gamepad,raw.DisplayName,null,"已连接（GIP 电量不可用）",false,DateTimeOffset.MinValue,key));}
        }}catch(Exception ex){DiagnosticLogger.LogWarning("GIP: "+ex.Message);}
        return rows;
    }
}
