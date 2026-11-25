using System;
using System.IO;
using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings15IntegrationTests
{
    [Theory]
    [InlineData(100,0,0)]
    [InlineData(100,100,100)]
    [InlineData(100,72,72)]
    [InlineData(0,0,null)]
    [InlineData(100,101,null)]
    [InlineData(100,-1,null)]
    public void GipDoesNotConvertInvalidCapacityToFull(int full,int remaining,int? expected)
        =>Assert.Equal(expected,GipBatteryReader.CapacityPercent(full,remaining));

    [Theory]
    [InlineData(2,0,true,2)]
    [InlineData(2,0,false,4)]
    [InlineData(2,2,true,8)]
    [InlineData(60,4,false,120)]
    [InlineData(120,0,false,120)]
    public void ProviderDelayIsBoundedAndAdaptive(double seconds,int failures,bool online,double expected)
        =>Assert.Equal(expected,ProviderScheduler.NextDelay(TimeSpan.FromSeconds(seconds),failures,online).TotalSeconds);

    [Fact]public void MalformedAliasAndInvalidSettingsAreSanitized(){
        var c=RingSettingsStore.Parse("{\"DeviceAliases\":{\"gip:a\":null},\"Order\":[\"a\",null,\"a\"],\"PollSeconds\":-1,\"BleSeconds\":9999,\"Font\":null}");
        Assert.Empty(c.DeviceAliases);Assert.Equal(new[]{"a"},c.Order);
        Assert.Equal(.5,c.PollSeconds);Assert.Equal(120d,c.BleSeconds);Assert.Equal("Segoe UI",c.Font);
    }

    [Fact]public void ValidBackupSurvivesUnsupportedPrimarySchema(){
        var folder=Path.Combine(Path.GetTempPath(),"rings15-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);var path=Path.Combine(folder,"rings-settings.json");
        try{
            File.WriteAllText(path,"{\"SchemaVersion\":999}");
            File.WriteAllText(path+".bak","{\"SchemaVersion\":3,\"Gap\":9}");
            Assert.Equal(9d,RingSettingsStore.Load(path).Gap);
            RingSettingsStore.Save(path,new RingPreferences{Gap=7});
            Assert.Equal(9d,RingSettingsStore.Parse(File.ReadAllText(path+".bak")).Gap);
            Assert.Equal(7d,RingSettingsStore.Load(path).Gap);
        }finally{Directory.Delete(folder,true);}
    }

    [Fact]public void InvalidInventoryFallsBackToBackup(){
        var folder=Path.Combine(Path.GetTempPath(),"rings15-inventory-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);var path=Path.Combine(folder,"inventory.json");
        try{
            var row=new TaskbarBattery(DeviceType.Mouse,"Mouse",null,"离线",false,DateTimeOffset.UnixEpoch,"known:mouse");
            File.WriteAllText(path+".bak",DeviceInventoryStore.Snapshot([row]));
            File.WriteAllText(path,"null");
            Assert.Equal(row.Key,Assert.Single(DeviceInventoryStore.Load(path)).Key);
        }finally{Directory.Delete(folder,true);}
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TaskbarFitRespectsNegativeMonitorOrigin(double scale){
        var screen=new TaskbarPlacement.PixelRect{Left=-2560,Top=-200,Right=0,Bottom=1240};
        var bar=new TaskbarPlacement.PixelRect{Left=-2560,Top=1240-(int)Math.Round(48*scale),Right=0,Bottom=1240};
        var tray=new TaskbarPlacement.PixelRect{Left=-(int)Math.Round(270*scale),Top=bar.Top,Right=0,Bottom=1240};
        Assert.True(TaskbarPlacement.TryFit(bar,tray,screen,scale,out var p,4));
        Assert.True(p.X>=screen.Left&&p.X+p.Width<=tray.Left);
    }

    [Fact]public void ExactIdentityModeChangesKeepLogicalIdButDifferentUnitsRemainSeparate(){
        var t=DateTimeOffset.UnixEpoch.AddDays(1);
        TaskbarBattery Row(string key,string physical)=>new(DeviceType.Gamepad,"Controller",70,"已连接",false,t,key,PhysicalId:physical);
        var c=new RingPreferences();var center=new PhysicalDeviceCenter(t);
        center.RefreshInventory([Row("gip:a","serial:a")],c);
        var id=Assert.Single(center.Advance(c,t).Rows).Key;
        center.RefreshInventory([Row("ble:a","serial:a"),Row("gip:b","serial:b")],c);
        var rows=center.Advance(c,t).Rows;
        Assert.Equal(2,rows.Length);Assert.Contains(rows,r=>r.Key==id);Assert.Equal(2,rows.Select(r=>r.Key).Distinct().Count());
    }
}
