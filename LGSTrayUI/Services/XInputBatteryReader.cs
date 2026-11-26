using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace LGSTrayUI;

// XInput exposes four battery bands, not an exact percentage or controller model.
internal static class XInputBatteryReader
{
    private static string lastOnline = "No XInput battery response";
    internal static string LastOnline => Volatile.Read(ref lastOnline);
    [StructLayout(LayoutKind.Sequential)]
    private struct GamepadState
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInfo { public byte Type, Level; }
    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint index, out GamepadState state);
    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetBatteryInformation(uint index, byte deviceType, out BatteryInfo battery);

    public static IEnumerable<TaskbarBattery> Read()
    {
        var result = new List<TaskbarBattery>();
        var observed = new List<string>();
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out _) != 0) continue;
                string label = "—", status = "已连接（电量不可用）";
                if (XInputGetBatteryInformation(i, 0, out var battery) == 0)
                {
                    observed.Add($"slot {i} type={battery.Type} band={battery.Level}" +
                        (battery.Type == 1 ? " (wired/receiver; band ignored)" : ""));
                    if (battery.Type == 1) { label = "USB"; status = "有线或接收器模式（未提供电量）"; }
                    else if (battery.Type is 2 or 3)
                    {
                        label = battery.Level switch { 0 => "Empty", 1 => "Low", 2 => "Med", 3 => "Full", _ => "—" };
                        status = "已连接（XInput 电量档位）";
                    }
                }
                result.Add(new(DeviceType.Gamepad, $"XInput Controller {i + 1}", null, status, false, DateTimeOffset.UtcNow, $"xinput:{i}", label));
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        if (observed.Count > 0) Volatile.Write(ref lastOnline, $"{DateTimeOffset.UtcNow:O} · {string.Join(", ",observed)}");
        return result;
    }
}
