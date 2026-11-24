using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings8Tests
{
    [Fact] public void CenteringDoesNotMoveLeftAnchor(){
        var slot=TaskbarLayoutGeometry.Dimensions(80,16,16,0);
        Assert.Equal(16,slot.before);
        Assert.Equal(165,TaskbarLayoutGeometry.PreviewAppStart(true,false,slot.total,784,136));
        Assert.Equal(324,TaskbarLayoutGeometry.PreviewAppStart(true,true,slot.total,784,136));
    }
    [Fact] public void ConfirmedAliasesPersistAcrossProviderSwitch(){
        var c=new RingPreferences();
        var a=new TaskbarBattery(DeviceType.Mouse,"Mouse",85,"已连接",false,DateTimeOffset.UtcNow,"logi:a",PhysicalId:"physical:a");
        var b=a with{Key="ble:b",Level=84};c.Order.Add(a.Key);c.Names[b.Key]="My mouse";
        Assert.Equal(a.Key,Assert.Single(DeviceIdentityManager.Group([a,b],c)).Reading.Key);
        Assert.Equal(a.Key,Assert.Single(DeviceIdentityManager.Group([b],c)).Reading.Key);
        Assert.Equal("My mouse",c.Names[a.Key]);
        c.AutoDeduplicate=false;Assert.Equal(b.Key,Assert.Single(DeviceIdentityManager.Group([b],c)).Reading.Key);
    }
    [Fact] public void HardwareSerialOnlyMatchesExactSerial(){
        const string serial="123ABC456DE";
        Assert.Equal(HardwareIdentity.LogitechSerial(serial),HardwareIdentity.LogitechSerial(Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(serial)),true));
        Assert.Null(HardwareIdentity.LogitechSerial("0000000000000000000000",true));
        Assert.Null(HardwareIdentity.LogitechSerial("unknown"));
    }
}
