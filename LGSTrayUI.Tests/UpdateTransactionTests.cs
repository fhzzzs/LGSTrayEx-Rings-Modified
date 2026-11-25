using System;
using System.IO;
using Xunit;
namespace LGSTrayUI.Tests;
public class UpdateTransactionTests{
    [Fact]public void CleanupNeverDeletesCurrentPreviousOrUnownedFolders(){
        string root=Path.Combine(Path.GetTempPath(),"rings-test-"+Guid.NewGuid().ToString("N"));
        try{
            string New(string name,bool owned){string path=Path.Combine(root,name);Directory.CreateDirectory(path);if(owned)File.WriteAllText(Path.Combine(path,UpdateTransaction.OwnershipMarker),"test");Directory.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-10));return path;}
            var a=New("rings-12.0.0-aaaaaaaa",true);var b=New("rings-11.0.2-bbbbbbbb",true);var old=New("rings-11.0.1-cccccccc",true);var unknown=New("rings-11.0.0-dddddddd",false);
            Assert.Equal(1,UpdateTransaction.CleanOldVersions(root,Path.Combine(a,"LGSTray.exe"),Path.Combine(b,"LGSTray.exe"),DateTimeOffset.UtcNow));
            Assert.True(Directory.Exists(a));Assert.True(Directory.Exists(b));Assert.False(Directory.Exists(old));Assert.True(Directory.Exists(unknown));
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]public void PortableConfigurationPreservesOverrideWithoutReplacingNewDefaults(){
        string root=Path.Combine(Path.GetTempPath(),"rings-test-"+Guid.NewGuid().ToString("N"));var old=Path.Combine(root,"old");var next=Path.Combine(root,"new","app");
        try{Directory.CreateDirectory(old);Directory.CreateDirectory(next);
            File.WriteAllText(Path.Combine(old,"appsettings.toml"),"old");File.WriteAllText(Path.Combine(old,"appsettings.local.toml"),"override");File.WriteAllText(Path.Combine(next,"appsettings.toml"),"new");
            UpdateTransaction.PreserveConfiguration(old,Path.Combine(root,"new"));
            Assert.Equal("new",File.ReadAllText(Path.Combine(next,"appsettings.toml")));Assert.Equal("old",File.ReadAllText(Path.Combine(next,"appsettings.previous.toml")));Assert.Equal("override",File.ReadAllText(Path.Combine(next,"appsettings.local.toml")));
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
