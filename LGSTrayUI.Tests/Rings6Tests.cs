using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings6Tests
{
    private static TaskbarBattery Mouse(string key,int? level=65,string? physical=null,bool offline=false)=>new(DeviceType.Mouse,"Same model",level,offline?"离线":"已连接",false,DateTimeOffset.MinValue,key,PhysicalId:physical);
    [Fact] public void SameModelIsNotIdentity()
    {
        var c=new RingPreferences();
        Assert.Equal(2,DeviceIdentityManager.Group([Mouse("logi:a"),Mouse("ble:b")],c).Length);
    }
    [Fact] public void ConfirmedIdentityCanBeSplitAndRestored()
    {
        var c=new RingPreferences();var readings=new[]{Mouse("ble:a",null,"physical:a",true),Mouse("ble:b",73,"physical:a")};
        var group=Assert.Single(DeviceIdentityManager.Group(readings,c));Assert.Equal(73,group.Reading.Level);
        DeviceIdentityManager.Split(c,group);Assert.Equal(2,DeviceIdentityManager.Group(readings,c).Length);
        c.SeparateDeviceSources.Clear();Assert.Single(DeviceIdentityManager.Group(readings,c));
    }
    [Fact] public void ManualMergePreservesPrimaryAndFallsBackToLiveBattery()
    {
        var c=new RingPreferences();var a=Mouse("logi:a",null,null,true);var b=Mouse("ble:b",82);
        c.Names[a.Key]="My mouse";c.DeviceColors[b.Key]="#123456";c.Order.Add(a.Key);
        var groups=DeviceIdentityManager.Group([a,b],c);
        DeviceIdentityManager.Merge(c,groups.Single(g=>g.Reading.Key==a.Key),groups.Single(g=>g.Reading.Key==b.Key));
        c.PreferredSources[a.Key]=a.Key;
        var merged=Assert.Single(DeviceIdentityManager.Group([a,b],c));Assert.Equal(a.Key,merged.Reading.Key);Assert.Equal(82,merged.Reading.Level);
        Assert.Equal("My mouse",c.Names[a.Key]);Assert.Equal("#123456",c.DeviceColors[a.Key]);
        DeviceIdentityManager.Split(c,merged);Assert.Equal(2,DeviceIdentityManager.Group([a,b],c).Length);
    }
    [Fact] public void RightGapRespectsBothMargins()
    {
        (int left,int right)[] apps=[(370,510)];
        Assert.True(TaskbarPlacement.TryGap(48,784,164,16,16,0,apps,out int right));Assert.Equal(604,right);
        Assert.False(TaskbarPlacement.TryGap(48,784,740,16,16,0,apps,out _));
    }
}
