using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LGSTrayUI;

// HIDAPI 0.15 on Windows shares its initialization and error state across all
// device paths. Every discovery and handle lifetime call in the UI uses Shared.
// Report I/O on a handle owned by one scan can proceed outside this lock.
internal sealed class HidDiscovery(
    Func<int> initialize,
    Func<ushort, ushort, nint> enumerate,
    Action<nint> freeEnumeration,
    Func<string, nint> openPath,
    Action<nint> close)
{
    internal static HidDiscovery Shared { get; } = new(
        hid_init, hid_enumerate, hid_free_enumeration, hid_open_path, hid_close);

    private readonly Lock gate = new();
    private bool initialized;

    // The caller holds gate. A failed initialization can be retried next scan.
    private bool EnsureInitialized()
    {
        if (!initialized)
            initialized = initialize() == 0;
        return initialized;
    }

    internal nint Enumerate(ushort vendorId, ushort productId)
    {
        lock (gate)
            return EnsureInitialized() ? enumerate(vendorId, productId) : 0;
    }

    internal nint OpenPath(string path)
    {
        lock (gate)
            return EnsureInitialized() ? openPath(path) : 0;
    }

    internal void FreeEnumeration(nint list)
    {
        lock (gate)
            freeEnumeration(list);
    }

    internal void Close(nint handle)
    {
        lock (gate)
            close(handle);
    }

    [DllImport("hidapi", CallingConvention = CallingConvention.Cdecl)]
    private static extern int hid_init();
    [DllImport("hidapi", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint hid_enumerate(ushort vendorId, ushort productId);
    [DllImport("hidapi", CallingConvention = CallingConvention.Cdecl)]
    private static extern void hid_free_enumeration(nint list);
    [DllImport("hidapi", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint hid_open_path([MarshalAs(UnmanagedType.LPStr)] string path);
    [DllImport("hidapi", CallingConvention = CallingConvention.Cdecl)]
    private static extern void hid_close(nint handle);
}
