using LGSTrayPrimitives;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace LGSTrayUI;
public sealed class BatteryRing : FrameworkElement
{
    private static readonly Geometry[] Glyphs=[Geometry.Parse("M32,10 C22,10 22,33 32,33 C42,33 42,10 32,10 Z M32,10 L32,19 M25,20 L39,20"),Geometry.Parse("M21.5,23 V20 A10.5,10.5 0 0 1 42.5,20 V23 M20,23 Q20,21 22,21 H25.5 V31 H22 Q20,31 20,29 Z M44,23 Q44,21 42,21 H38.5 V31 H42 Q44,31 44,29 Z"),Geometry.Parse("M22,13.5 H42 Q45,13.5 45,17 V27 Q45,30.5 42,30.5 H22 Q19,30.5 19,27 V17 Q19,13.5 22,13.5 Z M23,19 H25 M29,19 H31 M35,19 H37 M41,19 H42 M23,23 H25 M29,23 H31 M35,23 H37 M41,23 H42 M26,27 H38"),Geometry.Parse("M25,13.5 H39 Q43,13.5 46,25 Q48,33 42,29 L37,25 H27 L22,29 Q16,33 18,25 Q21,13.5 25,13.5 Z M22,21 H29 M25.5,17.5 V24.5 M39,19 H40 M42,23 H43")];
    private static readonly DependencyProperty LevelProperty=DependencyProperty.Register("Level",typeof(double),typeof(BatteryRing),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty InkProperty=DependencyProperty.Register("Ink",typeof(Color),typeof(BatteryRing),new FrameworkPropertyMetadata(Colors.Transparent,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty GrayProperty=DependencyProperty.Register("Gray",typeof(double),typeof(BatteryRing),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty ShinePhaseProperty=DependencyProperty.Register("ShinePhase",typeof(double),typeof(BatteryRing),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Geometry Bolt=Geometry.Parse("M47,11 L42,20 H46 L44,27 L52,17 H48 L51,11 Z");
    static BatteryRing(){foreach(var glyph in Glyphs)glyph.Freeze();Bolt.Freeze();}
    private double shinePeriod,shineStart;
    private (string Text,string Font,bool Bold,double Size,Color Color,double Dpi)? textKey;
    private FormattedText? cachedText;
    private readonly object shineKey=new();
    private readonly System.Collections.Generic.Dictionary<DependencyProperty,object> channels=new();
    internal RingPreferences? Preferences {get;set;}
    internal bool? PreviewLight {get;set;}
    private RingPreferences Settings=>Preferences??RingConfig.Current;
    private double Level=>(double)GetValue(LevelProperty); private Color Ink=>(Color)GetValue(InkProperty); private double Gray=>(double)GetValue(GrayProperty);
    public BatteryRing(){Loaded+=(_,_)=>{RingConfig.Changed+=Recolor;Recolor();};Unloaded+=(_,_)=>{RingConfig.Changed-=Recolor;StopShine();StopChannels();};IsVisibleChanged+=(_,_)=>{if(!IsVisible)StopChannels();else if(DataContext is TaskbarBattery d){if(!d.Offline)SetValue(LevelProperty,(double)(d.Level??0));SetValue(GrayProperty,d.Offline?1d:0d);SetValue(InkProperty,Settings.Accent(d));}SyncShine();};}
    private void StopShine(){if(shinePeriod<=0)return;RingFrameClock.Remove(shineKey);shinePeriod=0;SetValue(ShinePhaseProperty,0d);}
    private void SyncShine(){var c=Settings;if(!IsLoaded||!IsVisible||!c.Animate||!c.ChargeAnimate||DataContext is not TaskbarBattery d||d.Offline||!d.Charging||d.Level is not int n||n<=0||n>=100){StopShine();return;}double period=Math.Clamp(c.Ms(c.ChargePeriod),250,10000);if(Math.Abs(period-shinePeriod)<.01)return;StopShine();shinePeriod=period;shineStart=RingFrameClock.Now;RingFrameClock.Set(shineKey,now=>SetValue(ShinePhaseProperty,((now-shineStart)%shinePeriod)/shinePeriod));}
    internal static (double start,double end) ShineSegment(double phase,double level,double length){double extent=Math.Clamp(level,0,100)*3.6, span=Math.Clamp(length,1,60)/175.929*360;double head=Math.Clamp(phase,0,1)*(360+span);return(Math.Clamp(head-span,0,extent),Math.Clamp(head,0,extent));}
    private static Geometry Arc(double start,double end,double radius){double a=start*Math.PI/180,b=end*Math.PI/180;Point P(double t)=>new(32+radius*Math.Sin(t),32-radius*Math.Cos(t));var g=new StreamGeometry();using(var ctx=g.Open()){ctx.BeginFigure(P(a),false,false);ctx.ArcTo(P(b),new Size(radius,radius),0,end-start>180,SweepDirection.Clockwise,true,false);}return g;}

    internal static Brush Accent(TaskbarBattery d)=>new SolidColorBrush(RingConfig.Current.Accent(d));
    internal static readonly Brush Muted=Brushes.Gray;
    private void StopChannels(){foreach(var key in channels.Values)RingFrameClock.Remove(key);channels.Clear();}
    private void Transition(DependencyProperty prop,double ms,Action<double> apply){
        if(channels.Remove(prop,out var old))RingFrameClock.Remove(old);
        if(ms<=0||!IsLoaded||!IsVisible){apply(1);return;}
        var key=new object();channels[prop]=key;double started=RingFrameClock.Now;
        RingFrameClock.Set(key,now=>{
            double t=RingFrameClock.Progress(now,started,ms);apply(RingFrameClock.Smooth(t));
            if(t>=1){RingFrameClock.Remove(key);channels.Remove(prop);}
        });
    }
    private void AnimateDouble(DependencyProperty prop,double to,double ms){
        double from=(double)GetValue(prop);Transition(prop,from==to?0:ms,t=>SetValue(prop,from+(to-from)*t));
    }
    private void AnimateInk(Color to,double ms){
        var from=Ink;Transition(InkProperty,from==to?0:ms,t=>SetValue(InkProperty,Mix(from,to,t)));
    }
    private void Recolor(){if(DataContext is TaskbarBattery d){AnimateInk(Settings.Accent(d),0);SyncShine();InvalidateVisual();}}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(e.Property!=DataContextProperty||e.NewValue is not TaskbarBattery d)return;var old=e.OldValue as TaskbarBattery;if(old!=null&&(old with{Updated=d.Updated})==d)return;var c=Settings;bool fresh=old?.Key!=d.Key;
        if(!d.Offline&&(fresh||old?.Level!=d.Level||old?.Offline==true))AnimateDouble(LevelProperty,d.Level??0,fresh?0:c.Ms(c.BatteryMs));
        if(fresh||old?.Offline!=d.Offline)AnimateDouble(GrayProperty,d.Offline?1:0,fresh?0:c.Ms(c.GrayMs));
        if(fresh||old==null||c.Accent(old)!=c.Accent(d))AnimateInk(c.Accent(d),fresh?0:c.Ms(d.Offline?c.GrayMs:c.ColorMs));SyncShine();InvalidateVisual();}
    private static Color Mix(Color a,Color b,double t){t=Math.Clamp(t,0,1);byte M(byte x,byte y)=>(byte)Math.Round(x+(y-x)*t);return Color.FromArgb(M(a.A,b.A),M(a.R,b.R),M(a.G,b.G),M(a.B,b.B));}
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);if(DataContext is not TaskbarBattery d||ActualWidth<=0||ActualHeight<=0)return;var c=Settings;double s=Math.Min(ActualWidth,ActualHeight)/64;dc.PushTransform(new TranslateTransform((ActualWidth-64*s)/2,(ActualHeight-64*s)/2));dc.PushTransform(new ScaleTransform(s,s));
        // Transparent drawing provides the full rectangular hit target, including the lower half.
        dc.DrawRectangle(new SolidColorBrush(RingPreferences.Ink(c.Background,"#00000000")),null,new Rect(0,0,64,64));
        double w=Math.Clamp(c.RingWidth,1,7),rad=28;var center=new Point(32,32);bool light=PreviewLight??RingPreferences.SystemLight();var track=new SolidColorBrush(RingPreferences.Ink(c.Foreground(d.Key,"Track",light))){Opacity=Math.Clamp(c.TrackOpacity/100,0,1)};dc.DrawEllipse(null,new Pen(track,w),center,rad,rad);
        var hi=Mix(Ink,Color.FromArgb(Ink.A,255,255,255),Math.Clamp(c.Gradient/100,0,1)*(1-Gray));var lo=Mix(Ink,Color.FromArgb(Ink.A,0,0,0),Math.Clamp(c.Gradient/400,0,.5)*(1-Gray));
        var brush=new LinearGradientBrush(new GradientStopCollection{new(hi,0),new(Ink,.65),new(lo,1)},new Point(0,0),new Point(1,1));var cap=c.Rounded?PenLineCap.Round:PenLineCap.Flat;var pen=new Pen(brush,w){StartLineCap=cap,EndLineCap=cap};double level=Math.Clamp(Level,0,100);
        if(level>=99.999)dc.DrawEllipse(null,pen,center,rad,rad);else if(level>0.001){double a=level*Math.PI/50;var geo=new StreamGeometry();using(var ctx=geo.Open()){ctx.BeginFigure(new Point(32,4),false,false);ctx.ArcTo(new Point(32+rad*Math.Sin(a),32-rad*Math.Cos(a)),new Size(rad,rad),0,level>50,SweepDirection.Clockwise,true,false);}dc.DrawGeometry(null,pen,geo);}
        if(shinePeriod>0&&d.Charging&&!d.Offline&&level>0&&level<100){var (start,end)=ShineSegment((double)GetValue(ShinePhaseProperty),level,c.ChargeLength);if(end-start>.01){var glow=new SolidColorBrush(RingPreferences.Ink(c.DeviceInk(d.Key,"ChargeHighlight",c.ChargeHighlight))){Opacity=Math.Clamp(c.ChargeBrightness/100,0,1)};dc.DrawGeometry(null,new Pen(glow,w*.85){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Arc(start,end,rad));}}
        var muted=RingPreferences.Ink(c.DeviceInk(d.Key,"Offline",c.Offline));var icon=new SolidColorBrush(Mix(RingPreferences.Ink(c.Foreground(d.Key,"Icon",light)),muted,Gray));var text=new SolidColorBrush(Mix(RingPreferences.Ink(c.Foreground(d.Key,"Text",light)),muted,Gray));var outline=new Pen(icon,Math.Clamp(c.IconStroke,.5,4)){LineJoin=PenLineJoin.Round,StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        dc.PushTransform(new TranslateTransform(c.IconX,c.IconY));dc.PushTransform(new ScaleTransform(c.IconScale/100,c.IconScale/100,32,21.5));dc.DrawGeometry(null,outline,Glyphs[Math.Clamp(TaskbarBattery.Rank(d.Type),0,3)]);dc.Pop();dc.Pop();
        string label=!d.Offline&&d.ShortLabel==null&&d.Level is >=0 and <=100?$"{Math.Round(level)}%":d.Percent;
        var key=(label,c.Font,c.Bold,Math.Clamp(c.FontSize,8,20),text.Color,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if(cachedText==null||textKey!=key){text.Freeze();cachedText=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily(c.Font),FontStyles.Normal,c.Bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),key.Item4,text,key.Item6);textKey=key;}
        var ft=cachedText!;dc.DrawText(ft,new Point(32-ft.Width/2+c.TextX,c.TextY-ft.Baseline));
        if(d.Charging&&!d.Offline&&c.ChargeShowBolt){var bolt=new SolidColorBrush(RingPreferences.Ink(c.DeviceInk(d.Key,"ChargeBolt",c.ChargeBolt))){Opacity=Math.Clamp(c.ChargeBoltOpacity/100,0,1)};dc.PushTransform(new TranslateTransform(c.ChargeBoltX,c.ChargeBoltY));dc.PushTransform(new ScaleTransform(c.ChargeBoltScale/100,c.ChargeBoltScale/100,47,19));dc.DrawGeometry(bolt,null,Bolt);dc.Pop();dc.Pop();}dc.Pop();dc.Pop();}
}
