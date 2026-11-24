using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LGSTrayPrimitives;
namespace LGSTrayUI;

// Target layout simulation; native status below distinguishes real confirmed occupancy.
internal sealed class LayoutPreview : StackPanel
{
    private const int W=900,H=56;
    private readonly Canvas bar=new(){Width=W,Height=H,ClipToBounds=true};
    private readonly TextBlock note=new(){TextWrapping=TextWrapping.Wrap,Opacity=.75,FontSize=12,Margin=new Thickness(0,5,0,0)};
    private readonly TextBlock nativeStatus=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,5,0,0)};
    private readonly System.Windows.Threading.DispatcherTimer statusTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly CheckBox centered=new(){Content="模拟居中的 Windows 应用图标",IsChecked=true};
    private readonly ComboBox count=new(){ItemsSource=new[]{0,1,2,3,4},SelectedIndex=4,Width=62};
    private readonly CheckBox light=new(){Content="浅色任务栏"};
    internal LayoutPreview(){Children.Add(new TextBlock{Text="实时布局预览 · Live preview",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,6,0,2)});var header=new WrapPanel{Margin=new Thickness(0,5,0,6)};header.Children.Add(centered);header.Children.Add(new TextBlock{Text="  模拟设备数  ",VerticalAlignment=VerticalAlignment.Center});header.Children.Add(count);light.Margin=new Thickness(12,0,0,0);header.Children.Add(light);Children.Add(header);Children.Add(new Border{CornerRadius=new CornerRadius(9),ClipToBounds=true,Child=new Viewbox{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Stretch,Child=bar}});Children.Add(note);Children.Add(nativeStatus);statusTimer.Tick+=(_,_)=>RefreshStatus();Loaded+=(_,_)=>{RefreshStatus();statusTimer.Start();};Unloaded+=(_,_)=>statusTimer.Stop();centered.Checked+=(_,_)=>Refresh();centered.Unchecked+=(_,_)=>Refresh();light.Checked+=(_,_)=>Refresh();light.Unchecked+=(_,_)=>Refresh();count.SelectionChanged+=(_,_)=>Refresh();}
    private void RefreshStatus(){var slot=NativeTaskbarSlot.Instance;nativeStatus.Text=slot.Status+(string.IsNullOrEmpty(slot.PlacementStatus)?"":"\n"+slot.PlacementStatus);}
    private static Brush B(string hex)=>new SolidColorBrush(RingPreferences.Ink(hex));
    private void Put(FrameworkElement item,double x,double y){Canvas.SetLeft(item,x);Canvas.SetTop(item,y);bar.Children.Add(item);}
    private void Glyph(string path,string fill,double x,double y,double w=22,double h=22)=>Put(new Path{Data=Geometry.Parse(path),Fill=B(fill),Stretch=Stretch.Uniform,Width=w,Height=h},x,y);
    internal void Refresh(){
        var c=RingConfig.Current;bool isLight=light.IsChecked==true;bar.Background=B(isLight?"#E8EAED":"#20242C");var fg=isLight?"#20242C":"#F5F5F5";bar.Children.Clear();
        int n=Math.Max(0,count.SelectedIndex);double size=Math.Clamp(c.Size,24,52),gap=Math.Clamp(c.Gap,0,20);int width=(int)Math.Ceiling(n*size+Math.Max(0,n-1)*gap+(c.Divider&&n>0?8:0));size=Math.Min(size,H);double y=(H-size)/2+Math.Clamp(c.Y,-10,10);
        var reserved=TaskbarLayoutGeometry.Dimensions(width,c.LeftGap,c.RightGap,c.X);
        bool native=c.Left&&n>0&&reserved.total>0&&reserved.total+184<=784;
        int anchor=0;
        int startButtonRight=anchor+48;
        Glyph("M0,0 H8 V8 H0 Z M10,0 H18 V8 H10 Z M0,10 H8 V18 H0 Z M10,10 H18 V18 H10 Z","#35A7F2",anchor+12,17);
        int rightLimit=!c.Left&&n>0?784-reserved.total:784; int start=TaskbarLayoutGeometry.PreviewAppStart(native,centered.IsChecked==true,native?reserved.total:0,rightLimit,136);var occupied=new List<(int left,int right)>();
        for(int i=0;i<4;i++){int x=start+i*34;occupied.Add((x-5,x+28));if(i==0)Glyph("M0,5 H8 L11,8 H22 V21 H0 Z","#F3C454",x,17);else if(i==1){Put(new Ellipse{Width=22,Height=22,Fill=B("#4285F4")},x,17);Put(new Ellipse{Width=10,Height=10,Fill=B("#BFE2FF")},x+6,23);}else if(i==2)Glyph("M0,0 H22 V22 H0 Z","#137BD1",x,17);else Glyph("M0,0 H22 V22 H0 Z","#64748B",x,17);Put(new Rectangle{Width=10,Height=2,Fill=B(fg),Opacity=.5,RadiusX=1,RadiusY=1},x+6,44);}
        Put(new TextBlock{Text="⌃",Foreground=B(fg),FontSize=17},790,17);Put(new TextBlock{Text="◔  ◖",Foreground=B(fg),FontSize=14},810,19);Put(new TextBlock{Text=DateTime.Now.ToString("h:mm tt\nMM/dd/yyyy"),Foreground=B(fg),FontSize=10,TextAlignment=TextAlignment.Center},849,14);
        if(n==0){note.Text="无在线设备 · 释放全部占位和外部间距";return;}
        int pos;bool fits=native?TaskbarPlacement.TryReserved(startButtonRight,784,width,reserved.before,reserved.after,occupied,out pos):TaskbarPlacement.TryGap(48,784,width,(int)Math.Round(c.LeftGap),(int)Math.Round(c.RightGap),(int)Math.Round(c.X),c.Left,occupied,out pos);
        if(y<0||y+size>H||!fits){note.Text="模拟任务栏没有足够安全空间；实际任务栏不会覆盖原生图标。";return;}
        // Draw the same renderer and divider orientation as the actual strip.
        var types=new[]{DeviceType.Mouse,DeviceType.Headset,DeviceType.Keyboard,DeviceType.Gamepad};var levels=new[]{85,65,15,8};double ringsX=pos+(c.Divider&&!c.Left?8:0);
        for(int i=0;i<n;i++)Put(new BatteryRing{Width=size,Height=size,PreviewLight=isLight,DataContext=new TaskbarBattery(types[i],types[i].ToString(),levels[i],"已连接",false,DateTimeOffset.MinValue,"layout:"+i)},ringsX+i*(size+gap),y);
        if(c.Divider){double dh=Math.Clamp(c.DividerHeight,4,size);Put(new Rectangle{Width=Math.Clamp(c.DividerWidth,.5,4),Height=dh,Fill=B(c.DividerColor)},c.Left?pos+width-2-Math.Clamp(c.DividerWidth,.5,4):pos+2,y+(size-dh)/2);}
        note.Text=$"{(c.Left?"靠左":"靠右")} · 左间距 {c.LeftGap:0.#} DIP · 右间距 {c.RightGap:0.#} DIP · 设备间距 {gap:0.#} DIP · 水平 / 垂直偏移 {c.X:0.#} / {c.Y:0.#} DIP · {(native?"左侧目标布局 · 实际状态见下方":"安全空位模拟")}";
    }
}
