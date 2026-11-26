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
    [Fact]
    public void WindowsIdFailureStillProducesAConnectedControllerKey()
    {
        string key = GipBatteryReader.ControllerKey(() => throw new ArgumentException(), () => "pad1", out var problem);
        Assert.Equal("gip:wgi-session:pad1", key);
        Assert.Contains("80070057", problem);
        string second = GipBatteryReader.ControllerKey(() => "\0", () => "pad2", out _);
        Assert.NotEqual(key, second);
        string saved = GipBatteryReader.ControllerKey(() => "saved\0", () => throw new InvalidOperationException(), out var ok);
        Assert.Null(ok);
        Assert.Equal("gip:saved", saved);
    }
    [Fact]
    public void ConfirmedContainerStabilizesFallbackAcrossConnectionsAndPreservesExistingKeys()
    {
        var first = new TaskbarBattery(DeviceType.Gamepad, "GameSir G7 Pro", null, "已连接", false, DateTimeOffset.MinValue, "gip:wgi-session:a");
        var reconnected = first with { Key = "gip:wgi-session:b" };
        var container = Guid.NewGuid();
        var result = GameSirFastBattery.WithContainer(first, container);
        Assert.Equal(result.Key, GameSirFastBattery.WithContainer(reconnected, container).Key);
        Assert.NotEqual(result.Key, GameSirFastBattery.WithContainer(first, Guid.NewGuid()).Key);
        Assert.Equal("ON", result.RingLabel);
        Assert.Equal(first, GameSirFastBattery.WithContainer(first, null));
        var legacy = first with { Key = "gip:saved" };
        Assert.Equal(legacy, GameSirFastBattery.WithContainer(legacy, container));
    }
    [Theory]
    [InlineData(0x3537,0x10A9,false)]
    [InlineData(0x3537,0x10A8,true)]
    [InlineData(0x3537,0x100B,false)]
    [InlineData(0x045E,0x10A9,false)]
    public void NativeGipSubscriptionIsLimitedToG7GipIdentities(int vid,int pid,bool expected)
        =>Assert.Equal(expected,ControllerIdentity.CanListenForGip((ushort)vid,(ushort)pid));
    [Fact]
    public void AQuietLiveCycloneCollectionAgesWithoutFakingAnUnplug()
    {
        var now=DateTimeOffset.UnixEpoch;
        var row=new TaskbarBattery(DeviceType.Gamepad,"Cyclone 2",55,"充电中",true,now,"controller-hid:3537:100B:test");
        Assert.Equal(row,GameSirPassiveInput.WhilePresent(row,now.AddSeconds(35)));
        var aged=GameSirPassiveInput.WhilePresent(row,now.AddSeconds(36));
        Assert.True(aged.IsConnected);
        Assert.True(aged.Stale);
        Assert.False(aged.IsActivelyCharging);
        Assert.Null(aged.Level);
        Assert.Equal(55,aged.LastKnownLevel);
    }
}
