using System.Linq;
using Xunit;
namespace LGSTrayUI.Tests;
public class RingPreferencesTests
{
    [Fact]
    public void NotificationRulesSupportPerThresholdIntervalsAndRejectInvalidValues()
    {
        var rules=LGSTrayUI.RingAlerts.Parse("20:30,10:15,5:0,101:1,2:-1,abc").ToArray();
        Assert.Equal(new[]{(20,30),(10,15),(5,0)},rules);
    }
    [Fact]
    public void DefaultAnimationTimingsMatchDesign()
    {
        var p=new LGSTrayUI.RingPreferences();
        Assert.Equal(300d,p.GrayMs);Assert.Equal(3000d,p.WaitMs);Assert.Equal(700d,p.OutMs);Assert.Equal(500d,p.InMs);Assert.Equal(75d,p.StaggerMs);Assert.Equal(6d,p.Bounce);
        p.Speed=2;Assert.Equal(250d,p.Ms(500));p.Animate=false;Assert.Equal(0d,p.Ms(500));
    }
    [Fact]
    public void ThemeSnapshotDoesNotAliasDeviceColorsOrChangeInventory()
    {
        var source=new LGSTrayUI.RingPreferences{Normal="#123456",Gap=9,Speed=1.4,Rules="3:5"};source.DeviceColors["a"]="#ABCDEF";source.Order.Add("a");
        var saved=LGSTrayUI.RingThemes.Snapshot(source);source.DeviceColors["a"]="#111111";
        var target=new LGSTrayUI.RingPreferences{Rules="10:15"};target.Order.Add("b");target.Hidden.Add("c");
        LGSTrayUI.RingThemes.Copy(saved,target);
        Assert.Equal("#123456",target.Normal);Assert.Equal(9d,target.Gap);Assert.Equal(1.4,target.Speed);Assert.Equal("#ABCDEF",target.DeviceColors["a"]);Assert.Equal("10:15",target.Rules);Assert.Equal(new[]{"b"},target.Order);Assert.Equal(new[]{"c"},target.Hidden);
    }
    [Fact]
    public void LeftPlacementFindsFirstAvailableGapRatherThanHidingOnCollision()
    {
        var occupied=new[]{(48,200),(400,500)};
        Assert.True(LGSTrayUI.TaskbarPlacement.TryGap(48,900,100,16,16,0,true,occupied,out var left));
        Assert.Equal(216,left);
        Assert.True(LGSTrayUI.TaskbarPlacement.TryGap(48,900,100,16,16,0,false,occupied,out var right));
        Assert.Equal(784,right);
        Assert.False(LGSTrayUI.TaskbarPlacement.TryGap(48,200,100,16,16,0,true,occupied,out _));
    }
    [Fact]
    public void PaletteOverridesAndLightContrastAreIndependentAndThemeCopiesDeeply()
    {
        var p=new LGSTrayUI.RingPreferences();
        Assert.Equal("#20242C",p.Foreground("mouse","Icon",true));
        Assert.Equal("#F5F5F5",p.Foreground("mouse","Icon",false));
        p.DevicePalette["mouse"]=new(){["Icon"]="#123456",["Normal"]="#ABCDEF"};
        Assert.Equal("#123456",p.Foreground("mouse","Icon",true));
        Assert.Equal("#20242C",p.Foreground("mouse","Text",true));
        var saved=LGSTrayUI.RingThemes.Snapshot(p);p.DevicePalette["mouse"]["Icon"]="#111111";
        Assert.Equal("#123456",saved.DevicePalette["mouse"]["Icon"]);
    }
    [Fact]
    public void AudioIsPerDeviceAndThresholdWithGlobalFallback()
    {
        var p=new LGSTrayUI.RingPreferences();
        p.AlertSounds[""]=new(){[20]=new(){Sound="Glass",Volume=45}};
        p.AlertSounds["mouse"]=new(){[5]=new(){Sound="Pop",Volume=20}};
        Assert.Equal("Glass",p.Audio("mouse",20).Sound);
        Assert.Equal(20d,p.Audio("mouse",5).Volume);
        Assert.Equal("Alert",p.Audio("keyboard",5).Sound);
        Assert.Empty(LGSTrayUI.RingThemes.Snapshot(p).AlertSounds);
    }
}
