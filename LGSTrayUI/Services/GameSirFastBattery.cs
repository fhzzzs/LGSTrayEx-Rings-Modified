using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;

namespace LGSTrayUI;

internal static class GameSirFastBattery
{
    private const string Container = "System.Devices.ContainerId";
    private const string Battery = "System.Devices.BatteryLife";
    private static readonly string[] Properties = [Container, Battery];
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private sealed record Observation(string Key, DateTimeOffset Time, int? Percent, string Detail, int Generation);
    private static Observation? last;
    private static int generation;
    private static string lastOnline = "No 3537:10A9 session observed";
    internal static string LastOnline => Volatile.Read(ref lastOnline);
    internal static void Invalidate() => Interlocked.Increment(ref generation);

    internal static int? PublishedPercent(object? value) => value is byte b && b <= 100 ? b : null;

    internal static async Task CheckAsync(List<TaskbarBattery> rows, CancellationToken token)
    {
        int index = rows.FindIndex(IsFastController);
        if (index < 0) { last = null; return; }
        var row = rows[index];
        if (row.Level.HasValue || rows.FindIndex(index + 1, IsFastController) >= 0) {
            last = null;
            Volatile.Write(ref lastOnline, row.Level.HasValue ? "3537:10A9 Windows gaming battery report available" :
                "Multiple 3537:10A9 controllers; device-property association ambiguous");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        int currentGeneration = Volatile.Read(ref generation);
        if (last == null || last.Key != row.Key || last.Generation != currentGeneration || now - last.Time >= Interval) {
            try {
                var result = await InspectAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (currentGeneration != Volatile.Read(ref generation)) return;
                last = new(row.Key, now, result.Percent, result.Detail, currentGeneration);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                last = new(row.Key, now, null, $"Windows interface query HRESULT 0x{ex.HResult:X8}", currentGeneration);
            }
            Volatile.Write(ref lastOnline, $"{now:O} · 3537:10A9 · {last.Detail}");
        }
        if (last.Percent is int percent)
            rows[index] = row with { Level = percent, Status = "已连接（Windows 设备电量）", Updated = last.Time };
    }

    private static bool IsFastController(TaskbarBattery row) =>
        row.IsConnected && ControllerIdentity.IsG7FastReceiver(row.HardwareVendorId,row.HardwareProductId);

    private static async Task<(int? Percent, string Detail)> InspectAsync(CancellationToken token)
    {
        string selector = HidDevice.GetDeviceSelector(0xFFF0, 0x0040, 0x3537, 0x10A9);
        var interfaces = await DeviceInformation.FindAllAsync(selector, Properties).AsTask(token).ConfigureAwait(false);
        var ids = interfaces.Select(i => i.Properties.GetValueOrDefault(Container)).Distinct().ToArray();
        if (ids.Length != 1 || ids[0] is not Guid container || container == Guid.Empty ||
            container == new Guid("00000000-0000-0000-ffff-ffffffffffff"))
            return (null, $"FFF0:0040 interfaces={interfaces.Count}; no unique device container");
        var levels = new HashSet<int>();
        void Collect(DeviceInformation device) {
            if (PublishedPercent(device.Properties.GetValueOrDefault(Battery)) is int level) levels.Add(level);
        }
        foreach (var device in interfaces) Collect(device);
        string query = $"System.Devices.ContainerId:=\"{container:B}\" AND System.Devices.Present:=System.StructuredQueryType.Boolean#True";
        var nodes = await DeviceInformation.FindAllAsync(query, Properties, DeviceInformationKind.Device)
            .AsTask(token).ConfigureAwait(false);
        foreach (var node in nodes)
            if (node.Id.Contains("VID_3537&PID_10A9", StringComparison.OrdinalIgnoreCase)) Collect(node);
        var root = await DeviceInformation.CreateFromIdAsync(container.ToString("B"), Properties, DeviceInformationKind.DeviceContainer)
            .AsTask(token).ConfigureAwait(false);
        if (root != null) Collect(root);
        int? result = levels.Count == 1 ? levels.First() : null;
        string value = result?.ToString() + (result.HasValue ? "%" : levels.Count == 0 ? "no battery property" : "conflicting battery properties");
        return (result, $"FFF0:0040 present; {nodes.Count} present device nodes; {value}");
    }
}
