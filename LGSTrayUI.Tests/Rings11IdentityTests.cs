using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings11IdentityTests
{
    private static readonly DateTimeOffset T=DateTimeOffset.UnixEpoch.AddDays(1);
    private static TaskbarBattery Row(string key,string? physical=null)=>new(DeviceType.Gamepad,"Controller",80,"已连接",false,T,key,PhysicalId:physical);
    [Fact]public void IdentitySurvivesProviderChangeAndConfigRoundTrip(){
        var c=new RingPreferences();var a=new PhysicalDeviceCenter(T);
        a.RefreshInventory([Row("gip:a","serial:1")],c);var id=Assert.Single(a.Advance(c,T).Rows).Key;c.Names[id]="My controller";
        c=RingSettingsStore.Parse(System.Text.Json.JsonSerializer.Serialize(c));var b=new PhysicalDeviceCenter(T);
        b.RefreshInventory([Row("ble:b","serial:1")],c);var row=Assert.Single(b.Advance(c,T).Rows);
        Assert.Equal(id,row.Key);Assert.Equal("My controller",row.Name);
    }
    [Fact]public void UnrelatedControllerProvidersRemainAvailable(){
        var c=new RingPreferences();var a=new PhysicalDeviceCenter(T);
        a.RefreshInventory([Row("gip:a"),Row("xinput:0")],c);
        Assert.Equal(2,a.Advance(c,T).Rows.Length);
    }
    [Fact]public void ExpiredBatteryIsNotReportedAsLive(){
        var c=new RingPreferences();var a=new PhysicalDeviceCenter(T);
        a.RefreshInventory([Row("gip:a")],c);var row=Assert.Single(a.Advance(c,T.AddMinutes(5)).Rows);
        Assert.True(row.Stale);Assert.Null(row.Level);Assert.Equal(80,row.LastKnownLevel);Assert.True(row.IsConnected);
    }
    [Fact]public void SplitDoesNotReuseSameLogicalKey(){
        var c=new RingPreferences();var a=new PhysicalDeviceCenter(T);
        a.RefreshInventory([Row("gip:a","serial:1"),Row("ble:b","serial:1")],c);
        var group=Assert.Single(a.Advance(c,T).Groups);DeviceIdentityManager.Split(c,group);
        a.Regroup(c);Assert.Equal(2,a.Advance(c,T).Rows.Select(x=>x.Key).Distinct().Count());
    }
}
