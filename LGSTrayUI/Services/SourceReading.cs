using System;

namespace LGSTrayUI;

// One normalized observation; connection and battery freshness are independent.
internal sealed record SourceReading(TaskbarBattery Row,ConnectionState Connection,BatteryChargeState Charge,
    ReadingQuality Quality,DateTimeOffset FreshUntil,int Confidence)
{
    internal static SourceReading Normalize(TaskbarBattery row,DateTimeOffset now,RingPreferences preferences)
    {
        var ttl=row.Key.StartsWith("gip:")||row.Key.StartsWith("xinput:")?TimeSpan.FromSeconds(Math.Max(15,Math.Clamp(preferences.PollSeconds,.5,60)*3)):
            row.Key.StartsWith("ble:")?TimeSpan.FromSeconds(Math.Max(60,Math.Clamp(preferences.BleSeconds,5,120)*3)):
            (row.Key.StartsWith("asus:")||row.Key.StartsWith("astro:"))?TimeSpan.FromSeconds(90):TimeSpan.FromMinutes(20);
        var until=row.Updated==DateTimeOffset.MinValue?DateTimeOffset.MinValue:row.Updated+ttl;
        var quality=row.Quality;
        if(quality is ReadingQuality.Exact or ReadingQuality.Band)
            if(row.Updated>now.AddSeconds(5)||until<now)quality=ReadingQuality.Stale;
        return new(row,row.Connection,row.ChargeState,quality,until,ConfidenceFor(row,quality));
    }
    internal static int ConfidenceFor(TaskbarBattery row,ReadingQuality quality){
        if(quality==ReadingQuality.Band)return row.Estimated?40:25;
        if(quality!=ReadingQuality.Exact)return 0;
        if(row.Key.StartsWith("asus:",StringComparison.Ordinal)||row.Key.StartsWith("astro:",StringComparison.Ordinal)||row.Key.StartsWith("ble:",StringComparison.Ordinal))return 100;
        if(row.Key.StartsWith("logi:",StringComparison.Ordinal))return row.Key.Contains(":Native:",StringComparison.Ordinal)?95:90;
        if(row.Key.StartsWith("gip:",StringComparison.Ordinal))return 70;
        return 80;
    }
}
