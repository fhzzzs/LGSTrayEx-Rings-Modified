using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class LiveDeviceDisplayTests{
    [Fact]public void ConnectionOnlyNeverPretendsToBeZeroPercent(){
        var d=new TaskbarBattery(DeviceType.Gamepad,"G7 Pro",null,"已连接",false,DateTimeOffset.UtcNow,"gip:a");
        Assert.True(d.ConnectionOnly);Assert.Equal("ON",d.RingLabel);
        Assert.Equal("N/A",(d with{Stale=true}).RingLabel);
        Assert.Equal("…",d.WithConnection(ConnectionState.Reconnecting).RingLabel);
        Assert.Equal("Off",d.WithConnection(ConnectionState.Disconnected).RingLabel);
        Assert.Equal("0%",(d with{Level=0}).RingLabel);
        Assert.Equal("Low",(d with{ShortLabel="Low"}).RingLabel);
        Assert.Equal("ON",(d with{ShortLabel="USB"}).RingLabel);
        Assert.False((d with{ShortLabel="Low"}).ConnectionOnly);
        Assert.Equal("N/A",(d with{Charging=true}).RingLabel);
    }
    [Fact]public void ObsoleteActiveGameSirQueryCannotEnablePassiveReader(){
        var c=RingSettingsStore.Parse("{\"GameSirCycloneQuery\":true}");
        Assert.False(c.GameSirPassiveEnabled);
        Assert.DoesNotContain("GameSirCycloneQuery",RingSettingsStore.Snapshot(c));
        c.GameSirPassiveEnabled=true;
        Assert.True(RingSettingsStore.Parse(RingSettingsStore.Snapshot(c)).GameSirPassiveEnabled);
    }
}
