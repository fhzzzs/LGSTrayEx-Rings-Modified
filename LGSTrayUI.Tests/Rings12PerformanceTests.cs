using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings12PerformanceTests
{
    [Fact]public void FrameProgressIsClampedAndMonotonic(){
        Assert.Equal(0,RingFrameClock.Progress(10,20,100));Assert.Equal(.5,RingFrameClock.Progress(70,20,100));Assert.Equal(1,RingFrameClock.Progress(200,20,100));
        Assert.Equal(0,RingFrameClock.Smooth(0));Assert.Equal(1,RingFrameClock.Smooth(1));
    }
    [Fact]public async Task LatestSnapshotWinsWithoutBlockingEnqueue(){
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var writes=new List<string>();
        var writer=new AsyncPreferenceWriter((path,value)=>{if(value=="first"){entered.Set();release.Wait(TimeSpan.FromSeconds(5));}writes.Add(value);});
        writer.Enqueue("settings","first");
        try{Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));writer.Enqueue("settings","second");writer.Enqueue("settings","last");}
        finally{release.Set();}
        Assert.True(await writer.FlushAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[]{"first","last"},writes);
    }
}
