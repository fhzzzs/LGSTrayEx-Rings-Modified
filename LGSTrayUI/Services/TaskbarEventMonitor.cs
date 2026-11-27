using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LGSTrayUI;

// WinEvent hooks observe shell changes without modifying Explorer. Keep a
// bounded fallback because not every shell layout change raises an event.
internal sealed class TaskbarEventMonitor:IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Action changed;
    private readonly WinEvent callback;
    private readonly nint location,foreground;
    private readonly DispatcherTimer layoutTimer;
    private bool disposed,foregroundQueued;
    internal const int LayoutCoalesceMs=120;
    internal TaskbarEventMonitor(Dispatcher dispatcher,Action changed)
    {
        this.dispatcher=dispatcher;this.changed=changed;callback=OnEvent;
        layoutTimer=new DispatcherTimer(DispatcherPriority.Background,dispatcher){Interval=TimeSpan.FromMilliseconds(LayoutCoalesceMs)};
        layoutTimer.Tick+=LayoutTick;
        location=SetWinEventHook(0x800B,0x800B,0,callback,0,0,2);
        foreground=SetWinEventHook(3,3,0,callback,0,0,2);
    }
    private void OnEvent(nint hook,uint evt,nint hwnd,int obj,int child,uint thread,uint time)
    {
        if(disposed||hwnd==0||dispatcher.HasShutdownStarted)return;
        if(evt==3){
            // Foreground changes can enter/leave fullscreen. Check promptly,
            // but do not invalidate the taskbar's geometry just for focus.
            if(foregroundQueued)return;
            foregroundQueued=true;
            dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{
                foregroundQueued=false;if(!disposed)changed();
            }));
            return;
        }
        var bar=FindWindow("Shell_TrayWnd",null);
        if(bar==0||GetAncestor(hwnd,2)!=bar&&hwnd!=bar)return;
        // Fixed coalescing window (not a sliding debounce): sustained events
        // still get serviced, while secondary-taskbar events are ignored.
        if(!layoutTimer.IsEnabled)layoutTimer.Start();
    }
    private void LayoutTick(object? sender,EventArgs e){
        layoutTimer.Stop();
        if(disposed)return;
        TaskbarAutomationProbe.RequestRefresh();
        changed();
    }
    public void Dispose(){if(disposed)return;disposed=true;layoutTimer.Stop();layoutTimer.Tick-=LayoutTick;if(location!=0)UnhookWinEvent(location);if(foreground!=0)UnhookWinEvent(foreground);}
    private delegate void WinEvent(nint hook,uint evt,nint hwnd,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")]private static extern nint SetWinEventHook(uint min,uint max,nint module,WinEvent proc,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string cls,string? title);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint hwnd,uint flags);
}
