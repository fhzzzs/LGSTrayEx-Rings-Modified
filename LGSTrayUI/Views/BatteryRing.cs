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
    private static readonly Geometry Bolt=Geometry.Parse("M47,11 L42,20 H46 L44,27 L52,17 H48 L51,11 Z");
    private static readonly Geometry CheckMark=Geometry.Parse("M44,19 L48,23 L55,14");
    static BatteryRing(){foreach(var glyph in Glyphs)glyph.Freeze();Bolt.Freeze();CheckMark.Freeze();}
    private double shinePeriod,shineStart,lastShineFrame,shinePhase;
    private StreamGeometry? shineArc;
    private (double start, double end) shineSegment;
    private Placement ringPlacement, iconPlacement, boltPlacement;
    private SolidColorBrush? cachedBolt;
    private (string Text,string Font,bool Bold,double Size,Color Color,double Dpi)? textKey;
    private FormattedText? cachedText;
    private string? cachedPercentLabel;
    private long cachedPercentBits;
    private CultureInfo? cachedPercentCulture;
    private NumberFormatInfo? cachedPercentFormat;
    private (Color Color,double Opacity,double Width)? trackKey;
    private Pen? cachedTrack;
    private (Color Hi,Color Mid,Color Lo,double Width,PenLineCap Cap)? gradientKey;
    private Pen? cachedGradient;
    private (Color Color,double Width)? outlineKey;
    private Pen? cachedOutline;
    private (Color Color,double Opacity,double Width)? glowKey;
    private Pen? cachedGlow;
    private double arcLevel=-1;
    private Geometry? cachedArc;
    private (Color Color,double Width)? connectionPenKey;
    private Pen? connectionPen;
    private Color? backgroundKey;
    private Brush? cachedBackground;
    private Color? cachedIconColor,cachedTextColor;
    private SolidColorBrush? cachedIconBrush,cachedTextBrush;
    private SolidColorBrush FrozenBrush(Color color,ref Color? old,ref SolidColorBrush? brush){if(brush==null||old!=color){brush=new SolidColorBrush(color);brush.Freeze();old=color;}return brush;}
    private readonly object shineKey=new();
    private readonly System.Collections.Generic.Dictionary<DependencyProperty,object> channels=new();
    internal RingPreferences? Preferences {get;set;}
    internal bool? PreviewLight {get;set;}
    private RingPreferences Settings=>Preferences??RingConfig.Current;
    private double Level=>(double)GetValue(LevelProperty); private Color Ink=>(Color)GetValue(InkProperty); private double Gray=>(double)GetValue(GrayProperty);
    public BatteryRing(){Loaded+=(_,_)=>{RingConfig.Changed+=Recolor;Recolor();};Unloaded+=(_,_)=>{RingConfig.Changed-=Recolor;StopShine();StopChannels();};IsVisibleChanged+=(_,_)=>{if(!IsVisible)StopChannels();else if(DataContext is TaskbarBattery d){if(!d.Offline)SetValue(LevelProperty,(double)d.BarValue);SetValue(GrayProperty,d.Offline?1d:0d);SetValue(InkProperty,Settings.Accent(d));}SyncShine();};}
    private void StopShine()
    {
        if (shinePeriod <= 0) return;
        RingFrameClock.Remove(shineKey);
        shinePeriod = shinePhase = 0;
        UpdateShineArc(0);
        InvalidateVisual();
    }

    private void SyncShine()
    {
        var c = Settings;
        if (!IsLoaded || !IsVisible || !c.Animate || !c.ChargeAnimate ||
            DataContext is not TaskbarBattery {IsActivelyCharging: true, Level: > 0 and < 100}) {
            StopShine();
            return;
        }
        double period = Math.Clamp(c.Ms(c.ChargePeriod), 250, 10000);
        if (Math.Abs(period - shinePeriod) < .01) return;
        shinePeriod = period;
        shinePhase = 0;
        shineStart = RingFrameClock.Now;
        lastShineFrame = double.NegativeInfinity;
        RingFrameClock.Set(shineKey, AdvanceShine, ambient: true);
        InvalidateVisual();
    }

    private void AdvanceShine(double now)
    {
        if (now - lastShineFrame < 1000d / 30) return;
        lastShineFrame = now;
        shinePhase = ((now - shineStart) % shinePeriod) / shinePeriod;
        UpdateShineArc(Level);
    }

    private void UpdateShineArc(double level)
    {
        var next = level is > 0 and < 100 ? ShineSegment(shinePhase, level, Settings.ChargeLength) : default;
        if (next.end - next.start <= .01) next = default;
        if (next == shineSegment) return;
        shineSegment = next;
        if (next == default) shineArc?.Clear();
        else WriteArc(shineArc ??= new StreamGeometry(), next.start, next.end, 28);
    }
    internal static (double start,double end) ShineSegment(double phase,double level,double length){double extent=Math.Clamp(level,0,100)*3.6, span=Math.Clamp(length,1,60)/175.929*360;double head=Math.Clamp(phase,0,1)*(360+span);return(Math.Clamp(head-span,0,extent),Math.Clamp(head,0,extent));}
    private static void WriteArc(StreamGeometry geometry, double start, double end, double radius)
    {
        double a = start * Math.PI / 180, b = end * Math.PI / 180;
        Point P(double angle) => new(32 + radius * Math.Sin(angle), 32 - radius * Math.Cos(angle));
        using var context = geometry.Open();
        context.BeginFigure(P(a), false, false);
        context.ArcTo(P(b), new Size(radius, radius), 0, end - start > 180, SweepDirection.Clockwise, true, false);
    }

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
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(e.Property!=DataContextProperty||e.NewValue is not TaskbarBattery d)return;var old=e.OldValue as TaskbarBattery;if(old!=null&&TaskbarBattery.SamePresentation(old,d))return;var c=Settings;bool fresh=old?.Key!=d.Key;
        if(!d.Offline&&(fresh||old?.BarValue!=d.BarValue||old?.Offline==true))AnimateDouble(LevelProperty,d.BarValue,fresh?0:c.Ms(c.BatteryMs));
        if(fresh||old?.Offline!=d.Offline)AnimateDouble(GrayProperty,d.Offline?1:0,fresh?0:c.Ms(c.GrayMs));
        if(fresh||old==null||c.Accent(old)!=c.Accent(d))AnimateInk(c.Accent(d),fresh?0:c.Ms(d.Offline?c.GrayMs:c.ColorMs));SyncShine();InvalidateVisual();}
    private static Color Mix(Color a,Color b,double t){t=Math.Clamp(t,0,1);byte M(byte x,byte y)=>(byte)Math.Round(x+(y-x)*t);return Color.FromArgb(M(a.A,b.A),M(a.R,b.R),M(a.G,b.G),M(a.B,b.B));}
    private struct Placement
    {
        private TranslateTransform? offset;
        private ScaleTransform? scaling;

        internal void Push(DrawingContext dc, double scale, double x, double y, double centerX = 0, double centerY = 0)
        {
            if (offset == null || offset.X != x || offset.Y != y) {
                offset = new TranslateTransform(x, y);
                offset.Freeze();
            }
            if (scaling == null || scaling.ScaleX != scale || scaling.CenterX != centerX || scaling.CenterY != centerY) {
                scaling = new ScaleTransform(scale, scale, centerX, centerY);
                scaling.Freeze();
            }
            dc.PushTransform(offset);
            dc.PushTransform(scaling);
        }
    }

    private string PercentLabel(double level)
    {
        double rounded = Math.Round(level);
        var culture = CultureInfo.CurrentCulture;
        var format = culture.NumberFormat;
        // Preserve the original formatting when a caller can mutate its culture.
        if (!culture.IsReadOnly || !format.IsReadOnly) return $"{rounded}%";
        long bits = BitConverter.DoubleToInt64Bits(rounded);
        if (cachedPercentLabel == null || cachedPercentBits != bits ||
            !ReferenceEquals(cachedPercentCulture, culture) || !ReferenceEquals(cachedPercentFormat, format)) {
            cachedPercentLabel = $"{rounded}%";
            cachedPercentBits = bits;
            cachedPercentCulture = culture;
            cachedPercentFormat = format;
        }
        return cachedPercentLabel;
    }

    protected override void OnRender(DrawingContext dc){base.OnRender(dc);if(DataContext is not TaskbarBattery d||ActualWidth<=0||ActualHeight<=0)return;var c=Settings;double s=Math.Min(ActualWidth,ActualHeight)/64;
        ringPlacement.Push(dc, s, (ActualWidth-64*s)/2, (ActualHeight-64*s)/2);
        // Transparent drawing provides the full rectangular hit target, including the lower half.
        var bg=RingPreferences.Ink(c.Background,"#00000000");if(cachedBackground==null||backgroundKey!=bg){var brush=new SolidColorBrush(bg);brush.Freeze();cachedBackground=brush;backgroundKey=bg;}dc.DrawRectangle(cachedBackground,null,new Rect(0,0,64,64));
        double w=Math.Clamp(c.RingWidth,1,7),rad=28;var center=new Point(32,32);bool light=PreviewLight??RingPreferences.SystemLight();var tk=(RingPreferences.Ink(c.Foreground(d.Key,"Track",light)),Math.Clamp(c.TrackOpacity/100,0,1),w);if(cachedTrack==null||trackKey!=tk){var track=new SolidColorBrush(tk.Item1){Opacity=tk.Item2};cachedTrack=new Pen(track,w);cachedTrack.Freeze();trackKey=tk;}dc.DrawEllipse(null,cachedTrack,center,rad,rad);
        var ink = Ink; double gray = Gray;
        var hi=Mix(ink,Color.FromArgb(ink.A,255,255,255),Math.Clamp(c.Gradient/100,0,1)*(1-gray));var lo=Mix(ink,Color.FromArgb(ink.A,0,0,0),Math.Clamp(c.Gradient/400,0,.5)*(1-gray));
        var cap=c.Rounded?PenLineCap.Round:PenLineCap.Flat;var gk=(hi,ink,lo,w,cap);if(cachedGradient==null||gradientKey!=gk){var brush=new LinearGradientBrush(new GradientStopCollection{new(hi,0),new(ink,.65),new(lo,1)},new Point(0,0),new Point(1,1));cachedGradient=new Pen(brush,w){StartLineCap=cap,EndLineCap=cap};cachedGradient.Freeze();gradientKey=gk;}var pen=cachedGradient;double level=Math.Clamp(Level,0,100);
        if(!d.IsFull&&(d.ConnectionOnly||(!d.Offline&&(d.Stale||d.Connection!=ConnectionState.Connected||d.Level==null&&d.Quality==ReadingQuality.Unknown)))){var ck=(ink,w*.8);if(connectionPen==null||connectionPenKey!=ck){connectionPen=new Pen(new SolidColorBrush(ck.Item1),ck.Item2){DashStyle=new DashStyle(new double[]{1.6,2.2},0),DashCap=PenLineCap.Round};connectionPen.Freeze();connectionPenKey=ck;}dc.DrawEllipse(null,connectionPen,center,rad,rad);}else if(level>=99.999)dc.DrawEllipse(null,pen,center,rad,rad);else if(level>0.001){if(cachedArc==null||arcLevel!=level){var geo=new StreamGeometry();WriteArc(geo,0,level*3.6,rad);geo.Freeze();cachedArc=geo;arcLevel=level;}dc.DrawGeometry(null,pen,cachedArc);}
        if (shinePeriod > 0 && d.IsActivelyCharging) {
            UpdateShineArc(level);
            var g = (RingPreferences.Ink(c.DeviceInk(d.Key,"ChargeHighlight",c.ChargeHighlight)),
                Math.Clamp(c.ChargeBrightness/100,0,1), w*.85);
            if (cachedGlow == null || glowKey != g) {
                var glow = new SolidColorBrush(g.Item1) { Opacity = g.Item2 };
                cachedGlow = new Pen(glow,g.Item3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                cachedGlow.Freeze();
                glowKey = g;
            }
            // WPF retains this geometry even when empty. The clock edits only the highlight.
            dc.DrawGeometry(null, cachedGlow, shineArc ??= new StreamGeometry());
        }
        var muted=RingPreferences.Ink(c.DeviceInk(d.Key,"Offline",c.Offline));var icon=FrozenBrush(Mix(RingPreferences.Ink(c.Foreground(d.Key,"Icon",light)),muted,gray),ref cachedIconColor,ref cachedIconBrush);var text=FrozenBrush(Mix(RingPreferences.Ink(c.Foreground(d.Key,"Text",light)),muted,gray),ref cachedTextColor,ref cachedTextBrush);var ok=(icon.Color,Math.Clamp(c.IconStroke,.5,4));if(cachedOutline==null||outlineKey!=ok){cachedOutline=new Pen(icon,ok.Item2){LineJoin=PenLineJoin.Round,StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};cachedOutline.Freeze();outlineKey=ok;}var outline=cachedOutline;
        iconPlacement.Push(dc, c.IconScale/100, c.IconX, c.IconY, 32, 21.5);
        dc.DrawGeometry(null,outline,Glyphs[Math.Clamp(TaskbarBattery.Rank(d.Type),0,3)]);dc.Pop();dc.Pop();
        string label=!d.IsFull&&!d.Offline&&!d.Stale&&d.IsConnected&&d.ShortLabel==null&&d.Level is >=0 and <=100?PercentLabel(level):d.RingLabel;
        var key=(label,c.Font,c.Bold,Math.Clamp(c.FontSize,8,20),text.Color,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if(cachedText==null||textKey!=key){cachedText=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily(c.Font),FontStyles.Normal,c.Bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),key.Item4,text,key.Item6);textKey=key;}
        var ft=cachedText!;dc.DrawText(ft,new Point(32-ft.Width/2+c.TextX,c.TextY-ft.Baseline));
        if(d.IsFull){dc.DrawGeometry(null,pen,CheckMark);}else if(d.IsActivelyCharging&&c.ChargeShowBolt){
            var color = RingPreferences.Ink(c.DeviceInk(d.Key,"ChargeBolt",c.ChargeBolt));
            double opacity = Math.Clamp(c.ChargeBoltOpacity/100,0,1);
            if (cachedBolt == null || cachedBolt.Color != color || cachedBolt.Opacity != opacity) {
                cachedBolt = new SolidColorBrush(color) {Opacity = opacity};
                cachedBolt.Freeze();
            }
            boltPlacement.Push(dc, c.ChargeBoltScale/100, c.ChargeBoltX, c.ChargeBoltY, 47, 19);
            dc.DrawGeometry(cachedBolt,null,Bolt);
            dc.Pop();dc.Pop();
        }
        dc.Pop();dc.Pop();
    }
}
