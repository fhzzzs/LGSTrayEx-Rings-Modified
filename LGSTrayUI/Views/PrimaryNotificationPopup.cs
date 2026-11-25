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
    internal const double Width=300,CompactHeight=76,ActionHeight=114;
    internal const int MaxVisible=3;
    internal static Rect[] Stack(Rect work,double scale,int count)=>Stack(work,scale,Enumerable.Repeat(CompactHeight,Math.Clamp(count,0,MaxVisible)).ToArray());
    internal static Rect[] Stack(Rect work,double scale,IReadOnlyList<double> heights,double widthDip=Width,string corner="BottomRight")
    {
        if(work.IsEmpty||work.Width<=0||work.Height<=0||!double.IsFinite(scale)||scale<=0||heights.Count==0)return [];
        double margin=Math.Min(Math.Ceiling(12*scale),Math.Min(work.Width,work.Height)/8);
        double gap=Math.Ceiling(8*scale);
        double width=Math.Max(1,Math.Min(Math.Ceiling(Math.Clamp(widthDip,240,420)*scale),work.Width-2*margin));
        bool top=corner.StartsWith("Top",StringComparison.Ordinal),left=corner.EndsWith("Left",StringComparison.Ordinal);
        double cursor=top?work.Top+margin:work.Bottom-margin;var result=new List<Rect>();
        foreach(double logical in heights.Take(MaxVisible)){
            double height=Math.Max(1,Math.Min(Math.Ceiling(logical*scale),work.Height-2*margin));
            double y=top?cursor:cursor-height;
            if(y<work.Top+margin||y+height>work.Bottom-margin)break;
            result.Add(new Rect(left?work.Left+margin:work.Right-margin-width,y,width,height));
            cursor+= (top?1:-1)*(height+gap);
        }
        return result.ToArray();
    }
}

internal static class PrimaryNotificationPopup
{
    private sealed record Entry(Window Window,nint Handle,string? DeviceKey,double Height);
    private static readonly List<Entry> Active=[];
    private static bool arranging;
    private static DispatcherTimer? monitorTimer;
    private static nint targetMonitor;
    internal static void CloseAll(){monitorTimer?.Stop();foreach(var entry in Active.ToArray())entry.Window.Close();}
    internal static void Show(string title,string message,string color,TimeSpan expiration,string? actionText=null,Action? action=null,string? deviceKey=null)
    {
        var dispatcher=Application.Current?.Dispatcher;
        if(dispatcher==null||dispatcher.HasShutdownStarted)return;
        if(!dispatcher.CheckAccess()){dispatcher.BeginInvoke(new Action(()=>Show(title,message,color,expiration,actionText,action,deviceKey)));return;}
        if(deviceKey!=null)foreach(var old in Active.Where(e=>e.DeviceKey==deviceKey).ToArray())old.Window.Close();
        var preferences=RingConfig.Current;
        if(Active.Count==0)targetMonitor=preferences.NotificationMonitor=="Active"?MonitorFromWindow(GetForegroundWindow(),2):MonitorFromPoint(new PointI(),1);
        double height=actionText==null?NotificationPlacement.CompactHeight:NotificationPlacement.ActionHeight;
        var w=new Window{
            Title="Battery Monitor",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Topmost=true,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,
            Width=preferences.NotificationWidth,Height=height,Opacity=preferences.NotificationOpacity/100,Background=new SolidColorBrush(RingPreferences.Ink("#25282E")),Foreground=Brushes.White
        };
        var root=new DockPanel{Margin=new Thickness(9,7,9,7)};
        var accent=new Border{Width=3,CornerRadius=new CornerRadius(2),Margin=new Thickness(0,2,9,2),Background=new SolidColorBrush(RingPreferences.Ink(color))};DockPanel.SetDock(accent,Dock.Left);root.Children.Add(accent);
        var close=new Button{Content="×",Width=23,Height=23,Padding=new Thickness(0),Margin=new Thickness(5,0,0,0),VerticalAlignment=VerticalAlignment.Top};
        close.Click+=(_,_)=>w.Close();DockPanel.SetDock(close,Dock.Right);root.Children.Add(close);
        var content=new StackPanel();
        content.Children.Add(new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,FontSize=13,FontWeight=FontWeights.SemiBold,MaxHeight=34,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=title});
        content.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,0),FontSize=12});
        if(actionText!=null){
            var button=new Button{Content=actionText,Margin=new Thickness(0,5,0,0),Padding=new Thickness(6,2,6,2),HorizontalAlignment=HorizontalAlignment.Left};
            button.Click+=(_,_)=>{action?.Invoke();w.Close();};content.Children.Add(button);
        }
        root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        w.Content=root;
        var handle=new WindowInteropHelper(w).EnsureHandle();
        TaskbarPlacement.MakeToolWindow(handle);
        var entry=new Entry(w,handle,deviceKey,height);
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
            var monitor=targetMonitor;
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(monitor==0||!GetMonitorInfo(monitor,ref info)){monitor=MonitorFromPoint(new PointI(),1);if(monitor==0||!GetMonitorInfo(monitor,ref info))return;targetMonitor=monitor;}
            double scale=GetDpiForSystem()/96d;
            try{if(GetDpiForMonitor(monitor,0,out uint dx,out _)==0&&dx>0)scale=dx/96d;}catch{}
            if(scale<=0)scale=1;
            var work=new Rect(info.Work.Left,info.Work.Top,Math.Max(0,info.Work.Right-info.Work.Left),Math.Max(0,info.Work.Bottom-info.Work.Top));
            var positions=NotificationPlacement.Stack(work,scale,Active.AsEnumerable().Reverse().Select(e=>e.Height).ToArray(),RingConfig.Current.NotificationWidth,RingConfig.Current.NotificationCorner);
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
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint h,uint flags);
    [DllImport("shcore.dll")]private static extern int GetDpiForMonitor(nint m,int kind,out uint x,out uint y);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out RectI rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
}
