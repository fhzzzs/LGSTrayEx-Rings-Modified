using Xunit;
namespace LGSTrayUI.Tests;
public class Rings9Tests
{
    [Theory]
    [InlineData(22621,true)] [InlineData(22631,true)] [InlineData(26100,true)] [InlineData(26200,true)]
    [InlineData(22000,false)] [InlineData(26300,false)] [InlineData(19045,false)]
    public void OnlyExplicitlyAllowedBuildsAttach(int build,bool expected)=>Assert.Equal(expected,NativeTaskbarSlot.SupportedBuild(build));
    [Fact] public void AcknowledgementRequiresCurrentMatchingLease(){
        Assert.True(NativeTaskbarSlot.FreshReply(10000,9950,9800,112,112,1));
        Assert.False(NativeTaskbarSlot.FreshReply(10000,9700,9800,112,112,1));
        Assert.False(NativeTaskbarSlot.FreshReply(10000,9950,9800,112,113,1));
        Assert.False(NativeTaskbarSlot.FreshReply(10000,9950,9800,112,112,-1));
        Assert.False(NativeTaskbarSlot.FreshReply(10000,8000,7000,112,112,1));
        Assert.False(NativeTaskbarSlot.FreshReply(10000,10001,9800,112,112,1));
        Assert.False(NativeTaskbarSlot.FreshReply(10000,9950,9800,0,0,1));
    }
    [Fact] public void AckIsNotEnoughWithoutActualNativeClearance(){
        Assert.False(TaskbarPlacement.TryReserved(48,800,80,16,16,[(0,48),(100,140)],out _));
        Assert.True(TaskbarPlacement.TryReserved(48,800,80,16,16,[(0,48),(160,200)],out int x));
        Assert.Equal(64,x);
    }
}
