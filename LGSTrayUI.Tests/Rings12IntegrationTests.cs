using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings12IntegrationTests
{
    private static readonly DateTimeOffset T=DateTimeOffset.UnixEpoch.AddDays(1);
    private static TaskbarBattery Row(string key,DateTimeOffset now,bool online=true)=>new(DeviceType.Mouse,"Stress mouse",online?72:null,online?"已连接":"离线",false,now,key,PhysicalId:"stress:one");
    [Fact]public void FiftySourcesSurviveRepeatedRefreshRescanAndConfigRoundTrip(){
        var center=new PhysicalDeviceCenter(T);var c=new RingPreferences();
        TaskbarBattery[] Rows(DateTimeOffset t)=>Enumerable.Range(0,50).Select(i=>Row("provider:"+i,t)).ToArray();
        center.RefreshInventory(Rows(T),c);var first=Assert.Single(center.Advance(c,T).Rows);c.Names[first.Key]="My mouse";
        for(int i=1;i<=200;i++){
            var now=T.AddSeconds(i);
            center.RefreshInventory(Rows(now).Where((r,n)=>n%5==i%5),c);
            var state=center.Advance(c,now);
            Assert.Equal(first.Key,Assert.Single(state.Rows).Key);
            Assert.Equal(50,Assert.Single(state.Groups).Sources.Length);
            Assert.Empty(state.Changes);
        }
        var copy=RingSettingsStore.Parse(System.Text.Json.JsonSerializer.Serialize(c));
        var after=new PhysicalDeviceCenter(T.AddMinutes(10));after.RefreshInventory(Rows(T.AddMinutes(10)),copy);
        var row=Assert.Single(after.Advance(copy,T.AddMinutes(10)).Rows);
        Assert.Equal(first.Key,row.Key);Assert.Equal("My mouse",row.Name);
    }
    [Fact]public void BriefProviderLossDoesNotGenerateDisconnectNotification(){
        var center=new PhysicalDeviceCenter(T);var c=new RingPreferences();
        center.RefreshInventory([Row("provider:0",T)],c);center.Advance(c,T);
        var now=T.AddSeconds(11);
        for(int i=0;i<100;i++){
            var time=now.AddSeconds(i*2);
            center.RefreshInventory([],c);Assert.Empty(center.Advance(c,time).Changes);
            center.RefreshInventory([Row("provider:0",time.AddSeconds(1))],c);
            Assert.Empty(center.Advance(c,time.AddSeconds(1)).Changes);
        }
    }
    [Fact]public void SimulatedWakeAfterLongIdleDoesNotEmitFalseConnectionEvent(){
        var center=new PhysicalDeviceCenter(T);var c=new RingPreferences();
        center.RefreshInventory([Row("provider:0",T)],c);center.Advance(c,T);
        var now=T.AddHours(24);center.RefreshInventory([Row("provider:0",now)],c);
        var state=center.Advance(c,now);Assert.True(state.Suppressed);Assert.Empty(state.Changes);
        Assert.Equal(72,Assert.Single(state.Rows).Level);
    }
}
