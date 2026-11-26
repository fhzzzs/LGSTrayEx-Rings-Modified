using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class GameSirPerformanceTests{
    [Theory][InlineData(0x10A9)][InlineData(0x105E)][InlineData(0x109C)]
    public void G7KnownEditionUsesWgiAndRejectsEmptyNativeReport(int pid){
        Assert.Equal("GameSir G7 Pro",ControllerIdentity.KnownName(0x3537,(ushort)pid));
        Assert.True(ControllerInputBattery.Supports(0x3537,(ushort)pid));
        byte[] empty=new byte[64];Assert.Null(ControllerInputBattery.Decode(0x3537,(ushort)pid,empty,64));
    }
    [Fact]public void CycloneAcceptsOnlyLiveStateReports(){
        byte[] frame=new byte[64];frame[0]=0x12;frame[36]=77;frame[35]=1;
        Assert.Null(ControllerInputBattery.Decode(0x3537,0x100B,frame,64));
        frame[1]=128;Assert.Equal((77,true,false),ControllerInputBattery.Decode(0x3537,0x100B,frame,64)!.Value);
        frame[36]=101;Assert.Null(ControllerInputBattery.Decode(0x3537,0x100B,frame,64));
        frame[36]=50;frame[35]=2;Assert.Null(ControllerInputBattery.Decode(0x3537,0x100B,frame,64));
        Assert.Null(ControllerInputBattery.Decode(0x3537,0x10A9,frame,64));
    }
    [Fact]public void SharedPidNotAssumedToBeG7(){
        Assert.False(ControllerIdentity.IsG7(0x3537,0x1004));
    }
    [Fact]public void VisualComparisonIgnoresOnlyTimestamp(){
        var a=new TaskbarBattery(DeviceType.Gamepad,"GameSir G7 Pro",72,"已连接",false,DateTimeOffset.UtcNow,"gip:a");
        Assert.True(TaskbarBattery.SamePresentation(a,a with{Updated=a.Updated.AddSeconds(1)}));
        Assert.False(TaskbarBattery.SamePresentation(a,a with{Level=73}));
        Assert.False(TaskbarBattery.SamePresentation(a,a with{Estimated=true}));
        Assert.False(TaskbarBattery.SamePresentation(a,a.WithConnection(ConnectionState.Reconnecting)));
    }
    [Fact]public void ResumeInvalidatesOldPercentWithoutFabricatingDisconnect(){
        var a=new TaskbarBattery(DeviceType.Gamepad,"GameSir G7 Pro",72,"已连接",true,DateTimeOffset.UtcNow,"gip:a");
        var b=ProviderScheduler.InvalidateForResume(a);
        Assert.Equal(ConnectionState.Reconnecting,b.Connection);
        Assert.Null(b.Level);Assert.Equal(72,b.LastKnownLevel);Assert.True(b.Stale);Assert.False(b.Charging);
    }
    [Fact]public void StartupStaggerAndStablePollsAreBounded(){
        Assert.Equal(0,ProviderScheduler.InitialDelayMs("gip"));
        Assert.InRange(ProviderScheduler.InitialDelayMs("controller-hid"),1,2500);
        Assert.Equal(15,ProviderScheduler.AdaptiveInterval("controller-hid",TimeSpan.FromSeconds(10),3).TotalSeconds);
        Assert.Equal(2,ProviderScheduler.AdaptiveInterval("gip",TimeSpan.FromSeconds(2),100).TotalSeconds);
    }
    [Fact]
    public void NativeGipSeparatesStatusBandsFromVendorPercentages()
    {
        var time=DateTimeOffset.UnixEpoch;
        var status=GameSirGipTelemetry.Decode(3,new byte[]{0x8B,0,0,0},time);
        Assert.Equal("4/4",status!.Value.Band);
        Assert.Null(status.Value.Percent);
        Assert.Null(GameSirGipTelemetry.Decode(3,new byte[]{0x80,0,0,0},time));
        var payload=new byte[60];payload[0]=0xE0;payload[28]=1;payload[29]=63;
        var exact=GameSirGipTelemetry.Decode(0x10,payload,time);
        Assert.Equal(63,exact!.Value.Percent);
        Assert.True(exact.Value.Charging);
        Assert.Null(GameSirGipTelemetry.Decode(0x20,payload,time));
    }
    [Fact]
    public void FastReceiverRejectsWiredPlaceholderAndInvalidDeviceProperty()
    {
        Assert.Null(GipBatteryReader.ReportPercent(1000,1000,null));
        Assert.Null(GameSirFastBattery.PublishedPercent(null));
        Assert.Null(GameSirFastBattery.PublishedPercent((byte)255));
        Assert.Equal(73,GameSirFastBattery.PublishedPercent((byte)73));
        Assert.Equal(0,GameSirFastBattery.PublishedPercent((byte)0));
    }
    [Theory]
    [InlineData(0x3537,0x10A9,false)]
    [InlineData(0x3537,0x10A8,true)]
    [InlineData(0x3537,0x100B,false)]
    [InlineData(0x045E,0x10A9,false)]
    public void NativeGipSubscriptionIsLimitedToG7GipIdentities(int vid,int pid,bool expected)
        =>Assert.Equal(expected,ControllerIdentity.CanListenForGip((ushort)vid,(ushort)pid));
}
