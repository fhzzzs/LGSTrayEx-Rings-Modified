using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class PhysicalDeviceCenterTests
{
    private static readonly DateTimeOffset T=DateTimeOffset.UnixEpoch;
    private static TaskbarBattery Row(bool online)=>new(DeviceType.Mouse,"Mouse",online?90:null,online?"已连接":"离线",false,T,"mouse");
    [Fact]public void RendererAndNotificationUseSameConfirmedOfflineState(){
        var center=new PhysicalDeviceCenter(T);var p=new RingPreferences();
        center.Inventory["mouse"]=Row(true);center.Regroup(p);Assert.False(Assert.Single(center.Advance(p,T).Rows).Offline);
        center.Inventory["mouse"]=Row(false);center.Regroup(p);
        var transient=center.Advance(p,T.AddSeconds(11));Assert.False(Assert.Single(transient.Rows).Offline);Assert.Empty(transient.Changes);
        var confirmed=center.Advance(p,T.AddSeconds(14));Assert.True(Assert.Single(confirmed.Rows).Offline);Assert.False(Assert.Single(confirmed.Changes).Online);
    }
    [Fact]public void RescanDoesNotRemoveOnlineRingOrEmitFalseNotification(){
        var center=new PhysicalDeviceCenter(T);var p=new RingPreferences();
        center.Inventory["mouse"]=Row(true);center.Regroup(p);center.Advance(p,T);
        center.Suppress(T.AddSeconds(11),TimeSpan.FromSeconds(10));
        center.Inventory["mouse"]=Row(false);center.Regroup(p);
        var held=center.Advance(p,T.AddSeconds(12));Assert.False(Assert.Single(held.Rows).Offline);Assert.Empty(held.Changes);
        center.Inventory["mouse"]=Row(true);center.Regroup(p);
        Assert.Empty(center.Advance(p,T.AddSeconds(16)).Changes);
        Assert.Empty(center.Advance(p,T.AddSeconds(22)).Changes);
    }
    [Fact]public void ShortDisconnectNeverChangesConfirmedUiState(){
        var center=new PhysicalDeviceCenter(T);var p=new RingPreferences();
        center.Inventory["mouse"]=Row(true);center.Regroup(p);center.Advance(p,T);
        center.Inventory["mouse"]=Row(false);center.Regroup(p);center.Advance(p,T.AddSeconds(11));
        center.Inventory["mouse"]=Row(true);center.Regroup(p);
        var restored=center.Advance(p,T.AddSeconds(12));Assert.False(Assert.Single(restored.Rows).Offline);Assert.Empty(restored.Changes);
    }
}
