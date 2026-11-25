using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class NativeControllerIdentityTests{
    static TaskbarBattery Row(string key,bool connected=true)=>new TaskbarBattery(DeviceType.Gamepad,"Sony DualSense",connected?75:null,connected?"已连接":"离线",false,DateTimeOffset.UtcNow,key){HardwareVendorId=0x054C,HardwareProductId=0x0CE6};
    [Fact]public void OfflineHistoryAloneIsNotEvidenceForNewDevice(){
        var c=new RingPreferences();
        Assert.Equal(2,DeviceIdentityManager.Group([Row("controller-hid:a",false),Row("gip:b")],c).Length);
        Assert.Single(DeviceIdentityManager.Group([Row("controller-hid:a"),Row("gip:b")],c));
        Assert.Single(DeviceIdentityManager.Group([Row("controller-hid:a",false),Row("gip:b")],c));
    }
    [Fact]public void MultipleUnitsDisableModelBasedAssociation(){
        var c=new RingPreferences();
        Assert.Equal(3,DeviceIdentityManager.Group([Row("controller-hid:a"),Row("controller-hid:b"),Row("gip:c")],c).Length);
        Assert.Equal(2,DeviceIdentityManager.Group([Row("controller-hid:a"),Row("gip:c")],c).Length);
    }
}
