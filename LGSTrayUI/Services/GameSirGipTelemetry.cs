using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Gaming.Input;
using Windows.Gaming.Input.Custom;
using WinRT;

namespace LGSTrayUI;

// Windows routes messages for the matched physical GIP provider to this sink.
// The 10A9 XUSB identity has a different transport and is never registered.
internal sealed class GameSirGipTelemetry : ICustomGameControllerFactory
{
    internal readonly record struct Sample(int? Percent, string? Band, bool Charging, DateTimeOffset Time);
    internal static readonly GameSirGipTelemetry Instance = new();
    private readonly Dictionary<ushort, string> registrations = new();
    private readonly List<WeakReference<InputSink>> sinks = new();
    private volatile bool enabled;
    internal void Enable(bool value)
    {
        if (enabled == value) return;
        enabled = value;
        if (!value) lock (sinks)
            foreach (var reference in sinks) if (reference.TryGetTarget(out var sink)) sink.OnInputSuspended(0);
    }

    internal static Sample? Decode(byte message, ReadOnlySpan<byte> payload, DateTimeOffset now)
    {
        // Windows has already removed the four-byte GIP header (10 xx xx 3C).
        if (message == 0x10 && payload.Length == 60 && payload[0] == 0xE0 &&
            payload[28] <= 1 && payload[29] <= 100)
            return new(payload[29], null, payload[28] == 1, now);
        if (message != 3 || payload.Length is not (4 or >= 0x23 and <= 0x37)) return null;
        byte state = payload[0];
        int batteryType = (state >> 2) & 3;
        if ((state & 0x80) == 0 || batteryType is not (1 or 2)) return null;
        string band = (state & 3) switch { 0 => "1/4", 1 => "2/4", 2 => "3/4", _ => "4/4" };
        return new(null, band, false, now);
    }

    internal Sample? Read(RawGameController raw, out string detail)
    {
        var pid = raw.HardwareProductId;
        if (!ControllerIdentity.IsG7(raw.HardwareVendorId, pid) || pid == 0x10A9) {
            detail = "XUSB fast mode"; return null;
        }
        // Register only on demand and once per product. Registration can call
        // CreateGameController synchronously, so no sink lock spans this call.
        if (!registrations.TryGetValue(pid, out var registration)) {
            try {
                GameControllerFactoryManager.RegisterCustomFactoryForHardwareId(this, raw.HardwareVendorId, pid);
                registration = "registered";
            } catch (Exception ex) { registration = $"registration HRESULT 0x{ex.HResult:X8}"; }
            registrations.Add(pid, registration);
        }
        detail = registration;
        if (registration != "registered") return null;
        try {
            var controller = GameControllerFactoryManager.TryGetFactoryControllerFromGameController(this, raw);
            if (controller == null) { detail = "no native GIP projection"; return null; }
            var target = controller.As<IGipGameControllerInputSink>();
            var pointer = MarshalInterface<IGipGameControllerInputSink>.FromManaged(target);
            try {
                lock (sinks) {
                    for (int i = sinks.Count - 1; i >= 0; i--) {
                        if (!sinks[i].TryGetTarget(out var sink)) { sinks.RemoveAt(i); continue; }
                        // Match the exact sink interface of the WGI controller.
                        // A product ID alone cannot distinguish two identical pads.
                        var candidate = MarshalInterface<IGipGameControllerInputSink>.FromManaged(sink);
                        try { if (candidate == pointer) return sink.Latest(out detail); }
                        finally { Marshal.Release(candidate); }
                    }
                }
            } finally { Marshal.Release(pointer); }
            detail = "no matching native sink";
        } catch (Exception ex) { detail = $"native projection HRESULT 0x{ex.HResult:X8}"; }
        return null;
    }

    public object CreateGameController(IGameControllerProvider provider)
    {
        var sink = new InputSink(this,provider is GipGameControllerProvider);
        lock (sinks) {
            sinks.RemoveAll(reference => !reference.TryGetTarget(out _));
            sinks.Add(new(sink));
        }
        return sink;
    }
    public void OnGameControllerAdded(IGameController value) { }
    public void OnGameControllerRemoved(IGameController value) { }

    private sealed class InputSink(GameSirGipTelemetry owner, bool isGip) : IGipGameControllerInputSink
    {
        private Sample? exact, band;
        public void OnMessageReceived(ulong timestamp, GipMessageClass messageClass, byte messageId, byte sequenceId, byte[] messageBuffer)
        {
            if (!owner.enabled || !isGip || messageId is not (3 or 0x10) || (messageId == 3 && messageClass != GipMessageClass.Command)) return;
            lock (this) {
                if (!owner.enabled) return;
                var value = Decode(messageId, messageBuffer, DateTimeOffset.UtcNow);
                if (messageId == 3) {
                    band = value;
                    if (messageBuffer.Length > 0 && (messageBuffer[0] & 0x80) == 0) exact = null;
                } else if (value.HasValue) exact = value;
            }
        }
        internal Sample? Latest(out string detail)
        {
            lock (this) {
                var cutoff = DateTimeOffset.UtcNow.AddSeconds(-35);
                var sample = exact?.Time >= cutoff ? exact : band?.Time >= cutoff ? band : null;
                detail = sample?.Percent != null ? "native vendor percentage" : sample != null ? "native status band" : "waiting for a battery message";
                return sample;
            }
        }
        public void OnKeyReceived(ulong timestamp, byte keyCode, bool isPressed) { }
        public void OnInputResumed(ulong timestamp) { }
        public void OnInputSuspended(ulong timestamp) { lock (this) { exact = null; band = null; } }
    }
}
