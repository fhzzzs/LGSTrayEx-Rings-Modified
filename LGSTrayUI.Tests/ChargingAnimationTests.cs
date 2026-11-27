using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using LGSTrayPrimitives;
using Xunit;

namespace LGSTrayUI.Tests;

public class ChargingAnimationTests
{
    [Fact]
    public void EmptyToVisibleAndVisibleToEmptyFramesInvalidateWpfRendering()
    {
        Exception? error=null;
        var thread=new Thread(()=>{
            try{
                var ring=new BatteryRing{
                    Preferences=new RingPreferences{ChargeLength=15,Animate=true,ChargeAnimate=true},
                    PreviewLight=false,
                    DataContext=new TaskbarBattery(DeviceType.Mouse,"Mouse",65,"充电中",true,DateTimeOffset.UtcNow,"test:charging")
                };
                void Layout(){
                    ring.Measure(new Size(64,64));
                    ring.Arrange(new Rect(0,0,64,64));
                    ring.UpdateLayout();
                    Assert.True(ring.IsArrangeValid);
                }
                Layout();
                ring.SetShinePhase(0);
                Assert.True(ring.IsArrangeValid);
                ring.SetShinePhase(.2);
                Assert.False(ring.IsArrangeValid);
                Layout();
                ring.SetShinePhase(.3);
                Assert.False(ring.IsArrangeValid);
                Layout();
                ring.SetShinePhase(.99);
                Assert.False(ring.IsArrangeValid);
                Layout();
                ring.SetShinePhase(0);
                Assert.True(ring.IsArrangeValid);
                ring.SetShinePhase(.1);
                Assert.False(ring.IsArrangeValid);
                Layout();
                ring.DataContext=null;
            }catch(Exception ex){error=ex;}
        }){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)),"WPF animation test timed out.");
        if(error!=null)ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(65)]
    [InlineData(99)]
    public void SweepMovesAndReturnsOnEveryCycle(int level)
    {
        double previousStart=-1;
        int visible=0;
        for(int i=0;i<=1000;i++){
            var (start,end)=BatteryRing.ShineSegment(i/1000d,level,15);
            Assert.InRange(start,0,level*3.6);
            Assert.InRange(end,start,level*3.6);
            Assert.True(start>=previousStart);
            previousStart=start;
            if(end-start>.01)visible++;
        }
        Assert.True(visible>5);
        Assert.Equal((0d,0d),BatteryRing.ShineSegment(0,level,15));
        var last=BatteryRing.ShineSegment(1,level,15);
        Assert.Equal(last.start,last.end,8);
    }
}
