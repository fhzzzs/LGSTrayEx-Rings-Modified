using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class RuntimeFeedbackTests{
    static readonly DateTimeOffset T=DateTimeOffset.UnixEpoch.AddDays(10);
    static TaskbarBattery Row(DeviceType type,string key,int? level=80,string status="已连接",bool charging=false)=>new(type,key,level,status,charging,T,key);
    [Fact]public void CustomOrderOverridesCategoryAndRoundTrips(){
        var mouse=Row(DeviceType.Mouse,"mouse");var headset=Row(DeviceType.Headset,"headset");var pad=Row(DeviceType.Gamepad,"pad");
        var c=new RingPreferences();Assert.Equal(new[]{"mouse","headset","pad"},DeviceOrder.Sort([pad,headset,mouse],c.Order).Select(d=>d.Key));
        c.Order=["pad","mouse","headset"];
        var restored=RingSettingsStore.Parse(RingSettingsStore.Snapshot(c));
        Assert.Equal(new[]{"pad","mouse","headset"},DeviceOrder.Sort([mouse,headset,pad],restored.Order).Select(d=>d.Key));
        restored.Order.Clear();Assert.Equal(new[]{"mouse","headset","pad"},DeviceOrder.Sort([pad,headset,mouse],restored.Order).Select(d=>d.Key));
    }
    [Fact]public void ConfirmedFullStopsShineEvenWhenRawLevelIs99(){
        var row=Row(DeviceType.Headset,"logi:Headset:PROX2:Native:a",99,"已充满",true);
        Assert.True(row.IsFull);Assert.False(row.IsActivelyCharging);Assert.Equal(100,row.BarValue);Assert.Equal("Full",row.RingLabel);
        Assert.Equal(new RingPreferences().Accent(row with{Status="已连接",Level=100,Charging=false}),new RingPreferences().Accent(row));
        Assert.False((row with{Stale=true}).IsFull);
        Assert.False((row with{Status="充电中"}).IsFull);
    }
    [Fact]public void NativeFullWinsOverOlderNearFullHubButNotManualPreference(){
        var n=Row(DeviceType.Headset,"logi:Headset:PROX2:Native:a",100,"已充满");
        var h=Row(DeviceType.Headset,"logi:Headset:PROX2:GHub:b",99,"充电中",true) with{Updated=T.AddMinutes(-5)};
        var c=new RingPreferences();var selector=new BatterySourceSelector();
        var root=n with{Key="device:test"};
        DeviceGroup G(params TaskbarBattery[] rows)=>new(root,rows,false,true);
        var initial=Assert.Single(selector.Select([G(n with{Level=98,Status="充电中",Charging=true},h)],c,T));
        Assert.False(initial.Reading.IsFull);
        var selected=Assert.Single(selector.Select([G(n,h)],c,T));Assert.True(selected.Reading.IsFull);
        c.PreferredSources[root.Key]=h.Key;
        Assert.False(Assert.Single(selector.Select([G(n,h)],c,T)).Reading.IsFull);
        c.PreferredSources.Clear();
        Assert.Null(BatterySourceSelector.ConfirmedFull([SourceReading.Normalize(n,T,c),SourceReading.Normalize(h with{Level=70},T,c)]));
        Assert.Null(BatterySourceSelector.ConfirmedFull(new[]{SourceReading.Normalize(n,T.AddMinutes(21),c)}.Where(x=>x.Quality==ReadingQuality.Exact).ToArray()));
    }
    [Fact]public void G7TelemetryRequiresSpecificHeaderAndValidFields(){
        byte[] report=new byte[64];report[0]=0x10;report[3]=0x3C;report[4]=0xE0;report[32]=1;report[33]=73;
        Assert.Equal((73,true,false),ControllerInputBattery.Decode(0x3537,0x10A9,report,64)!.Value);
        Assert.Null(ControllerInputBattery.Decode(0x3537,0x10A9,report,20));
        Assert.Null(ControllerInputBattery.Decode(0x3537,0x100B,report,64));
        report[33]=255;Assert.Null(ControllerInputBattery.Decode(0x3537,0x10A9,report,64));
        report[33]=100;report[32]=0;Assert.Equal((100,false,true),ControllerInputBattery.Decode(0x3537,0x10A9,report,64)!.Value);
        report[4]=0;Assert.Null(ControllerInputBattery.Decode(0x3537,0x10A9,report,64));
    }
    [Fact]public void UiaFreshnessStillFailsClosed(){
        Assert.True(TaskbarAutomationProbe.Fresh(1499,0,50));
        Assert.False(TaskbarAutomationProbe.Fresh(1501,0,50));
        Assert.InRange(TaskbarAutomationProbe.CaptureIntervalMs,900,1200);
    }
    [Fact]
    public void ReorderingVisibleDevicesPreservesSlotsForAbsentDevicesAcrossRestart()
    {
        var c=new RingPreferences{Order=["mouse","away","pad"]};
        c.Order=DeviceOrder.MergeDrag(c.Order,["pad","mouse","new"]);
        Assert.Equal(new[]{"pad","away","mouse","new"},RingSettingsStore.Parse(RingSettingsStore.Snapshot(c)).Order);
    }
}
