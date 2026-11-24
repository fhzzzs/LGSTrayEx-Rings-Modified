using LGSTrayCore;
using LGSTrayPrimitives;

namespace LGSTrayUI.Tests;

public class TaskbarBatteryTests
{
    [Fact]
    public void DevicesSortByCategoryAndDoNotDropExtraMice()
    {
        LogiDevice[] devices = [new() { DeviceType = DeviceType.Keyboard, DeviceName = "Keyboard" },
            new() { DeviceType = DeviceType.Headset, DeviceName = "Headset" },
            new() { DeviceType = DeviceType.Mouse, DeviceName = "Mouse A" },
            new() { DeviceType = DeviceType.Mouse, DeviceName = "Mouse B" },
            new() { DeviceType = DeviceType.Gamepad, DeviceName = "Controller" },
            new() { DeviceType = DeviceType.Headset, HasBattery = false }];
        var rows = TaskbarBattery.ReadAll(devices);
        Assert.Equal(new[] { DeviceType.Mouse, DeviceType.Mouse, DeviceType.Headset, DeviceType.Keyboard, DeviceType.Gamepad }, rows.Select(r => r.Type));
        Assert.All(rows, row => Assert.Equal("Off", row.Percent));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(100, 2)]
    [InlineData(null, -1)]
    public void AppleColorBoundaries(int? percent, int band) => Assert.Equal(band, TaskbarBattery.ColorBand(percent));

    [Theory]
    [InlineData(true, false, 0, nameof(PowerSupplyStatus.DISCHARGING), "0%", "已连接")]
    [InlineData(true, false, 99.6, nameof(PowerSupplyStatus.CHARGING), "100%", "充电中")]
    [InlineData(true, false, 100, nameof(PowerSupplyStatus.FULL), "100%", "已充满")]
    [InlineData(false, false, 81, nameof(PowerSupplyStatus.DISCHARGING), "Off", "离线")]
    [InlineData(true, true, 81, nameof(PowerSupplyStatus.UNKNOWN), "—", "充电中")]
    [InlineData(true, false, -1, nameof(PowerSupplyStatus.UNKNOWN), "—", "等待读数")]
    [InlineData(true, false, 101, nameof(PowerSupplyStatus.UNKNOWN), "—", "等待读数")]
    public void CurrentReadingDistinguishesUnknownOfflineAndEmpty(bool online, bool cableTransition,
        double percent, string power, string expectedPercent, string expectedStatus)
    {
        var device = new LogiDevice { DeviceType = DeviceType.Headset, DeviceName = "PRO X 2",
            BatteryPercentage = percent, IsOnline = online, IsWiredMode = cableTransition,
            PowerSupplyStatus = Enum.Parse<PowerSupplyStatus>(power) };
        var result = TaskbarBattery.ReadAll([device]).Single();
        Assert.Equal(expectedPercent, result.Percent);
        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public void ReconnectSelectsLiveReadingForTargetAcrossSources()
    {
        var native = new LogiDevice { DeviceType = DeviceType.Mouse, DeviceName = "PRO X SUPERLIGHT 2",
            BatteryPercentage = 27, IsOnline = false, LastUpdate = DateTimeOffset.Now };
        var ghub = new LogiDevice { DeviceType = DeviceType.Mouse, DeviceName = native.DeviceName,
            DataSource = DataSource.GHub, BatteryPercentage = 34, IsOnline = true };
        var otherMouse = new LogiDevice { DeviceType = DeviceType.Mouse, DeviceName = "G903", IsOnline = true,
            LastUpdate = DateTimeOffset.Now, BatteryPercentage = 80 };
        var devices = new[] { otherMouse, native, ghub };
        Assert.Equal("34%", TaskbarBattery.ReadAll(devices).Single(d => d.Name == native.DeviceName).Percent);
        native.IsOnline = true;
        Assert.Equal("27%", TaskbarBattery.ReadAll(devices).Single(d => d.Name == native.DeviceName).Percent);
    }

    [Theory]
    [InlineData(0, 0, 1, 48)]
    [InlineData(-2560, -200, 1.25, 60)]
    [InlineData(1920, 100, 1.5, 72)]
    [InlineData(0, 0, 2, 96)]
    public void RingsStayInsideTheBarImmediatelyLeftOfTheTray(int x, int y, double scale, int thickness)
    {
        var screen = Rect(x, y, x + 1920, y + 1080);
        var bar = Rect(x, screen.Bottom - thickness, screen.Right, screen.Bottom);
        var tray = Rect(screen.Right - 270, bar.Top, screen.Right, bar.Bottom);
        Assert.True(TaskbarPlacement.TryFit(bar, tray, screen, scale, out var p));
        Assert.Equal(tray.Left - (int)Math.Round(4 * scale), p.X + p.Width);
        Assert.InRange(p.Y, bar.Top, bar.Bottom - p.Height);
        Assert.Equal(86d, p.LogicalWidth, 0);
        Assert.Equal(40d, p.LogicalHeight, 0);
    }

    [Fact]
    public void FourSlotsGrowLeftwards()
    {
        var screen = Rect(0, 0, 1920, 1080);
        var bar = Rect(0, 1032, 1920, 1080);
        var tray = Rect(1650, 1032, 1920, 1080);
        Assert.True(TaskbarPlacement.TryFit(bar, tray, screen, 1, out var p, 4));
        Assert.Equal(178, p.Width);
        Assert.Equal(1646, p.X + p.Width);
    }

    [Fact]
    public void SameModelMultipleUnitsAreNotMerged()
    {
        var first = new LogiDevice { DeviceType = DeviceType.Mouse, DeviceName = "G903", DeviceId = "a" };
        var second = new LogiDevice { DeviceType = DeviceType.Mouse, DeviceName = "G903", DeviceId = "b" };
        var rows = TaskbarBattery.ReadAll([first, second]);
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].Key, rows[1].Key);
    }

    [Theory]
    [InlineData("GameSir G7 Pro", 0, 0, DeviceType.Gamepad)]
    [InlineData("Generic", 15, 1, DeviceType.Keyboard)]
    [InlineData("Generic", 15, 2, DeviceType.Mouse)]
    [InlineData("Wireless Headphones", 0, 0, DeviceType.Headset)]
    public void BluetoothClassification(string name, ushort category, ushort subcategory, DeviceType expected) =>
        Assert.Equal<DeviceType?>(expected, BluetoothBatteryReader.Classify(name, category, subcategory));

    [Fact]
    public void AutoHiddenBarAndMissingTrayHaveNoOverlay()
    {
        var screen = Rect(0, 0, 1920, 1080);
        Assert.False(TaskbarPlacement.TryFit(Rect(0, 1078, 1920, 1126), Rect(1648, 1078, 1920, 1126),
            screen, 1, out _));
        Assert.False(TaskbarPlacement.TryFit(Rect(0, 1032, 1920, 1080), default, screen, 1, out _));
    }

    private static TaskbarPlacement.PixelRect Rect(int l, int t, int r, int b) =>
        new() { Left = l, Top = t, Right = r, Bottom = b };
}
