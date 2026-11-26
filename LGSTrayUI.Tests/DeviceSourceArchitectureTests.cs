using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;

public class DeviceSourceArchitectureTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.UnixEpoch.AddDays(1);
    private static TaskbarBattery Native(string id="one",string? physical=null)=>new(DeviceType.Mouse,"PRO X 2",97,"已连接",false,Now,"logi:Mouse:PROX2:Native:"+id,PhysicalId:physical);
    private static TaskbarBattery Hub(int mode,bool reports=false,string? physical=null)=>new(DeviceType.Mouse,"PRO X SUPERLIGHT 2 Wireless Mouse",reports?97:null,reports?"已连接":"离线",false,reports?Now:DateTimeOffset.MinValue,"logi:Mouse:PROXSUPERLIGHT2:GHub:GHUB.pro_x_2_wireless_mouse"+(mode==0?"":"_ext"+mode),PhysicalId:physical);
    [Fact]
    public void BandOnlySelectionKeepsSourceOrderAndAnExplicitBandCanOverrideAnExactReading()
    {
        var slot = new TaskbarBattery(DeviceType.Gamepad, "Pad", null, "已连接", false, Now, "xinput:0", "4/4") { Estimated = true };
        var native = slot with { Key = "gip:pad", ShortLabel = "2/4", Estimated = false, Updated = Now.AddSeconds(-1) };
        var root = native with { Key = "device:pad" };
        var preferences = new RingPreferences();
        var selector = new BatterySourceSelector();
        DeviceGroup Group(TaskbarBattery report) => new(root, [slot, report], false);

        Assert.Equal("2/4", Assert.Single(selector.Select([Group(native)], preferences, Now)).Reading.ShortLabel);
        preferences.PreferredSources[root.Key] = slot.Key;
        var exact = native with { Level = 70, ShortLabel = null };
        Assert.Equal("4/4", Assert.Single(selector.Select([Group(exact)], preferences, Now)).Reading.ShortLabel);
        preferences.PreferredSources.Clear();
        Assert.Equal(70, Assert.Single(selector.Select([Group(exact)], preferences, Now)).Reading.Level);
    }
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(50)]
    public void AnyNumberOfUnreportedModeAliasesCanJoinOneMouse(int total)
    {
        var rows=new[]{Native()}.Concat(Enumerable.Range(0,total-1).Select(i=>Hub(i,i==1))).ToArray();
        var c=new RingPreferences();
        var group=Assert.Single(DeviceIdentityManager.Group(rows,c));
        Assert.Equal(total,group.Sources.Length);
        Assert.Equal(group.Reading.Key,Assert.Single(DeviceIdentityManager.Group(rows.Reverse(),c)).Reading.Key);
    }
    [Fact] public void MultipleReportingUnitsRemainDistinctWithoutEvidence()
    {
        Assert.Equal(4,DeviceIdentityManager.Group([Native(),Hub(0,true),Hub(1,true),Hub(2)],new()).Length);
        Assert.Equal(4,DeviceIdentityManager.Group([Native(),Native("two"),Hub(0),Hub(1,true)],new()).Length);
        Assert.Equal(3,DeviceIdentityManager.Group([Native(physical:"a"),Hub(0,true,"b"),Hub(1)],new()).Length);
    }
    [Fact] public void ExactIdentityHasNoProviderOrCountLimit()
    {
        var rows=Enumerable.Range(0,12).Select(i=>Native("id"+i,"serial:a") with{Key="provider"+i}).ToArray();
        Assert.Equal(12,Assert.Single(DeviceIdentityManager.Group(rows,new())).Sources.Length);
    }
    [Fact] public void AbsentAliasRootCannotHideConflictingSerials()
    {
        var a=Native("a","serial:a");var b=Native("b","serial:b");
        var c=new RingPreferences();
        c.AutomaticAliases[a.Key]="absent";c.AutomaticAliases[b.Key]="absent";
        Assert.Equal(2,DeviceIdentityManager.Group([a,b],c).Length);
    }
    [Fact] public void SourceSelectionIsStableAndFailsOverWhenUnavailable()
    {
        var a=Native();var b=Hub(1,true);
        var c=new RingPreferences();
        var selector=new BatterySourceSelector();
        DeviceGroup[] Group(TaskbarBattery x,TaskbarBattery y)=>DeviceIdentityManager.Group([x,y],c);
        var initial=Assert.Single(selector.Select(Group(a,b with{Updated=Now.AddSeconds(-1),Level=88}),c,Now));
        Assert.Equal(97,initial.Reading.Level);
        var next=Assert.Single(selector.Select(Group(a,b with{Updated=Now.AddSeconds(1),Level=88}),c,Now.AddSeconds(1)));
        Assert.Equal(97,next.Reading.Level);
        var failover=Assert.Single(selector.Select(Group(a with{Status="离线",Level=null},b with{Level=88}),c,Now.AddSeconds(2)));
        Assert.Equal(88,failover.Reading.Level);
        Assert.Equal(initial.Reading.Key,failover.Reading.Key);
    }
    [Fact] public void ExplicitPreferenceAndFreshnessAreRespected()
    {
        var a=Native();var b=Hub(1,true) with{Level=80,Updated=Now.AddMinutes(-25)};
        var c=new RingPreferences();
        var g=Assert.Single(DeviceIdentityManager.Group([a,b],c));
        c.PreferredSources[g.Reading.Key]=b.Key;
        var selector=new BatterySourceSelector();
        Assert.Equal(97,Assert.Single(selector.Select([g],c,Now)).Reading.Level);
        g=Assert.Single(DeviceIdentityManager.Group([a,b with{Updated=Now}],c));
        Assert.Equal(80,Assert.Single(selector.Select([g],c,Now)).Reading.Level);
    }
}
