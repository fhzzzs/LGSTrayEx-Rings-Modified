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
    private sealed record Observation(string Key, DateTimeOffset Time, Guid? ContainerId, int? Percent, string Detail, int Generation);
    private static Observation? last;
    private static int generation;
    private static string lastOnline = "No 3537:10A9 session observed";
    internal static string LastOnline => Volatile.Read(ref lastOnline);
    internal static void Invalidate() => Interlocked.Increment(ref generation);

    internal static int? PublishedPercent(object? value) => value is byte b && b <= 100 ? b : null;
    internal static TaskbarBattery WithContainer(TaskbarBattery row, Guid? container) =>
        container is Guid id && row.Key.StartsWith(GipBatteryReader.SessionPrefix, StringComparison.Ordinal)
            ? row with { Key = $"gip:container:{id:N}" } : row;

    internal static async Task CheckAsync(List<TaskbarBattery> rows, CancellationToken token)
    {
        int index = rows.FindIndex(IsFastController);
        if (index < 0) { Volatile.Write(ref last, null); return; }
        var row = rows[index];
        if (rows.FindIndex(index + 1, IsFastController) >= 0) {
            Volatile.Write(ref last, null);
            Volatile.Write(ref lastOnline, "Multiple 3537:10A9 controllers; device-property association ambiguous");
            return;
        }
        if (row.Level.HasValue && !row.Key.StartsWith(GipBatteryReader.SessionPrefix, StringComparison.Ordinal)) {
            Volatile.Write(ref lastOnline, "3537:10A9 Windows gaming battery report available");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        int currentGeneration = Volatile.Read(ref generation);
        var cached = Volatile.Read(ref last);
        if (cached == null || cached.Key != row.Key || cached.Generation != currentGeneration || now - cached.Time >= Interval) {
            var previousId = cached?.Key == row.Key && cached.Generation == currentGeneration ? cached.ContainerId : null;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(1.2));
            try {
                var result = await InspectAsync(budget.Token).ConfigureAwait(false);
                cached = new(row.Key, now, result.ContainerId, result.Percent, result.Detail, currentGeneration);
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                cached = new(row.Key, now, previousId, null, "Windows device-property query timed out", currentGeneration);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                cached = new(row.Key, now, previousId, null, $"Windows interface query HRESULT 0x{ex.HResult:X8}", currentGeneration);
            }
            token.ThrowIfCancellationRequested();
            if (currentGeneration != Volatile.Read(ref generation)) return;
            Volatile.Write(ref last, cached);
            Volatile.Write(ref lastOnline, $"{now:O} · 3537:10A9 · {cached.Detail}");
        }
        row = WithContainer(row, cached.ContainerId);
        if (!row.Level.HasValue && cached.Percent is int percent)
            row = row with { Level = percent, Status = "已连接（Windows 设备电量）", Updated = cached.Time };
        rows[index] = row;
    }

    private static bool IsFastController(TaskbarBattery row) =>
        row.IsConnected && ControllerIdentity.IsG7FastReceiver(row.HardwareVendorId,row.HardwareProductId);

    private static async Task<(Guid? ContainerId, int? Percent, string Detail)> InspectAsync(CancellationToken token)
    {
        string selector = HidDevice.GetDeviceSelector(0xFFF0, 0x0040, 0x3537, 0x10A9);
        var interfaces = await DeviceInformation.FindAllAsync(selector, Properties).AsTask(token).ConfigureAwait(false);
        var ids = interfaces.Select(i => i.Properties.GetValueOrDefault(Container)).Distinct().ToArray();
        if (ids.Length != 1 || ids[0] is not Guid container || container == Guid.Empty ||
            container == new Guid("00000000-0000-0000-ffff-ffffffffffff"))
            return (null, null, $"FFF0:0040 interfaces={interfaces.Count}; no unique device container");
        var levels = new HashSet<int>();
        void Collect(DeviceInformation device) {
            if (PublishedPercent(device.Properties.GetValueOrDefault(Battery)) is int level) levels.Add(level);
        }
        foreach (var device in interfaces) Collect(device);
        string query = $"System.Devices.ContainerId:=\"{container:B}\" AND System.Devices.Present:=System.StructuredQueryType.Boolean#True";
        var details = new List<string> { "FFF0:0040 present" };
        try {
            var nodes = await DeviceInformation.FindAllAsync(query, Properties, DeviceInformationKind.Device)
                .AsTask(token).ConfigureAwait(false);
            foreach (var node in nodes)
                if (node.Id.Contains("VID_3537&PID_10A9", StringComparison.OrdinalIgnoreCase)) Collect(node);
            details.Add($"{nodes.Count} present device nodes");
        } catch (Exception ex) when (ex is not OperationCanceledException) { details.Add($"device nodes HRESULT 0x{ex.HResult:X8}"); }
        try {
            var root = await DeviceInformation.CreateFromIdAsync(container.ToString("B"), Properties, DeviceInformationKind.DeviceContainer)
                .AsTask(token).ConfigureAwait(false);
            if (root != null) Collect(root);
        } catch (Exception ex) when (ex is not OperationCanceledException) { details.Add($"device container HRESULT 0x{ex.HResult:X8}"); }
        int? result = levels.Count == 1 ? levels.First() : null;
        string value = result?.ToString() + (result.HasValue ? "%" : levels.Count == 0 ? "no battery property" : "conflicting battery properties");
        details.Add(value);
        return (container, result, string.Join("; ", details));
    }
}
