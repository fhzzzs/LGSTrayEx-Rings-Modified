using Xunit;

namespace LGSTrayUI.Tests;

public class TaskbarPlacementStabilityTests
{
    private static TaskbarPlacement.Anchor Anchor(int monitor=1,double dpi=1,int width=100,int trayX=1600)
        =>new((nint)10,(nint)20,(nint)monitor,
            new(){Left=0,Top=1040,Right=1920,Bottom=1080},
            new(){Left=trayX,Top=1040,Right=1920,Bottom=1080},
            new(){Left=0,Top=0,Right=1920,Bottom=1080},
            dpi,width,40,1040,48,trayX,4,4,0);

    [Fact]
    public void PendingRefreshKeepsExistingAnchorButCannotExtendDeadline(){
        var state=new TaskbarPlacementStability();var anchor=Anchor();
        Assert.False(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,100,out _));
        state.Verified(anchor);
        Assert.True(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,200,out int first));
        Assert.Equal(TaskbarPlacementStability.HoldMs,first);
        Assert.True(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,849,out int last));
        Assert.Equal(1,last);
        Assert.False(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,850,out _));
        Assert.False(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,851,out _));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(2)]
    public void UnsafeResultsCannotHold(int code){
        var status=(TaskbarPlacementStatus)code;
        var state=new TaskbarPlacementStability();var anchor=Anchor();
        state.Verified(anchor);
        Assert.False(state.TryHold(status,anchor,100,out _));
        Assert.False(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,101,out _));
    }

    [Fact]
    public void MonitorDpiTrayAndWidthChangesNeverReusePreviousPlacement(){
        var state=new TaskbarPlacementStability();var anchor=Anchor();
        foreach(var changed in new[]{Anchor(monitor:2),Anchor(dpi:1.5),Anchor(width:140),Anchor(trayX:1500),anchor with{Bar=(nint)99}}){
            state.Verified(anchor);
            Assert.False(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,changed,200,out _));
        }
    }

    [Fact]
    public void FreshVerificationRearmsHoldAndHardResetRevokesIt(){
        var state=new TaskbarPlacementStability();var anchor=Anchor();
        state.Verified(anchor);
        Assert.True(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,100,out _));
        state.Verified(anchor);
        Assert.True(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,800,out int remaining));
        Assert.Equal(TaskbarPlacementStability.HoldMs,remaining);
        state.Reset();
        Assert.False(state.TryHold(TaskbarPlacementStatus.AwaitingProbe,anchor,801,out _));
    }
}
