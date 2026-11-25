using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings104Tests{
    private static readonly DateTimeOffset T=DateTimeOffset.UnixEpoch.AddDays(1);
    private static TaskbarBattery G7(string key,bool online=true)=>new TaskbarBattery(DeviceType.Gamepad,"Xbox 360 Controller for Windows",online?75:null,online?"已连接":"离线",false,T,key){HardwareVendorId=0x3537,HardwareProductId=0x10A9};
    private static TaskbarBattery Slot()=>new(DeviceType.Gamepad,"XInput Controller",null,"已连接",false,T,"xinput:0","Med");
    [Fact]public void NulMigrationPreservesSettings(){
        var c=new RingPreferences();
        c.PhysicalSourceIds["gip:old\0"]="device:old";
        c.ControllerHistoryHints["xinput:0"]="gip:old\0";
        c.SeparateDeviceSources.Add("gip:split\0");
        ControllerIdentity.Migrate(c);
        Assert.Equal("device:old",c.PhysicalSourceIds["gip:old"]);
        Assert.Equal("gip:old",c.ControllerHistoryHints["xinput:0"]);
        Assert.Contains("gip:split",c.SeparateDeviceSources);
        var row=ControllerIdentity.Normalize(G7("gip:old\0"));
        Assert.Equal("gip:old",row.Key);Assert.Equal("GameSir G7 Pro",row.Name);
        Assert.Equal("abcdef",ControllerIdentity.Clean("abc\0def"));
        Assert.DoesNotContain('\0',ControllerIdentity.Clean("abc\0def"));
    }
    [Fact]public void ConfirmedModelUsesOneReversibleHistoryAcrossReconnect(){
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(T);
        center.RefreshInventory([G7("gip:old\0"),Slot()],c);
        var first=Assert.Single(center.Advance(c,T).Rows);
        Assert.Equal("GameSir G7 Pro",first.Name);Assert.Equal(75,first.Level);
        center.RefreshInventory([G7("gip:new\0"),Slot()],c);
        var next=Assert.Single(center.Advance(c,T).Groups);
        Assert.Equal(first.Key,next.Reading.Key);Assert.Equal(3,next.Sources.Length);
        Assert.True(next.Inferred);
        Assert.False(c.PhysicalSourceIds.ContainsKey("gip:new"));
    }
    [Fact]public void ConcurrentSameModelControllersStaySeparate(){
        var c=new RingPreferences();
        var a=ControllerIdentity.Normalize(G7("gip:a"));
        var b=ControllerIdentity.Normalize(G7("gip:b"));
        Assert.Equal(2,DeviceIdentityManager.Group([a,b],c).Length);
        Assert.Equal(2,DeviceIdentityManager.Group([a,b.WithConnection(ConnectionState.Disconnected)],c).Length);
    }
    [Fact]public void ConflictingHardwareAndExplicitSplitStaySeparate(){
        var a=ControllerIdentity.Normalize(G7("gip:a",false)) with{PhysicalId="serial:a"};
        var b=ControllerIdentity.Normalize(G7("gip:b")) with{PhysicalId="serial:b"};
        Assert.Equal(2,DeviceIdentityManager.Group([a,b],new()).Length);
        var c=new RingPreferences();c.SeparateDeviceSources.Add(a.Key);
        Assert.Equal(2,DeviceIdentityManager.Group([a with{PhysicalId=null},b with{PhysicalId=null}],c).Length);
    }
    [Fact]public void GroupComparisonIgnoresTimestampsButNotMembership(){
        var d=G7("gip:a");
        var group=new DeviceGroup(d,[d],false,true);
        Assert.True(TaskbarBatteryWindow.SameGrouping([group],[group with{Sources=[d with{Updated=T.AddSeconds(1)}]}]));
        Assert.False(TaskbarBatteryWindow.SameGrouping([group],[group with{Sources=[d,G7("gip:b")]}]));
    }
}
