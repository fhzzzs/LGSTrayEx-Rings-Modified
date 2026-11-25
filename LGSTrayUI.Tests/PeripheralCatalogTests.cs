using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class PeripheralCatalogTests{
    [Theory]
    [InlineData("MX Master 3S",DeviceType.Mouse)]
    [InlineData("ROG Azoth",DeviceType.Keyboard)]
    [InlineData("ROG Delta II",DeviceType.Headset)]
    [InlineData("DualSense Wireless Controller",DeviceType.Gamepad)]
    [InlineData("Nintendo Joy-Con (L)",DeviceType.Gamepad)]
    [InlineData("8BitDo Pro 2",DeviceType.Gamepad)]
    [InlineData("8BitDo Retro Keyboard",DeviceType.Keyboard)]
    public void NamedDevicesAreClassified(string name,DeviceType type)=>Assert.Equal(type,PeripheralCatalog.NamedType(name));
    [Fact]public void ProfilesAreUniqueByTransportAndPid()=>Assert.Equal(PeripheralCatalog.Asus.Count,PeripheralCatalog.Asus.Select(x=>(x.Pid,x.Protocol)).Distinct().Count());
    [Fact]public void UnknownRawInterfacesAreNotGuessed(){
        Assert.True(ControllerIdentity.RawControllerAllowed(0x054C,0x0CE6));
        Assert.False(ControllerIdentity.RawControllerAllowed(0x2DC8,0xFFFF));
        Assert.Equal("Nintendo Joy-Con (R)",ControllerIdentity.Name(0x057E,0x2007,"HID"));
    }
    [Fact]public void EstimatesAreNeverExactAndYieldToExactReadings(){
        var t=DateTimeOffset.UtcNow;var c=new RingPreferences();
        var e=new TaskbarBattery(DeviceType.Mouse,"Mouse",75,"已连接",false,t,"asus:a","≈75%",PhysicalId:"serial:a"){Estimated=true};
        var exact=new TaskbarBattery(DeviceType.Mouse,"Mouse",72,"已连接",false,t,"ble:a",PhysicalId:"serial:a");
        Assert.Equal(ReadingQuality.Band,e.Quality);
        var selector=new BatterySourceSelector();var result=Assert.Single(selector.Select(DeviceIdentityManager.Group([e,exact],c),c,t));
        Assert.Equal(72,result.Reading.Level);
    }
}
