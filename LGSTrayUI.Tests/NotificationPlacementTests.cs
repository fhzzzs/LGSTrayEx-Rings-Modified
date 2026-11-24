using System.Windows;
using Xunit;
namespace LGSTrayUI.Tests;
public class NotificationPlacementTests
{
    [Theory]
    [InlineData(1920,1040,1)]
    [InlineData(2560,1400,1.5)]
    [InlineData(3840,2080,2)]
    [InlineData(800,480,1.25)]
    public void PopupsStayInsidePrimaryWorkArea(double width,double height,double scale)
    {
        var work=new Rect(0,0,width,height);
        var slots=NotificationPlacement.Stack(work,scale,20);
        Assert.NotEmpty(slots);
        foreach(var slot in slots)Assert.True(work.Contains(slot));
        for(int i=1;i<slots.Length;i++)Assert.True(slots[i].Bottom<=slots[i-1].Top);
    }
    [Fact] public void WorkAreaOriginAndTaskbarInsetsAreRespected(){
        var work=new Rect(72,40,1200,760);
        var slots=NotificationPlacement.Stack(work,1.5,2);
        Assert.Equal(2,slots.Length);
        Assert.All(slots,slot=>Assert.True(work.Contains(slot)));
        Assert.Equal(1254d,slots[0].Right);
        Assert.Equal(782d,slots[0].Bottom);
    }
}
