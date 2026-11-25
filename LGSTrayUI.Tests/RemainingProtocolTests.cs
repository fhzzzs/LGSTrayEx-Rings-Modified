using System;
using System.Collections.Generic;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class RemainingProtocolTests{
    [Fact]public void EightBitDoEnhancedReportsNeverTreatPaddingAsEmptyBattery(){
        byte[] data=new byte[64];data[0]=1;data[14]=0x80|75;
        Assert.Equal((75,true,false),ControllerInputBattery.Decode(0x2DC8,0x6006,data,64,true)!.Value);
        Assert.Null(ControllerInputBattery.Decode(0x2DC8,0x6006,data,64,false));
        data[14]=0;Assert.Null(ControllerInputBattery.Decode(0x2DC8,0x6006,data,64,true));
        data[0]=4;data[14]=20;
        Assert.Equal((20,false,false),ControllerInputBattery.Decode(0x2DC8,0x6009,data,64,true)!.Value);
    }
    [Fact]public void BluetoothBasicReportIsNotUsbBattery(){
        byte[] data=new byte[64];data[0]=1;data[53]=0;
        Assert.Null(ControllerInputBattery.Decode(0x054C,0x0CE6,data,64,true));
    }
    [Fact]public void WgiPlaceholderDoesNotFabricateFull(){
        Assert.Null(GipBatteryReader.ReportPercent(1000,1000,null));
        Assert.Equal(90,GipBatteryReader.ReportPercent(1000,900,null));
        Assert.Equal(100,GipBatteryReader.ReportPercent(1000,1000,0));
    }
    [Fact]public void HistoryExpiresOnlyWithConfirmedLiveEvidence(){
        var now=DateTimeOffset.UtcNow;
        var old=new TaskbarBattery(DeviceType.Mouse,"Mouse",null,"离线",false,now.AddDays(-31),"old",PhysicalId:"serial:a");
        var live=new TaskbarBattery(DeviceType.Mouse,"Mouse",80,"已连接",false,now,"new",PhysicalId:"serial:a");
        var unknown=old with{Key="unknown",PhysicalId=null};
        var distinct=old with{Key="different",PhysicalId="serial:b"};
        var current=new Dictionary<string,TaskbarBattery>{{live.Key,live}};
        var c=new RingPreferences();
        Assert.Equal(new[]{"old"},ConfirmedHistoryRetention.Expired([old,unknown,distinct,live],current,c,now));
        c.PreferredSources["device:1"]=old.Key;
        Assert.Empty(ConfirmedHistoryRetention.Expired([old,unknown,distinct,live],current,c,now));
    }
}
