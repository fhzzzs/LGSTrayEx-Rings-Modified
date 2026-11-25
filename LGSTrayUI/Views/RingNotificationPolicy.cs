using System;
using System.Runtime.InteropServices;

namespace LGSTrayUI;

internal static class RingNotificationPolicy
{
    internal static bool CanNotify(RingPreferences c)=>!c.QuietNotifications&&(!c.SuppressFullScreen||!ForegroundIsFullScreen());
    internal static bool ShouldNotifyLow((int threshold,DateTimeOffset time)? previous,int threshold,int repeatMinutes,DateTimeOffset now)
        =>previous is not { } old||threshold<old.threshold||(repeatMinutes>0&&now-old.time>=TimeSpan.FromMinutes(repeatMinutes));
    private static bool ForegroundIsFullScreen(){
        try{
            var front=GetForegroundWindow();if(front==0||front==GetShellWindow()||front==GetDesktopWindow())return false;
            if(!GetClientRect(front,out var client))return false;
            var origin=new PointI();if(!ClientToScreen(front,ref origin))return false;
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            return GetMonitorInfo(MonitorFromWindow(front,2),ref info)&&origin.X<=info.Monitor.Left&&origin.Y<=info.Monitor.Top&&
                origin.X+client.Right>=info.Monitor.Right&&origin.Y+client.Bottom>=info.Monitor.Bottom;
        }catch{return false;}
    }
    [StructLayout(LayoutKind.Sequential)]private struct PointI{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct RectI{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public RectI Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern nint GetShellWindow();
    [DllImport("user32.dll")]private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")]private static extern bool GetClientRect(nint h,out RectI r);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(nint h,ref PointI p);
    [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint h,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")]private static extern bool GetMonitorInfo(nint m,ref MonitorInfo i);
}
