using System;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;

namespace LGSTrayUI.Tests;
public class LogitechModelMergeTests
{
    private static TaskbarBattery Row(DeviceType type,string name,string provider,string id,int? level=92,string? physical=null,bool offline=false)
        =>new(type,name,offline?null:level,offline?"离线":"已连接",false,DateTimeOffset.UtcNow,
            $"logi:{type}:{name.Replace(" ","")}:{provider}:{id}",PhysicalId:physical);

    private static TaskbarBattery Mouse(string provider,string id="a",string? physical=null)
        =>Row(DeviceType.Mouse,provider=="Native"?"PRO X 2":"PRO X SUPERLIGHT 2 Wireless Mouse",provider,id,physical:physical);

    [Fact] public void ScreenshotPairsCollapseToOneMouseAndOneHeadset()
    {
        var c=new RingPreferences();
        var rows=new[]{Mouse("Native"),Mouse("GHub"),Row(DeviceType.Headset,"PRO X 2 LIGHTSPEED","Native","a",100),Row(DeviceType.Headset,"PRO X 2 Lightspeed Gaming Headset","GHub","b",99)};
        var groups=DeviceIdentityManager.Group(rows,c);
        Assert.Equal(2,groups.Length);
        Assert.Equal(new[]{DeviceType.Mouse,DeviceType.Headset},groups.Select(g=>g.Reading.Type));
        Assert.All(groups,g=>{Assert.Equal(2,g.Sources.Length);Assert.True(g.Inferred);});
        Assert.Empty(c.AutomaticAliases);
    }
    [Fact] public void RefreshAndRestartKeepOneStableRing()
    {
        var c=new RingPreferences();var a=Mouse("Native");var b=Mouse("GHub");
        var root=Assert.Single(DeviceIdentityManager.Group([a,b],c)).Reading.Key;
        var offline=b with{Status="离线",Level=null};
        var g=Assert.Single(DeviceIdentityManager.Group([offline,a],c));
        Assert.Equal(root,g.Reading.Key);Assert.Equal(92,g.Reading.Level);
        var restored=System.Text.Json.JsonSerializer.Deserialize<RingPreferences>(System.Text.Json.JsonSerializer.Serialize(c))!;
        Assert.Equal(root,Assert.Single(DeviceIdentityManager.Group([a with{Status="离线",Level=null},b],restored)).Reading.Key);
    }
    [Fact] public void AmbiguousProviderOrConflictingIdsNeverInferIdentity()
    {
        var a=Mouse("Native");var b=Mouse("GHub");
        Assert.Equal(3,DeviceIdentityManager.Group([a,b,Mouse("Native","other")],new()).Length);
        Assert.Equal(2,DeviceIdentityManager.Group([a with{PhysicalId="serial:a"},b with{PhysicalId="serial:b"}],new()).Length);
        Assert.Equal(2,DeviceIdentityManager.Group([a,b with{Name="PRO X SUPERLIGHT 2 DEX"}],new()).Length);
    }
    [Fact] public void ThreeSourcesFromOneMouseMergeWithoutDiscardingSource()
    {
        var c=new RingPreferences();
        var native=Mouse("Native","serial-one");
        var ghub=Mouse("GHub","GHUB.pro_x_2_wireless_mouse_ext1");
        var phantom=Mouse("GHub","GHUB.pro_x_2_wireless_mouse") with{Level=null,Status="离线",Updated=DateTimeOffset.MinValue};
        var group=Assert.Single(DeviceIdentityManager.Group([native,ghub,phantom],c));
        Assert.Equal(3,group.Sources.Length);
        Assert.Equal(92,group.Reading.Level);
        Assert.Single(DeviceIdentityManager.Group([native with{Level=null,Status="离线"},ghub with{Level=null,Status="离线"},phantom],c));
        Assert.Equal(3,DeviceIdentityManager.Group([native,ghub,phantom with{Level=65,Status="已连接",Updated=DateTimeOffset.UtcNow}],new()).Length);
        Assert.Equal(4,DeviceIdentityManager.Group([native,Mouse("Native","serial-two"),ghub,phantom],new()).Length);
    }
    [Fact] public void ManualPairCanAbsorbAnUnreportedModeAlias()
    {
        var c=new RingPreferences{AutoDeduplicate=false};
        var a=Mouse("Native");var b=Mouse("GHub","GHUB.pro_x_2_wireless_mouse_ext1");
        var alias=Mouse("GHub","GHUB.pro_x_2_wireless_mouse") with{Level=null,Status="离线",Updated=DateTimeOffset.MinValue};
        var pair=DeviceIdentityManager.Group([a,b],c);DeviceIdentityManager.Merge(c,pair[0],pair[1]);c.AutoDeduplicate=true;
        Assert.Equal(3,Assert.Single(DeviceIdentityManager.Group([a,b,alias],c)).Sources.Length);
    }
    [Fact] public void ExplicitSplitAndManualLinkAreRespectedOnRefresh()
    {
        var c=new RingPreferences();var a=Mouse("Native");var b=Mouse("GHub");
        var g=Assert.Single(DeviceIdentityManager.Group([a,b],c));
        DeviceIdentityManager.Split(c,g);
        var split=DeviceIdentityManager.Group([a,b],c);
        Assert.Equal(2,split.Length);
        DeviceIdentityManager.Merge(c,split[0],split[1]);
        var merged=Assert.Single(DeviceIdentityManager.Group([a,b with{Status="离线",Level=null}],c));
        Assert.False(merged.Inferred);
        Assert.Single(DeviceIdentityManager.Group([b],c));
    }
}
