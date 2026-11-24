using Xunit;
namespace LGSTrayUI.Tests;
public class Rings7Tests
{
    [Fact] public void NativeReservationIncludesBothMarginsAndReleasesAtZero(){
        Assert.Equal((16,16,112),TaskbarLayoutGeometry.Dimensions(80,16,16,0));
        Assert.Equal((0,0,0),TaskbarLayoutGeometry.Dimensions(0,16,16,0));
        Assert.Equal((26,6,112),TaskbarLayoutGeometry.Dimensions(80,16,16,10));
        Assert.Equal((0,36,116),TaskbarLayoutGeometry.Dimensions(80,16,16,-20));
    }
    [Fact] public void AcknowledgedReservationStillRequiresActualGap(){
        Assert.True(TaskbarPlacement.TryReserved(48,800,80,16,16,[(0,48),(160,200)],out int x));Assert.Equal(64,x);
        Assert.False(TaskbarPlacement.TryReserved(48,800,80,16,16,[(0,48),(120,200)],out _));
        Assert.False(TaskbarPlacement.TryReserved(48,150,80,16,16,[],out _));
    }
    [Fact] public void OversizedOrInvalidLeaseIsRejected(){
        Assert.Equal((0,0,0),TaskbarLayoutGeometry.Dimensions(900,16,16,0));
        Assert.Equal((0,0,0),TaskbarLayoutGeometry.Dimensions(double.NaN,16,16,0));
    }
}
