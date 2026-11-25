using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class ControllerAliasMatcherTests
{
    private static readonly DateTimeOffset T=DateTimeOffset.UnixEpoch.AddDays(1);
    private static TaskbarBattery Gip(string key="gip:a")=>new(DeviceType.Gamepad,"Xbox 360 Controller for Windows",100,"已连接（GIP）",false,T,key);
    private static TaskbarBattery Slot(string key="xinput:0")=>new(DeviceType.Gamepad,"XInput Controller",null,"有线或接收器模式（未提供电量）",false,T,key,"USB");
    [Fact]public void ReportedPairBecomesOnePhysicalRingWithGipBattery(){
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(T);
        center.RefreshInventory([Gip(),Slot()],c);
        var snap=center.Advance(c,T);
        var row=Assert.Single(snap.Rows);Assert.Equal(100,row.Level);Assert.Equal(Gip().Name,row.Name);
        Assert.Equal(2,Assert.Single(snap.Groups).Sources.Length);
        Assert.True(Assert.Single(snap.Groups).Inferred);
        Assert.False(c.PhysicalSourceIds.ContainsKey(Slot().Key));
    }
    [Fact]public void ExistingGipLogicalIdAndNameSurviveAutomaticPair(){
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(T);
        center.RefreshInventory([Gip()],c);var id=Assert.Single(center.Advance(c,T).Rows).Key;c.Names[id]="GameSir";
        center.RefreshInventory([Gip(),Slot()],c);
        var row=Assert.Single(center.Advance(c,T).Rows);Assert.Equal(id,row.Key);Assert.Equal("GameSir",row.Name);
    }
    [Fact]public void MultipleLiveSlotsAreNeverGuessed(){
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(T);
        center.RefreshInventory([Gip(),Slot()],c);var id=Assert.Single(center.Advance(c,T).Rows).Key;
        center.RefreshInventory([Gip(),Slot(),Slot("xinput:1")],c);
        var snap=center.Advance(c,T);Assert.Equal(3,snap.Rows.Length);
        Assert.Equal(id,snap.Groups.Single(g=>g.Sources.Any(s=>s.Key=="gip:a")).Reading.Key);
    }
    [Fact]public void MultipleLiveGipControllersAreNeverGuessed(){
        Assert.Equal(3,DeviceIdentityManager.Group([Gip(),Gip("gip:b"),Slot()],new()).Length);
    }
    [Fact]public void InactiveHistoryCanStayTogetherButLiveSlotReuseCannot(){
        var c=new RingPreferences();DeviceIdentityManager.Group([Gip(),Slot()],c);
        var offline=Slot().WithConnection(ConnectionState.Disconnected) with{ShortLabel=null};
        Assert.Single(DeviceIdentityManager.Group([Gip(),offline],c));
        Assert.Single(DeviceIdentityManager.Group([Gip().WithConnection(ConnectionState.Disconnected),offline],c));
        Assert.Equal(2,DeviceIdentityManager.Group([Gip().WithConnection(ConnectionState.Disconnected),Slot()],c).Length);
    }
    [Fact]public void ExplicitSplitAndDisableAreRespected(){
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(T);
        center.RefreshInventory([Gip(),Slot()],c);var group=Assert.Single(center.Advance(c,T).Groups);
        DeviceIdentityManager.Split(c,group);center.Regroup(c);Assert.Equal(2,center.Advance(c,T).Rows.Length);
        Assert.Equal(2,DeviceIdentityManager.Group([Gip(),Slot()],new(){AutoDeduplicate=false}).Length);
    }
    [Fact]public void GenericGipAndUnknownXInputSlotCanBeProvisionallyLinked(){
        var g=Gip() with{Name="HID-compliant game controller",Level=10};
        var x=Slot() with{ShortLabel="—"};
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(T);
        center.RefreshInventory([g,x],c);
        var snap=center.Advance(c,T);
        Assert.Equal(10,Assert.Single(snap.Rows).Level);
        Assert.Equal(2,Assert.Single(snap.Groups).Sources.Length);
        Assert.False(c.PhysicalSourceIds.ContainsKey(x.Key));
        Assert.Equal(3,DeviceIdentityManager.Group([g,Gip("gip:b"),x],new()).Length);
    }
    [Fact]public void ConflictingBluetoothBatteryIsNotGuessedAsSamePhysicalController(){
        var g=Gip() with{Name="HID-compliant game controller",Level=10};
        var x=Slot() with{ShortLabel="—"};
        var ble=new TaskbarBattery(DeviceType.Gamepad,"Xbox Wireless Controller",50,"已连接",false,T,"ble:xbox",PhysicalId:"ble-address:123456789ABC");
        Assert.Equal(2,DeviceIdentityManager.Group([g,x,ble],new()).Length);
    }
    [Fact]public void MicrosoftXboxBlePrefersDirectBatteryAndRetainsBleIdentityOnReconnect(){
        var ble=new TaskbarBattery(DeviceType.Gamepad,"Xbox Wireless Controller",82,"已连接",false,T,"ble:xbox",PhysicalId:"ble-address:123456789ABC");
        var g=Gip() with{Name="HID-compliant game controller",Level=10,HardwareVendorId=0x045e,HardwareProductId=0x0b13};
        var x=Slot() with{ShortLabel="—"};
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(T);
        center.RefreshInventory([ble,g,x],c);
        var first=Assert.Single(center.Advance(c,T).Groups);
        Assert.Equal(82,first.Reading.Level);Assert.Equal(3,first.Sources.Length);
        Assert.Equal("Xbox Wireless Controller",first.Reading.Name);
        center.RefreshInventory([ble,g with{Key="gip:changed"},x],c);
        var second=Assert.Single(center.Advance(c,T).Groups);
        Assert.Equal(first.Reading.Key,second.Reading.Key);
        Assert.Equal(82,second.Reading.Level);
        Assert.False(c.PhysicalSourceIds.ContainsKey("gip:changed"));
        Assert.Equal(3,DeviceIdentityManager.Group([ble,g,Gip("gip:other")],new()).Length);
    }
    [Fact]public void DifferentModelOrWirelessBandDoesNotTriggerUsbHeuristic(){
        Assert.Equal(2,DeviceIdentityManager.Group([Gip() with{Name="Other Controller"},Slot()],new()).Length);
        Assert.Equal(2,DeviceIdentityManager.Group([Gip(),Slot() with{ShortLabel="Med"}],new()).Length);
    }
}
