using LGSTrayCore;
using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

internal enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }
internal enum BatteryChargeState { Unknown, Discharging, Charging, Full }
internal enum ReadingQuality { Unknown, Band, Exact, Stale }

internal sealed record TaskbarBattery
{
    public DeviceType Type {get;init;}
    public string Name {get;init;}
    public int? Level {get;init;}
    public bool Charging {get;init;}
    public DateTimeOffset Updated {get;init;}
    public string Key {get;init;}
    public string? ShortLabel {get;init;}
    public string? PhysicalId {get;init;}
    private string detail="已连接";
    private bool full;
    public ConnectionState Connection {get;private init;}
    public BatteryChargeState ChargeState=>Offline?BatteryChargeState.Unknown:full?BatteryChargeState.Full:
        Charging?BatteryChargeState.Charging:Level.HasValue?BatteryChargeState.Discharging:BatteryChargeState.Unknown;
    // Legacy readers and previews can still supply display strings. Normalize
    // once on assignment; all lifecycle decisions read the enum, not the label.
    public string Status {
        get=>Connection switch {
            ConnectionState.Disconnected=>"离线",ConnectionState.Connecting=>"等待连接",
            ConnectionState.Reconnecting=>"重新连接",_=>Stale?"电量数据已过期":detail};
        init {
            Connection=value switch {"离线"=>ConnectionState.Disconnected,"等待连接"=>ConnectionState.Connecting,
                "重新连接"=>ConnectionState.Reconnecting,_=>ConnectionState.Connected};
            full=value=="已充满";detail=value;
        }
    }
    public TaskbarBattery(DeviceType Type,string Name,int? Level,string Status,bool Charging,DateTimeOffset Updated,
        string Key="",string? ShortLabel=null,string? PhysicalId=null)
    {
        this.Type=Type;this.Name=Name;this.Level=Level;this.Status=Status;this.Charging=Charging;this.Updated=Updated;
        this.Key=Key;this.ShortLabel=ShortLabel;this.PhysicalId=PhysicalId;
    }
    public string Category => Type switch { DeviceType.Mouse => "鼠标", DeviceType.Headset => "耳机", DeviceType.Keyboard => "键盘", DeviceType.Gamepad => "手柄", _ => "设备" };
    public bool Offline=>Connection==ConnectionState.Disconnected;
    public bool IsConnected=>Connection==ConnectionState.Connected;
    public TaskbarBattery WithConnection(ConnectionState state)=>this with{Connection=state};
    public bool Stale {get;init;}
    public int? LastKnownLevel {get;init;}
    public ReadingQuality Quality=>Stale?ReadingQuality.Stale:Level is >=0 and <=100?ReadingQuality.Exact:
        ShortLabel is "Empty" or "Low" or "Med" or "Full"?ReadingQuality.Band:ReadingQuality.Unknown;
    public string Percent => Offline ? "Off" : Stale ? "—" : ShortLabel ?? (Level is int value ? $"{value}%" : "—");
    public int BarValue => Level ?? 0;
    public string AccessibleLabel => $"{Category}，{Name}，{Status}，{Percent}";
    public string UpdatedLabel => Stale&&LastKnownLevel is int last ? $"历史电量 {last}% · {Updated.ToLocalTime():HH:mm}" : Updated == DateTimeOffset.MinValue ? "暂无读数" : $"更新于 {Updated.ToLocalTime():HH:mm}";
    public static int Rank(DeviceType type) => type switch { DeviceType.Mouse => 0, DeviceType.Headset => 1, DeviceType.Keyboard => 2, DeviceType.Gamepad => 3, _ => 4 };
    public static int ColorBand(int? level) => level switch { null => -1, < 10 => 0, < 20 => 1, _ => 2 };
    private static bool Valid(LogiDevice d) => d.BatteryPercentage is >= 0 and <= 100 && !d.IsWiredMode;
    private static LogiDevice Best(IEnumerable<LogiDevice> readings) => readings.OrderByDescending(d => d.IsOnline)
        .ThenByDescending(Valid).ThenByDescending(d => d.LastUpdate).ThenBy(d => d.DataSource).ThenBy(d => d.DeviceId, StringComparer.Ordinal).First();
    private static string Identity(LogiDevice d) => !string.IsNullOrWhiteSpace(d.DeviceSignature) ? d.DeviceSignature : d.DeviceId;
    private static string Model(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    // Merge one Native and one G HUB reading only when the model is unambiguous.
    // Multiple units of the same model on either source remain separate slots.
    public static IReadOnlyList<TaskbarBattery> ReadAll(IEnumerable<LogiDevice> devices)
    {
        var rows = new List<TaskbarBattery>();
        foreach (var model in devices.Where(d => d.HasBattery && Rank(d.DeviceType) < 4)
            .GroupBy(d => (d.DeviceType, Model(d.DeviceName))))
        {
            var sources = model.GroupBy(d => d.DataSource).ToArray();
            string key = $"logi:{model.Key.DeviceType}:{model.Key.Item2}";
            if (sources.All(s => s.Count() == 1))
                rows.Add(From(Best(model), key));
            else
                foreach (var device in model.OrderBy(d => Identity(d), StringComparer.Ordinal))
                    rows.Add(From(device, $"{key}:{device.DataSource}:{Identity(device)}"));
        }
        return rows.OrderBy(d => Rank(d.Type)).ThenBy(d => d.Key, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<TaskbarBattery> ReadSources(IEnumerable<LogiDevice> devices) => devices
        .Where(d=>d.HasBattery&&Rank(d.DeviceType)<4)
        .GroupBy(d=>$"logi:{d.DeviceType}:{Model(d.DeviceName)}:{d.DataSource}:{Identity(d)}",StringComparer.Ordinal)
        .Select(g=>From(Best(g),g.Key)).ToArray();

    public static TaskbarBattery From(LogiDevice device, string key)
    {
        bool charging = device.IsOnline && (device.IsWiredMode || device.PowerSupplyStatus == PowerSupplyStatus.CHARGING);
        int? level = device.IsOnline && Valid(device) ? (int)Math.Round(device.BatteryPercentage, MidpointRounding.AwayFromZero) : null;
        // A G HUB inventory-only record with no battery reading is not an online device.
        // A later valid update promotes it normally.
        bool inventoryOnly=device.DataSource==DataSource.GHub&&device.LastUpdate==DateTimeOffset.MinValue&&!level.HasValue&&!charging;
        string status = !device.IsOnline||inventoryOnly ? "离线" : device.PowerSupplyStatus == PowerSupplyStatus.FULL ? "已充满"
            : charging ? "充电中" : level.HasValue ? "已连接" : "等待读数";
        return new(device.DeviceType, device.DeviceName, level, status, charging, device.LastUpdate, key, PhysicalId:device.PhysicalId);
    }

    public static TaskbarBattery Waiting(DeviceType type, string name, string key) => new(type, name, null, "等待连接", false, DateTimeOffset.MinValue, key);
}
