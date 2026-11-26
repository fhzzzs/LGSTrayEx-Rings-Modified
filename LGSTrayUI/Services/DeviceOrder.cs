using System;
using System.Collections.Generic;
using System.Linq;
namespace LGSTrayUI;
internal static class DeviceOrder{
    internal static List<string> MergeDrag(IEnumerable<string> previous, IEnumerable<string> visible)
    {
        var keys = visible.Distinct(StringComparer.Ordinal).ToArray();
        var moving = keys.ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        int next = 0;
        foreach (string key in previous.Distinct(StringComparer.Ordinal))
            result.Add(moving.Contains(key) ? keys[next++] : key);
        for (; next < keys.Length; next++) result.Add(keys[next]);
        return result;
    }
    internal static IEnumerable<TaskbarBattery> Sort(IEnumerable<TaskbarBattery> rows,List<string> order)=>Sort(rows,order,d=>d);
    internal static IEnumerable<T> Sort<T>(IEnumerable<T> rows,List<string> order,Func<T,TaskbarBattery> read){
        // Explicit user ordering takes precedence. Unlisted devices append
        // in default category order; clearing the order restores defaults.
        return rows.OrderBy(x=>order.IndexOf(read(x).Key) is int i&&i>=0?i:int.MaxValue)
            .ThenBy(x=>TaskbarBattery.Rank(read(x).Type)).ThenBy(x=>read(x).Key,StringComparer.Ordinal);
    }
}
