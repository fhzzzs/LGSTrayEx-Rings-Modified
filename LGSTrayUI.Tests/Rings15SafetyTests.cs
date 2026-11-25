using System;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings15SafetyTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void InvalidBatteryLevelDoesNotDisplayAsPercentage(int value)
    {
        var row=new TaskbarBattery(DeviceType.Gamepad,"Controller",value,"已连接",false,DateTimeOffset.UtcNow,"test:controller");
        Assert.Equal("—",row.Percent);
        Assert.Equal(0,row.BarValue);
        Assert.Equal(ReadingQuality.Unknown,row.Quality);
    }
}
