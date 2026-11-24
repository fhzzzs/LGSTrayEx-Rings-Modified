using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LGSTrayUI;

// All rectangles are physical screen pixels. Never use the virtual desktop's
// right edge or mix primary-monitor DIPs with another monitor's coordinates.
internal static class NotificationPlacement
{
    internal static Rect[] Stack(Rect work,double scale,int count)
    {
        if(work.IsEmpty||work.Width<=0||work.Height<=0||!double.IsFinite(scale)||scale<=0||count<=0)return [];
        double margin=Math.Min(Math.Ceiling(12*scale),Math.Min(work.Width,work.Height)/8);
        double gap=Math.Ceiling(8*scale);
        double width=Math.Max(1,Math.Min(Math.Ceiling(360*scale),work.Width-2*margin));
        double height=Math.Max(1,Math.Min(Math.Ceiling(170*scale),work.Height-2*margin));
        int capacity=Math.Max(1,(int)Math.Floor((work.Height-2*margin+gap)/(height+gap)));
        return Enumerable.Range(0,Math.Min(count,capacity))
            .Select(i=>new Rect(work.Right-margin-width,work.Bottom-margin-height-i*(height+gap),width,height)).ToArray();
    }
}

internal static class PrimaryNotificationPopup
{
    private sealed record Entry(Window Window,nint Handle);
    private static readonly List<Entry> Active=[];
    private static bool arranging;
    private static DispatcherTimer? monitorTimer;
    internal static void Show(string title,string message,string color,TimeSpan expiration,string? actionText=null,Action? action=null)
    {
        var dispatcher=Application.Current?.Dispatcher;
        if(dispatcher==null||dispatcher.HasShutdownStarted)return;
        if(!dispatcher.CheckAccess()){dispatcher.BeginInvoke(new Action(()=>Show(title,message,color,expiration,actionText,action)));return;}
        var w=new Window{
            Title="Battery Monitor",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Topmost=true,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,
            Width=360,Height=170,Background=new SolidColorBrush(RingPreferences.Ink(color)),Foreground=Brushes.White
        };
        var root=new DockPanel{Margin=new Thickness(12)};
        var close=new Button{Content="×",Width=28,Height=26,Padding=new Thickness(0),Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Top};
        close.Click+=(_,_)=>w.Close();DockPanel.SetDock(close,Dock.Right);root.Children.Add(close);
        var content=new StackPanel();
        content.Children.Add(new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,FontSize=15,FontWeight=FontWeights.SemiBold});
        content.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,0),FontSize=13});
        if(actionText!=null){
            var button=new Button{Content=actionText,Margin=new Thickness(0,9,0,0),HorizontalAlignment=HorizontalAlignment.Left};
            button.Click+=(_,_)=>{action?.Invoke();w.Close();};content.Children.Add(button);
        }
        root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        w.Content=root;
        var handle=new WindowInteropHelper(w).EnsureHandle();
        TaskbarPlacement.MakeToolWindow(handle);
        var entry=new Entry(w,handle);
        DispatcherTimer? expiry=null;
        w.Closed+=(_,_)=>{
            expiry?.Stop();Active.Remove(entry);
            if(Active.Count==0)monitorTimer?.Stop();
            if(!arranging)Arrange();
        };
        Active.Add(entry);
        // Set the physical monitor position before showing; repeat after WPF's initial layout.
        Arrange();
        if(!Active.Contains(entry))return;
        w.Show();
        Arrange();
        monitorTimer??=CreateMonitorTimer();
        monitorTimer.Start();
        if(expiration!=TimeSpan.MaxValue&&expiration>TimeSpan.Zero){
            expiry=new DispatcherTimer{Interval=expiration};expiry.Tick+=(_,_)=>{expiry.Stop();if(Active.Contains(entry))w.Close();};expiry.Start();
        }
    }
    private static DispatcherTimer CreateMonitorTimer(){
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
        timer.Tick+=(_,_)=>Arrange();return timer;
    }
    private static void Arrange(){
        if(arranging||Active.Count==0)return;
        arranging=true;
        try{
            var monitor=MonitorFromPoint(new PointI(),1); // MONITOR_DEFAULTTOPRIMARY
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(monitor==0||!GetMonitorInfo(monitor,ref info))return;
            double scale=GetDpiForSystem()/96d;if(scale<=0)scale=1;
            var work=new Rect(info.Work.Left,info.Work.Top,Math.Max(0,info.Work.Right-info.Work.Left),Math.Max(0,info.Work.Bottom-info.Work.Top));
            var positions=NotificationPlacement.Stack(work,scale,Active.Count);
            if(positions.Length==0)return;
            while(Active.Count>positions.Length)Active[0].Window.Close();
            for(int i=0;i<Active.Count;i++){
                var entry=Active[Active.Count-1-i];var r=positions[i];
                int x=(int)Math.Floor(r.X),y=(int)Math.Floor(r.Y),width=Math.Max(1,(int)Math.Floor(r.Width)),height=Math.Max(1,(int)Math.Floor(r.Height));
                if(GetWindowRect(entry.Handle,out var old)&&old.Left==x&&old.Top==y&&old.Right==x+width&&old.Bottom==y+height)continue;
                SetWindowPos(entry.Handle,new nint(-1),x,y,width,height,0x0010|0x0200);
            }
        }finally{arranging=false;}
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointI{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct RectI{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo{public int Size;public RectI Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern nint MonitorFromPoint(PointI point,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")]private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out RectI rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
}
