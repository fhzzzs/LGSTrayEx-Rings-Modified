using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

/// <summary>All coordinates here are physical pixels on the primary taskbar's monitor.</summary>
internal readonly record struct TaskbarPlacement(int X, int Y, int Width, int Height, double Scale)
{
    public double LogicalWidth => Width / Scale;
    public double LogicalHeight => Height / Scale;

    internal static bool TryRead(out TaskbarPlacement placement, int count = 2)
    {
        placement = default;
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == 0 || !IsWindowVisible(bar) || !GetWindowRect(bar, out var bounds)) return false;
        var tray = FindWindowEx(bar, 0, "TrayNotifyWnd", null);
        if (tray == 0 || !GetWindowRect(tray, out var notification)) return false;
        var monitor = MonitorFromWindow(bar, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || ForegroundCoversMonitor(bar, monitor, info.Monitor)) return false;

        double scale = GetDpiForWindow(bar) / 96.0;
        return TryFit(bounds, notification, info.Monitor, scale, out placement, count);
    }

    // Position the WPF strip in verified free space adjacent to the notification area.
    internal static bool TryReadCustom(out TaskbarPlacement placement,double logicalWidth)
    {
        placement=default; if(!double.IsFinite(logicalWidth)||logicalWidth<=0){return false;}
        var c=RingConfig.Current;
        var bar=FindWindow("Shell_TrayWnd",null); if(bar==0||!IsWindowVisible(bar)||!GetWindowRect(bar,out var bounds)){return false;}
        var tray=FindWindowEx(bar,0,"TrayNotifyWnd",null); if(tray==0||!GetWindowRect(tray,out var notify)){return false;}
        var monitor=MonitorFromWindow(bar,2); var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(monitor,ref info)||ForegroundCoversMonitor(bar,monitor,info.Monitor)){return false;}
        double scale=GetDpiForWindow(bar)/96d;if(scale<=0){return false;}
        int top=Math.Max(bounds.Top,info.Monitor.Top),bottom=Math.Min(bounds.Bottom,info.Monitor.Bottom),h=bottom-top;
        if(h<24*scale||h>bounds.Right-bounds.Left){return false;}
        int height=Math.Min((int)Math.Round(Math.Clamp(c.Size,24,52)*scale),h), width=(int)Math.Ceiling(logicalWidth*scale);
        int offset=(int)Math.Round(Math.Clamp(c.X,-100,100)*scale), y=top+(h-height)/2+(int)Math.Round(Math.Clamp(c.Y,-20,20)*scale);
        if(y<top||y+height>bottom){return false;}
        int start=Math.Max(bounds.Left,info.Monitor.Left)+(int)Math.Round(48*scale), end=Math.Min(notify.Left,Math.Min(bounds.Right,info.Monitor.Right));
        if(!TaskbarAutomationProbe.TryRead(bar,bounds,out var controls))return false;
        var occupied=controls.Where(r=>r.Top<y+height&&r.Bottom>y&&r.Left<end&&r.Right>bounds.Left)
            .Select(r=>((int)Math.Floor(r.Left),(int)Math.Ceiling(r.Right))).ToArray();
        int leftGap=(int)Math.Round(Math.Clamp(c.LeftGap,0,64)*scale),rightGap=(int)Math.Round(Math.Clamp(c.RightGap,0,64)*scale);
        if(!TryGap(start,end,width,leftGap,rightGap,offset,occupied,out int x))return false;
        placement=new(x,y,width,height,scale);return true;
    }
    // Use only the trailing free gap; never jump to the leading side or between apps.
    internal static bool TryGap(int start,int end,int width,int leftGap,int rightGap,int offset,IEnumerable<(int left,int right)> occupied,out int x){
        x=0;if(width<=0||start>=end||leftGap<0||rightGap<0)return false;
        int cursor=start;
        foreach(var item in occupied){if(item.right>start&&item.left<end)cursor=Math.Max(cursor,Math.Min(item.right,end));}
        int lo=cursor+leftGap,hi=end-rightGap-width;
        if(lo>hi)return false;
        x=Math.Clamp(hi+offset,lo,hi);return true;
    }
    internal static bool TryFit(PixelRect bar, PixelRect tray, PixelRect screen, double scale,
                                out TaskbarPlacement placement, int count = 2)
    {
        placement = default;
        if (!double.IsFinite(scale) || scale <= 0 || tray.Right <= tray.Left || tray.Bottom <= tray.Top) return false;
        int top = Math.Max(bar.Top, screen.Top);
        int bottom = Math.Min(bar.Bottom, screen.Bottom);
        int thickness = bottom - top;
        // Auto-hidden taskbars leave only a sliver on screen. Wait until the bar is visible.
        if (thickness < 24 * scale || thickness > bar.Right - bar.Left) return false;
        int height = Math.Min((int)Math.Round(40 * scale), thickness - (int)Math.Round(2 * scale));
        if (count < 1) return false;
        int width = count * height + (count - 1) * (int)Math.Round(6 * scale);
        int x = tray.Left - (int)Math.Round(4 * scale) - width;
        if (x < Math.Max(bar.Left, screen.Left) || tray.Left > Math.Min(bar.Right, screen.Right)) return false;
        placement = new(x, top + (thickness - height) / 2, width, height, scale);
        return true;
    }

    internal void Apply(nint window)
    {
        // The bar can rise above us without moving. Avoid reordering or repainting an
        // already-visible overlay on every timer tick (including while a tooltip is open).
        if (GetWindowRect(window, out var bounds) && bounds.Left == X && bounds.Top == Y
            && bounds.Right == X + Width && bounds.Bottom == Y + Height)
        {
            var insideRing = new PixelPoint { X = X + Height / 2, Y = Y + Height / 2 };
            if (GetAncestor(WindowFromPoint(insideRing), 2) != FindWindow("Shell_TrayWnd", null))
                return;
        }
        SetWindowPos(window, new nint(-1), X, Y, Width, Height, 0x0010 | 0x0200); // NOACTIVATE | NOOWNERZORDER
    }

    internal static void MakeToolWindow(nint window)
    {
        // A non-activating tool window stays out of Alt+Tab and preserves focus in the current app.
        const int styleIndex = -20, toolWindow = 0x80, noActivate = 0x08000000, appWindow = 0x40000;
        int style = GetWindowLong(window, styleIndex);
        SetWindowLong(window, styleIndex, (style | toolWindow | noActivate) & ~appWindow);
    }

    private static bool ForegroundCoversMonitor(nint bar, nint monitor, PixelRect screen)
    {
        var front = GetForegroundWindow();
        if (front == 0 || front == bar || front == GetDesktopWindow() || front == GetShellWindow()
            || MonitorFromWindow(front, 2) != monitor) return false;
        // After "Show desktop", Explorer's WorkerW can be the foreground window.
        if (front == FindWindow("WorkerW", null) || FindWindowEx(front, 0, "SHELLDLL_DefView", null) != 0) return false;
        if (!GetClientRect(front, out var client)) return false;
        var origin = new PixelPoint();
        return ClientToScreen(front, ref origin) && origin.X <= screen.Left && origin.Y <= screen.Top
            && origin.X + client.Right >= screen.Right && origin.Y + client.Bottom >= screen.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PixelRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public PixelRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint previous, string className, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out PixelRect bounds);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out PixelRect bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref PixelPoint point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint order, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowLong(nint window, int index, int value);
}
