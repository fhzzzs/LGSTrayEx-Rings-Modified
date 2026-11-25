using System;
using System.IO;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class DeviceInventoryArchitectureTests
{
    [Fact]public void InventoryRestoresHistoryWithoutPretendingToBeOnline(){
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));string path=Path.Combine(dir,"devices.json");
        var now=DateTimeOffset.UtcNow;
        try{
            DeviceInventoryStore.Save(path,[new TaskbarBattery(DeviceType.Mouse,"Mouse",90,"已连接",false,now,"mouse",PhysicalId:"serial:a")]);
            var row=Assert.Single(DeviceInventoryStore.Load(path));Assert.True(row.Offline);Assert.Null(row.Level);Assert.Equal(now,row.Updated);Assert.Equal("serial:a",row.PhysicalId);
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [Fact]public void InventoryRefreshDoesNotDiscardFullProviderKeyAsLegacy(){
        var now=DateTimeOffset.UtcNow;var center=new PhysicalDeviceCenter(now);var p=new RingPreferences();
        var a=new TaskbarBattery(DeviceType.Mouse,"Mouse",90,"已连接",false,now,"logi:Mouse:M:Native:serial");
        center.RefreshInventory([a],p);
        center.RefreshInventory([a,a with{Key=a.Key+":other"}],p);
        Assert.Equal(2,center.Inventory.Count);
    }
}
