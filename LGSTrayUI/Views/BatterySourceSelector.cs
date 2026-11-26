using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

// Selection is independent of window placement and rendering. A source remains
// preferred while healthy; only confirmed usable data can replace it.
internal sealed class BatterySourceSelector
{
    private readonly Dictionary<string,string> selected=new(StringComparer.Ordinal);
    private readonly HashSet<string> retained = new(StringComparer.Ordinal);
    internal static bool Usable(TaskbarBattery row)=>row.IsConnected&&row.Level is >=0 and <=100;
    private static bool NativeFull(TaskbarBattery row)=>row.IsFull&&row.Key.Contains(":Native:",StringComparison.Ordinal);
    internal static TaskbarBattery? ConfirmedFull(ReadOnlySpan<SourceReading> fresh)
    {
        TaskbarBattery? full = null;
        foreach (var observation in fresh) {
            if (observation.Quality != ReadingQuality.Exact) continue;
            var row = observation.Row;
            if (row.Level < 95) return null;
            if (NativeFull(row) && (full == null || row.Updated > full.Updated)) full = row;
        }
        if (full == null) return null;
        foreach (var observation in fresh) {
            var row = observation.Row;
            if (observation.Quality == ReadingQuality.Exact && row.Key != full.Key &&
                row.Updated > full.Updated.AddSeconds(30) && !row.IsFull) return null;
        }
        return full;
    }

    private SourceReading Choose(DeviceGroup group, RingPreferences preferences, DateTimeOffset now)
    {
        var sources = group.Sources;
        if (sources.Length == 1) return SourceReading.Normalize(sources[0], now, preferences);

        string root = group.Reading.Key;
        preferences.PreferredSources.TryGetValue(root, out var preferred);
        var buffer = new SourceReading[sources.Length];
        int count = 0;
        bool hasNativeFull = false, hasOtherExact = false;
        foreach (var row in sources) {
            if (!row.IsConnected) continue;
            var observation = SourceReading.Normalize(row, now, preferences);
            if (observation.Quality is not (ReadingQuality.Exact or ReadingQuality.Band)) continue;
            if (row.Key == preferred) return observation;
            buffer[count++] = observation;
            if (observation.Quality == ReadingQuality.Exact) {
                if (NativeFull(row)) hasNativeFull = true;
                else hasOtherExact = true;
            }
        }
        if (count == 0) return SourceReading.Normalize(DeviceIdentityManager.Best(sources, null), now, preferences);
        if (count == 1) return buffer[0];
        var fresh = buffer.AsSpan(0, count);
        if (hasNativeFull && ConfirmedFull(fresh) is { } full)
            return SourceReading.Normalize(full, now, preferences);

        bool preferXboxBle = group.Inferred && Array.Exists(sources, ControllerAliasMatcher.IsGip);
        selected.TryGetValue(root, out var previous);
        SourceReading? retainedSource = null;
        SourceReading best = default;
        int bestRank = int.MinValue;
        foreach (var observation in fresh) {
            var row = observation.Row;
            // A conflicting exact report releases a native FULL selection.
            if (hasNativeFull && hasOtherExact && NativeFull(row)) continue;
            bool exact = observation.Quality == ReadingQuality.Exact;
            if (exact && preferXboxBle && ControllerAliasMatcher.IsXboxBle(row)) return observation;
            if (exact && row.Key == previous) retainedSource ??= observation;
            // Band readings use the ordinary source order. Confidence only ranks exact readings.
            int rank = exact ? observation.Confidence : -1;
            if (rank > bestRank || rank == bestRank && DeviceIdentityManager.RanksAhead(row, best.Row, null)) {
                best = observation;
                bestRank = rank;
            }
        }
        return retainedSource ?? best;
    }
    internal DeviceGroup[] Select(IEnumerable<DeviceGroup> groups,RingPreferences preferences,DateTimeOffset now)
    {
        var input = groups as DeviceGroup[] ?? groups.ToArray();
        var result = new DeviceGroup[input.Length];
        retained.Clear();
        for (int i = 0; i < input.Length; i++)
        {
            var group = input[i];
            string root=group.Reading.Key;
            retained.Add(root);
            var observation = Choose(group, preferences, now);
            var chosen = observation.Row;
            if(observation.Quality==ReadingQuality.Stale)
                chosen=chosen with{LastKnownLevel=chosen.Level??chosen.LastKnownLevel,Level=null,ShortLabel=null,Stale=true,Charging=false};
            if(chosen.Level is int level&&(level<0||level>100))chosen=chosen with{Level=null};
            if(chosen.IsFull&&chosen.Charging)chosen=chosen with{Charging=false};
            selected[root]=chosen.Key;RingDiagnostics.Source(root,chosen.Key);
            // Preserve group identity/name and all individual source data.
            result[i] = group with { Reading = chosen with { Key = root, Name = group.Reading.Name } };
        }
        if (selected.Count != retained.Count)
            foreach (var key in selected.Keys.ToArray())
                if (!retained.Contains(key)) selected.Remove(key);
        return result;
    }
}
