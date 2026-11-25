using System;

namespace LGSTrayUI;

// One normalized observation; connection and battery freshness are independent.
internal sealed record SourceReading(TaskbarBattery Row,ConnectionState Connection,BatteryChargeState Charge,
    ReadingQuality Quality,DateTimeOffset FreshUntil,int Confidence)
{
    internal static SourceReading Normalize(TaskbarBattery row,DateTimeOffset now)
    {
        var ttl=row.Key.StartsWith("gip:")||row.Key.StartsWith("xinput:")?TimeSpan.FromSeconds(Math.Max(15,RingConfig.Current.PollSeconds*3)):
            row.Key.StartsWith("ble:")?TimeSpan.FromSeconds(Math.Max(60,RingConfig.Current.BleSeconds*3)):
            row.Key.StartsWith("asus:")?TimeSpan.FromSeconds(40):TimeSpan.FromMinutes(20);
        var until=row.Updated==DateTimeOffset.MinValue?DateTimeOffset.MinValue:row.Updated+ttl;
        var quality=row.Quality;
        if(quality is ReadingQuality.Exact or ReadingQuality.Band)
            if(row.Updated>now.AddSeconds(5)||until<now)quality=ReadingQuality.Stale;
        return new(row,row.Connection,row.ChargeState,quality,until,quality==ReadingQuality.Exact?100:quality==ReadingQuality.Band?30:0);
    }
}
