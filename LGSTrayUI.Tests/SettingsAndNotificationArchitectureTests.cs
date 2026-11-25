using System;
using System.IO;
using System.Windows;
using Xunit;
namespace LGSTrayUI.Tests;
public class SettingsAndNotificationArchitectureTests
{
    [Fact]public void LegacySettingsMigrateWithoutDiscardingAliases(){
        var c=RingSettingsStore.Parse("{\"DeviceAliases\":{\"a\":\"b\"},\"Gap\":7,\"Names\":null}");
        Assert.Equal(3,c.SchemaVersion);Assert.Equal("b",c.DeviceAliases["a"]);Assert.Equal(7d,c.Gap);Assert.Empty(c.Names);
    }
    [Fact]public void NewerUnknownSchemaIsRejected()=>Assert.Throws<FormatException>(()=>RingSettingsStore.Parse("{\"SchemaVersion\":999}"));
    [Fact]public void AtomicSettingsSaveRetainsPreviousValidVersion(){
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));string path=Path.Combine(dir,"test.json");
        try{RingSettingsStore.Save(path,new RingPreferences{Gap=5});RingSettingsStore.Save(path,new RingPreferences{Gap=9});
            File.WriteAllText(path,"invalid");Assert.Equal(5d,RingSettingsStore.Load(path).Gap);
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [Fact]public void ThemesDoNotChangeDeviceIdentityOrNotificationPreferences(){
        var theme=new RingPreferences{QuietNotifications=true,NotificationWidth=400};theme.DeviceAliases["a"]="b";
        var target=new RingPreferences{NotificationWidth=300};target.DeviceAliases["c"]="d";
        RingThemes.Copy(theme,target);Assert.False(target.QuietNotifications);Assert.Equal(300d,target.NotificationWidth);Assert.Equal("d",target.DeviceAliases["c"]);Assert.False(target.DeviceAliases.ContainsKey("a"));
    }
    [Theory][InlineData("TopLeft")][InlineData("TopRight")][InlineData("BottomLeft")][InlineData("BottomRight")]
    public void AllNotificationCornersFitWorkArea(string corner){
        var work=new Rect(-1200,0,1200,800);var slots=NotificationPlacement.Stack(work,1.5,new double[]{76,114,76},340,corner);
        Assert.Equal(3,slots.Length);Assert.All(slots,r=>Assert.True(work.Contains(r)));
        Assert.Equal(corner.EndsWith("Left")?work.Left+18:work.Right-18-510,slots[0].X);
        for(int i=1;i<slots.Length;i++)Assert.False(slots[i].IntersectsWith(slots[i-1]));
    }
    [Fact]public void LowAlertOnlyRepeatsAtNewThresholdOrInterval(){
        var now=DateTimeOffset.UnixEpoch;
        Assert.True(RingNotificationPolicy.ShouldNotifyLow(null,20,30,now));
        Assert.False(RingNotificationPolicy.ShouldNotifyLow((20,now),20,30,now.AddMinutes(1)));
        Assert.True(RingNotificationPolicy.ShouldNotifyLow((20,now),10,30,now.AddMinutes(1)));
        Assert.True(RingNotificationPolicy.ShouldNotifyLow((20,now),20,30,now.AddMinutes(30)));
        Assert.False(RingNotificationPolicy.ShouldNotifyLow((20,now),20,0,now.AddDays(1)));
    }
}
