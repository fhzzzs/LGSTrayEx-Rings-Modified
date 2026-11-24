using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace LGSTrayUI;

// Standard BLE Battery Service (0x180F). No vendor commands or writes.
// A paired device without a usable battery characteristic is shown as unknown, never 0%.
internal static class BluetoothBatteryReader
{
    internal static DeviceType? Classify(string name, ushort category, ushort subcategory)
    {
        if (category == 15)
        {
            if (subcategory == 1) return DeviceType.Keyboard;
            if (subcategory == 2) return DeviceType.Mouse;
            if (subcategory is 3 or 4) return DeviceType.Gamepad;
        }
        string n = name.ToLowerInvariant();
        if (n.Contains("gamesir") || n.Contains("gamepad") || n.Contains("controller") || n.Contains("dualshock") || n.Contains("dualsense")) return DeviceType.Gamepad;
        if (n.Contains("g735")) return DeviceType.Headset;
        if (n.Contains("harpe ace mini")) return DeviceType.Mouse;
        if (n.Contains("keyboard")) return DeviceType.Keyboard;
        if (n.Contains("mouse")) return DeviceType.Mouse;
        if (n.Contains("headset") || n.Contains("headphone") || n.Contains("earbud") || n.Contains("airpods")) return DeviceType.Headset;
        return null;
    }

    public static async Task<IReadOnlyList<TaskbarBattery>> ReadAsync(CancellationToken cancellationToken)
    {
        var result = new List<TaskbarBattery>();
        var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(cancellationToken);
        foreach (var info in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
                if (device == null) continue;
                var type = Classify(info.Name, device.Appearance.Category, device.Appearance.SubCategory);
                if (type == null) continue;
                string key = "ble:" + info.Id;
                string? physical = device.BluetoothAddress==0?null:$"ble-address:{device.BluetoothAddress:X12}";
                if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    result.Add(new(type.Value, info.Name, null, "离线", false, DateTimeOffset.MinValue, key, PhysicalId:physical));
                    continue;
                }
                int? level = null;
                var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(cancellationToken);
                try
                {
                    if (services.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var service in services.Services)
                        {
                            var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(cancellationToken);
                            if (chars.Status != GattCommunicationStatus.Success) continue;
                            foreach (var characteristic in chars.Characteristics)
                            {
                                var reading = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                                if (reading.Status != GattCommunicationStatus.Success || reading.Value.Length == 0) continue;
                                using var reader = DataReader.FromBuffer(reading.Value);
                                byte value = reader.ReadByte();
                                if (value <= 100) { level = value; break; }
                            }
                            if (level.HasValue) break;
                        }
                    }
                }
                finally { foreach (var service in services.Services) service.Dispose(); }
                bool online = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                result.Add(new(type.Value, info.Name, online ? level : null, !online ? "离线" : level.HasValue ? "已连接" : "等待读数", false,
                    level.HasValue && online ? DateTimeOffset.Now : DateTimeOffset.MinValue, key, PhysicalId:physical));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { DiagnosticLogger.LogWarning($"BLE battery: {info.Name}: {ex.Message}"); }
        }
        return result;
    }
}
