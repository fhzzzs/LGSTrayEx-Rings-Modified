using System;
using Xunit;
namespace LGSTrayUI.Tests;
public class RingMotionTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(4d)]
    [InlineData(16d)]
    public void SharedProgressPreservesSpacingDuringInsertionAndRemoval(double gap)
    {
        double size=38,step=size+gap;
        double[] from=[0,step,2*step],to=[0,2*step,3*step];
        Assert.True(RingMotionGeometry.CanInterpolate(from,to,size));
        for(int i=-10;i<=110;i++){
            double p=i/100d;
            for(int j=1;j<from.Length;j++){
                Assert.True(RingMotionGeometry.Position(from[j],to[j],p)-RingMotionGeometry.Position(from[j-1],to[j-1],p)>=size-.001);
                Assert.True(RingMotionGeometry.Position(to[j],from[j],p)-RingMotionGeometry.Position(to[j-1],from[j-1],p)>=size-.001);
            }
        }
    }
    [Fact] public void ReorderedOrOverlappingStartPositionsCannotAnimate()
    {
        Assert.False(RingMotionGeometry.CanInterpolate([42,0],[0,42],38));
        Assert.False(RingMotionGeometry.CanInterpolate([0,20],[0,42],38));
    }
    [Fact] public void CenteredStartAllowsVerifiedLeadingGap()
    {
        Assert.True(TaskbarPlacement.TryLeftFallback(0,900,420,468,24,84,16,16,0,[(420,468),(468,520)],out int x));
        Assert.Equal(16,x);
    }
    [Fact] public void LeftAlignedStartRequiresAdjacentGap()
    {
        Assert.False(TaskbarPlacement.TryLeftFallback(0,900,0,48,24,84,16,16,0,[(0,48),(48,100)],out _));
        Assert.True(TaskbarPlacement.TryLeftFallback(0,900,0,48,24,84,16,16,0,[(0,48),(200,250)],out int x));
        Assert.Equal(64,x);
    }
}
