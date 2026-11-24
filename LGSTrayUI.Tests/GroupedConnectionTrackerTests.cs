using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class GroupedConnectionTrackerTests
{
    private static readonly DateTimeOffset Start=DateTimeOffset.UnixEpoch;
    private static TaskbarBattery Row(string key,bool online)=>new(DeviceType.Mouse,"Mouse",online?96:null,online?"已连接":"离线",false,Start,key);
    private static DeviceGroup Group(string key,bool first,bool second)=>new(Row(key,first||second),[Row(key,first),Row(key+":second",second)],false);
    [Fact] public void StartupAndSingleProviderChangesAreSilent()
    {
        var tracker=new GroupedConnectionTracker(Start);
        Assert.Empty(tracker.Observe([Group("mouse",true,true)],Start));
        Assert.Empty(tracker.Observe([Group("mouse",true,false)],Start.AddSeconds(11)));
        Assert.Empty(tracker.Observe([Group("mouse",false,true)],Start.AddSeconds(14)));
        Assert.Empty(tracker.Observe([Group("mouse",true,true)],Start.AddSeconds(17)));
    }
    [Fact] public void OnlyChangedPhysicalDeviceNotifiesOnceAfterDebounce()
    {
        var tracker=new GroupedConnectionTracker(Start);
        tracker.Observe([Group("mouse",true,true),Group("other",true,true)],Start);
        Assert.Empty(tracker.Observe([Group("mouse",false,false),Group("other",true,true)],Start.AddSeconds(11)));
        var change=Assert.Single(tracker.Observe([Group("mouse",false,false),Group("other",true,true)],Start.AddSeconds(14)));
        Assert.False(change.Online);Assert.Equal("mouse",change.Device.Key);
        Assert.Empty(tracker.Observe([Group("mouse",false,false),Group("other",true,true)],Start.AddSeconds(15)));
        Assert.Empty(tracker.Observe([Group("mouse",true,false),Group("other",true,true)],Start.AddSeconds(16)));
        Assert.True(Assert.Single(tracker.Observe([Group("mouse",true,true),Group("other",true,true)],Start.AddSeconds(19))).Online);
    }
    [Fact] public void RescanAndWakeRebaselineSilently()
    {
        var tracker=new GroupedConnectionTracker(Start);
        tracker.Observe([Group("mouse",true,true)],Start);
        tracker.Suppress(Start.AddSeconds(11),TimeSpan.FromSeconds(10));
        Assert.Empty(tracker.Observe([Group("mouse",false,false)],Start.AddSeconds(12)));
        Assert.Empty(tracker.Observe([Group("mouse",true,true)],Start.AddSeconds(20)));
        Assert.Empty(tracker.Observe([Group("mouse",true,true)],Start.AddSeconds(22)));
        Assert.Empty(tracker.Observe([Group("mouse",false,false)],Start.AddSeconds(50)));
        Assert.Empty(tracker.Observe([Group("mouse",true,true)],Start.AddSeconds(54)));
    }
    [Fact] public void NewDeviceAndRootChangeAreHandled()
    {
        var tracker=new GroupedConnectionTracker(Start);
        tracker.Observe([],Start);
        Assert.Empty(tracker.Observe([Group("mouse",true,false)],Start.AddSeconds(11)));
        Assert.Single(tracker.Observe([Group("mouse",true,true)],Start.AddSeconds(14)));
        var renamed=new DeviceGroup(Row("mouse:second",true),[Row("mouse",true),Row("mouse:second",true)],false);
        Assert.Empty(tracker.Observe([renamed],Start.AddSeconds(17)));
    }
}
