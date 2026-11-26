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
    public ushort HardwareVendorId {get;init;}
    public ushort HardwareProductId {get;init;}
    private string detail="已连接";
    private bool full;
    public ConnectionState Connection {get;private init;}
    public BatteryChargeState ChargeState=>!IsConnected||Stale?BatteryChargeState.Unknown:full?BatteryChargeState.Full:
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
    public bool Estimated {get;init;}
    public bool Stale {get;init;}
    public int? LastKnownLevel {get;init;}
    public ReadingQuality Quality=>Stale?ReadingQuality.Stale:Level is >=0 and <=100?(Estimated?ReadingQuality.Band:ReadingQuality.Exact):
        ShortLabel is "Empty" or "Low" or "Med" or "Full" or "1/4" or "2/4" or "3/4" or "4/4"?ReadingQuality.Band:ReadingQuality.Unknown;
    // Timestamp-only refreshes must not rebuild UI or allocate record clones.
    internal static bool SamePresentation(TaskbarBattery a,TaskbarBattery b)=>a.Key==b.Key&&a.Type==b.Type&&a.Name==b.Name&&a.Level==b.Level&&a.Connection==b.Connection&&a.ChargeState==b.ChargeState&&a.Charging==b.Charging&&a.Status==b.Status&&a.ShortLabel==b.ShortLabel&&a.PhysicalId==b.PhysicalId&&a.HardwareVendorId==b.HardwareVendorId&&a.HardwareProductId==b.HardwareProductId&&a.Estimated==b.Estimated&&a.Stale==b.Stale&&a.LastKnownLevel==b.LastKnownLevel;
    public string Percent => Offline ? "Off" : Stale ? "—" : ShortLabel ?? (Level is int value && value is >=0 and <=100 ? $"{value}%" : "—");
    public bool IsFull=>IsConnected&&!Stale&&ChargeState==BatteryChargeState.Full;
    public bool IsActivelyCharging=>IsConnected&&!Stale&&!IsFull&&Charging;
    public bool ConnectionOnly=>IsConnected&&!Stale&&Quality==ReadingQuality.Unknown&&!Charging;
    public string RingLabel=>Offline?"Off":Connection is ConnectionState.Reconnecting or ConnectionState.Connecting?"…":Stale?"N/A":IsFull?"Full":ConnectionOnly?"ON":Quality==ReadingQuality.Unknown&&Charging?"N/A":Percent;
    public string DisplayStatus=>Status+" · "+RingLabel;
    public int BarValue => IsFull?100:Level is >=0 and <=100 ? Level.Value : 0;
    public string AccessibleLabel => $"{Category}，{Name}，{Status}，{RingLabel}";
    public string UpdatedLabel => Stale&&LastKnownLevel is int last ? $"历史电量 {last}% · {Updated.ToLocalTime():HH:mm}" : Updated == DateTimeOffset.MinValue ? "暂无读数" : $"更新于 {Updated.ToLocalTime():HH:mm}";
    public static int Rank(DeviceType type) => type switch { DeviceType.Mouse => 0, DeviceType.Headset => 1, DeviceType.Keyboard => 2, DeviceType.Gamepad => 3, _ => 4 };
    public static int ColorBand(int? level) => level switch { null => -1, < 10 => 0, < 20 => 1, _ => 2 };
    private static bool Valid(LogiDevice d) => d.BatteryPercentage is >= 0 and <= 100 && !d.IsWiredMode;
    private static bool RanksAhead(LogiDevice candidate,LogiDevice current)
    {
        if(candidate.IsOnline!=current.IsOnline)return candidate.IsOnline;
        bool valid=Valid(candidate),currentValid=Valid(current);
        if(valid!=currentValid)return valid;
        int time=candidate.LastUpdate.CompareTo(current.LastUpdate);
        if(time!=0)return time>0;
        int source=Comparer<DataSource>.Default.Compare(candidate.DataSource,current.DataSource);
        if(source!=0)return source<0;
        return StringComparer.Ordinal.Compare(candidate.DeviceId,current.DeviceId)<0;
    }
    private static string Identity(LogiDevice d) => !string.IsNullOrWhiteSpace(d.DeviceSignature) ? d.DeviceSignature : d.DeviceId;
    private static string Model(string name)
    {
        int count=0;
        foreach(char ch in name)if(char.IsLetterOrDigit(ch))count++;
        return string.Create(count,name,static (span,value)=>{
            int i=0;
            foreach(char ch in value)if(char.IsLetterOrDigit(ch))span[i++]=char.ToUpperInvariant(ch);
        });
    }

    public static IReadOnlyList<TaskbarBattery> ReadSources(IEnumerable<LogiDevice> devices)
    {
        // Keep first-seen group order and the original stable tie-breaker, without grouping or sorting.
        var indexes=new Dictionary<string,int>(StringComparer.Ordinal);
        var winners=new List<(string Key,LogiDevice Device)>();
        foreach(var d in devices){
            if(!d.HasBattery||Rank(d.DeviceType)>=4)continue;
            string key=$"logi:{d.DeviceType}:{Model(d.DeviceName)}:{d.DataSource}:{Identity(d)}";
            if(indexes.TryGetValue(key,out int index)){
                if(RanksAhead(d,winners[index].Device))winners[index]=(key,d);
            }else{indexes.Add(key,winners.Count);winners.Add((key,d));}
        }
        var rows=winners.Count==0?Array.Empty<TaskbarBattery>():new TaskbarBattery[winners.Count];
        for(int i=0;i<rows.Length;i++)rows[i]=From(winners[i].Device,winners[i].Key);
        return rows;
    }

    public static TaskbarBattery From(LogiDevice device, string key)
    {
        bool charging = device.IsOnline && device.PowerSupplyStatus!=PowerSupplyStatus.FULL && (device.IsWiredMode || device.PowerSupplyStatus == PowerSupplyStatus.CHARGING);
        int? level = device.IsOnline && Valid(device) ? (int)Math.Round(device.BatteryPercentage, MidpointRounding.AwayFromZero) : null;
        if (device.IsOnline && device.PowerSupplyStatus == PowerSupplyStatus.FULL) level ??= 100;
        // A G HUB inventory-only record with no battery reading is not an online device.
        // A later valid update promotes it normally.
        bool inventoryOnly=device.DataSource==DataSource.GHub&&device.LastUpdate==DateTimeOffset.MinValue&&!level.HasValue&&!charging;
        string status = !device.IsOnline||inventoryOnly ? "离线" : device.PowerSupplyStatus == PowerSupplyStatus.FULL ? "已充满"
            : charging ? "充电中" : level.HasValue ? "已连接" : "等待读数";
        return new(device.DeviceType, device.DeviceName, level, status, charging, device.LastUpdate, key, PhysicalId:device.PhysicalId);
    }

    public static TaskbarBattery Waiting(DeviceType type, string name, string key) => new(type, name, null, "等待连接", false, DateTimeOffset.MinValue, key);
}
