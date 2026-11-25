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

    // Position the WPF strip only in verified free space, including a native reservation.
    internal static bool TryReadCustom(out TaskbarPlacement placement,double logicalWidth)
    {
        placement=default; if(logicalWidth<=0){return false;}
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
        int start=Math.Max(bounds.Left,info.Monitor.Left)+(int)Math.Round(48*scale), end=Math.Min(notify.Left,bounds.Right);
        var occupied=new List<(int left,int right)>();
        var nonStart=new List<(int left,int right)>();
        int? startButtonRight=null, startButtonLeft=null;
        var slot=NativeTaskbarSlot.Instance;
        if(!TaskbarAutomationProbe.TryRead(bar,bounds,c.Left?slot.GeometryNotBefore:0,out var controls)){
            if(c.Left){slot.Verify(false);slot.ReportPlacement("实际位置：等待新鲜的任务栏控件几何数据");}
            return false;
        }
        foreach(var item in controls){
            var r=item.Rect;if(r.Top>=y+height||r.Bottom<=y)continue;
            bool isStart=item.IsStart;
            if(isStart && r.Left>=bounds.Left && r.Right<=end){startButtonRight=(int)Math.Ceiling(r.Right);startButtonLeft=(int)Math.Floor(r.Left);}
            if(r.Left<end&&r.Right>bounds.Left){var interval=((int)Math.Floor(r.Left),(int)Math.Ceiling(r.Right));occupied.Add(interval);if(!isStart)nonStart.Add(interval);}
        }
        int leftGap=(int)Math.Round(Math.Clamp(c.LeftGap,0,64)*scale),rightGap=(int)Math.Round(Math.Clamp(c.RightGap,0,64)*scale);
        // Prefer the exact Start-adjacent slot if it is already free. Never claim a reserved slot that Explorer did not allocate.
        var reserve=TaskbarLayoutGeometry.Dimensions(logicalWidth,c.LeftGap,c.RightGap,c.X);
        if(c.Left && (!c.RequireNativeLeft||NativeTaskbarSlot.Instance.Acknowledged) && startButtonRight is int anchor && startButtonLeft is int leading &&
           leading<=bounds.Left+(int)Math.Ceiling(24*scale) &&
           !nonStart.Any(b=>b.right>leading&&b.left<anchor) && reserve.total>0 &&
           TryReserved(anchor,end,width,(int)Math.Round(reserve.before*scale),(int)Math.Round(reserve.after*scale),occupied,out int nativeX)){
            NativeTaskbarSlot.Instance.Verify(true);
            NativeTaskbarSlot.Instance.ReportPlacement(NativeTaskbarSlot.Instance.Acknowledged?"实际位置：原生占位已验证 · 开始按钮右侧":"实际位置：现有开始按钮右侧空位（非原生占位）");
            placement=new(nativeX,y,width,height,scale);return true;
        }
        // Prefer a real, independently verified left-edge gap even when the native
        // reservation is unavailable. Never claim the Start button has been moved.
        if(c.Left){
            NativeTaskbarSlot.Instance.Verify(false);
            if(c.RequireNativeLeft){NativeTaskbarSlot.Instance.ReportPlacement("实际位置：等待验证的原生占位；未启用悬浮回退");return false;}
            if(startButtonLeft is int sl && startButtonRight is int sr &&
               TryLeftFallback(Math.Max(bounds.Left,info.Monitor.Left),end,sl,sr,(int)Math.Ceiling(24*scale),width,leftGap,rightGap,offset,occupied,out int lx)){
                NativeTaskbarSlot.Instance.ReportPlacement("实际位置：左侧安全空位（非原生占位）");
                placement=new(lx,y,width,height,scale);return true;
            }
            // Some Windows versions expose a different Start automation identifier.
            // Only use a left-side gap when actual controls were observed; reserve the
            // first conventional Start-width area if the Start button is unidentified.
            int edge=Math.Max(bounds.Left,info.Monitor.Left);
            int leftLimit=Math.Min(end,edge+(bounds.Right-edge)/2);
            if(occupied.Count>0&&TryGap(startButtonLeft.HasValue?edge:start,leftLimit,width,leftGap,rightGap,offset,true,occupied,out int safeLeft)){
                NativeTaskbarSlot.Instance.ReportPlacement("实际位置：左半区域安全空位（非原生占位）");
                placement=new(safeLeft,y,width,height,scale);return true;
            }
        }
        if(!TryGap(start,end,width,leftGap,rightGap,offset,false,occupied,out int x)){
            if(c.Left)NativeTaskbarSlot.Instance.ReportPlacement("实际位置：无安全空位，暂时隐藏");
            return false;
        }
        if(c.Left)NativeTaskbarSlot.Instance.ReportPlacement("实际位置：右侧回退（左侧空间不足）");
        placement=new(x,y,width,height,scale);return true;
    }
    internal static bool TryLeftFallback(int edge,int end,int startLeft,int startRight,int edgeTolerance,int width,int leftGap,int rightGap,int offset,IEnumerable<(int left,int right)> occupied,out int x){
        x=0;
        if(startLeft<edge||startRight<=startLeft||startRight>end)return false;
        var intervals=occupied.ToArray();
        // Centered Start: use the leading free space, never overlap the centered buttons.
        if(startLeft>edge+edgeTolerance)
            return TryGap(edge,startLeft,width,leftGap,rightGap,offset,true,intervals,out x);
        // Left-aligned Start: only use the adjacent free slot, not a remote right-hand gap.
        int next=intervals.Where(i=>i.right>startRight).Select(i=>Math.Max(startRight,i.left)).DefaultIfEmpty(end).Min();
        return TryGap(startRight,Math.Min(end,next),width,leftGap,rightGap,offset,true,intervals,out x);
    }
    internal static bool TryReserved(int anchor,int end,int width,int before,int after,IEnumerable<(int left,int right)> occupied,out int x){
        x=0;if(width<=0||before<0||after<0)return false;
        long l=(long)anchor+before,r=l+width;
        if(r+after>end||l<int.MinValue||r>int.MaxValue)return false;
        // Verify both the ring strip and its requested trailing padding, excluding the Start button itself.
        if(occupied.Any(b=>b.right>anchor&&b.left<r+after))return false;
        x=(int)l;return true;
    }
    // Find the first/last safe gap. This cannot reserve space in Explorer.
    internal static bool TryGap(int start,int end,int width,int leftGap,int rightGap,int offset,bool left,IEnumerable<(int left,int right)> occupied,out int x){
        x=0;var gaps=new List<(int l,int r)>();int cursor=start;
        foreach(var item in occupied.OrderBy(r=>r.left)){int l=Math.Max(start,item.left),r=Math.Min(end,item.right);if(r<=cursor||l>=end)continue;if(l>cursor)gaps.Add((cursor,l));cursor=Math.Max(cursor,r);}
        if(cursor<end)gaps.Add((cursor,end));
        foreach(var g in left?gaps.AsEnumerable():gaps.AsEnumerable().Reverse()){int lo=g.l+leftGap,hi=g.r-rightGap-width;if(lo>hi)continue;x=Math.Clamp((left?lo:hi)+offset,lo,hi);return true;}return false;
    }
    internal static bool TryFit(PixelRect bar, PixelRect tray, PixelRect screen, double scale,
                                out TaskbarPlacement placement, int count = 2)
    {
        placement = default;
        if (scale <= 0 || tray.Right <= tray.Left || tray.Bottom <= tray.Top) return false;
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
