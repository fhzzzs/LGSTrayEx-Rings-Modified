using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Performance20Tests{
    [Fact]public void HistoryIsBoundedAndTimestampOnlyUpdatesAreIgnored(){
        var history=new BatteryHistory(256);var t=DateTimeOffset.UtcNow;
        var row=new TaskbarBattery(DeviceType.Mouse,"Mouse",50,"已连接",false,t,"a");
        history.Observe([new DeviceGroup(row,[row],false)],t);
        for(int i=1;i<10000;i++){
            var next=row with{Updated=t.AddSeconds(i)};
            history.Observe([new DeviceGroup(next,[next],false)],next.Updated);
        }
        Assert.Single(history.Snapshot());
        for(int i=0;i<10000;i++){
            var next=row with{Level=i%101,Updated=t.AddSeconds(i)};
            history.Observe([new DeviceGroup(next,[next],false)],next.Updated);
        }
        Assert.Equal(256,history.Snapshot().Length);
    }
    [Theory][InlineData("asus",10,3,20)][InlineData("ble",15,2,15)][InlineData("ble",100,3,120)][InlineData("gip",2,100,2)]
    public void StableIntervalIsBoundedAndKeepsGamepadResponsive(string id,int period,int stable,int expected)
        =>Assert.Equal(expected,ProviderScheduler.AdaptiveInterval(id,TimeSpan.FromSeconds(period),stable).TotalSeconds);
}
