using System;
using System.IO;
using Xunit;
namespace LGSTrayUI.Tests;
public class PortableLayoutTests
{
    [Fact]public void BrandedLayoutUsesNewLauncherAndWorker(){
        string root=Path.Combine(Path.GetTempPath(),"Battery Monitor test "+Guid.NewGuid().ToString("N"));
        string inner=Path.Combine(root,"app");
        try{
            Directory.CreateDirectory(inner);
            foreach(var name in new[]{"Battery Monitor.exe","Battery Monitor HID.exe","hidapi.dll","appsettings.toml"})File.WriteAllText(Path.Combine(inner,name),"");
            var entry=Path.Combine(root,"Battery Monitor.exe");File.WriteAllText(entry,"");
            Assert.True(PortableLayout.IsCompletePackage(root));
            Assert.Equal(entry,PortableLayout.EntryExecutable(Path.Combine(inner,"Battery Monitor.exe")));
            Assert.Equal(entry,PortableLayout.FindEntry(root));
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]public void NewAndLegacyLayoutsValidateAndResolveEntry(){
        string root=Path.Combine(Path.GetTempPath(),"rings layout "+Guid.NewGuid().ToString("N"));
        string inner=Path.Combine(root,"app");
        try{
            Directory.CreateDirectory(inner);
            foreach(var name in new[]{"LGSTray.exe","LGSTrayHID.exe","hidapi.dll","appsettings.toml"})
                File.WriteAllText(Path.Combine(inner,name),"");
            var entry=Path.Combine(root,"LGSTray.exe");File.WriteAllText(entry,"");
            Assert.True(PortableLayout.IsCompletePackage(root));
            Assert.True(PortableLayout.IsCompletePackage(inner));
            Assert.Equal(entry,PortableLayout.EntryExecutable(Path.Combine(inner,"LGSTray.exe")));
            Assert.Equal(entry,PortableLayout.EntryExecutable(entry));
            File.Delete(Path.Combine(inner,"hidapi.dll"));
            Assert.False(PortableLayout.IsCompletePackage(root));
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
