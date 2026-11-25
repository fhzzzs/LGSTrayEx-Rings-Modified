using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace LGSTrayUI;
internal sealed class SpringEase : EasingFunctionBase
{
    public double Bounce {get;set;}=.06;
    protected override Freezable CreateInstanceCore()=>new SpringEase{Bounce=Bounce};
    protected override double EaseInCore(double t){if(t<=0||t>=1)return t;double b=Math.Clamp(Bounce,0,.2);if(b==0)return (1-(1+9*t)*Math.Exp(-9*t))/(1-10*Math.Exp(-9));double w=Math.PI/.46,a=-Math.Log(b)/.46;double Raw(double x)=>1-Math.Exp(-a*x)*(Math.Cos(w*x)+a/w*Math.Sin(w*x));double u=Math.Clamp((t-.8)/.2,0,1);return Raw(t)+(1-Raw(1))*u*u*(3-2*u);}
}
// Shared progress preserves the distance between every pair of existing rings.
// A new ring stays invisible until its slot has been cleared.
internal static class RingMotionGeometry
{
    internal static bool CanInterpolate(double[] from,double[] to,double size)
    {
        if(from.Length!=to.Length||!double.IsFinite(size)||size<=0)return false;
        for(int i=0;i<from.Length;i++){
            if(!double.IsFinite(from[i])||!double.IsFinite(to[i]))return false;
            if(i>0&&(from[i]-from[i-1]<size-.001||to[i]-to[i-1]<size-.001))return false;
        }
        return true;
    }
    internal static double Position(double from,double to,double progress)=>from+(to-from)*Math.Clamp(progress,0,1);
}
internal sealed class RingStripAnimator : IDisposable
{
    private sealed class Entry
    {
        internal required Button Button;
        internal required TaskbarBattery Data;
        internal DateTimeOffset? OfflineAt;
        internal bool Leaving,Pending=true;
        internal int Generation,FadeGeneration;
        internal Action? FinishFade;
    }
    internal Func<double>? WaitOverride;
    internal Func<bool>? PlacementReady;
    internal bool PreviewMode,StaticPreview,SeedImmediately;
    private readonly Rectangle divider=new(){IsHitTestVisible=false,Visibility=Visibility.Collapsed};
    private readonly Canvas canvas;
    private readonly Func<Button> create;
    private readonly Action layout;
    private readonly Dictionary<string,Entry> entries=new();
    private readonly DispatcherTimer timer;
    private readonly object moveKey=new();
    private string[] previous=[];
    private bool disposed;
    private double previousSize;
    private Action? moveFrame;
    internal double Width {get;private set;}
    internal int Count=>entries.Count;

    internal RingStripAnimator(Canvas canvas,Func<Button> create,Action layout)
    {
        this.canvas=canvas;this.create=create;this.layout=layout;
        canvas.Children.Add(divider);
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=Tick;
        canvas.IsVisibleChanged+=VisibilityChanged;
    }
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){
        if(canvas.IsVisible)return;
        // Do not run animations for a hidden window/preview. Complete the
        // current layout and pending fades deterministically.
        finishMove?.Invoke();
        foreach(var entry in entries.Values.ToArray())entry.FinishFade?.Invoke();
    }
    private Action? finishMove;
    private void Fade(Entry entry,double to,double ms,Action? done=null,double delay=0)
    {
        int token=++entry.FadeGeneration;var b=entry.Button;
        RingFrameClock.Remove(entry);entry.FinishFade=null;
        double from=b.Opacity;
        void Finish(){
            if(token!=entry.FadeGeneration)return;
            RingFrameClock.Remove(entry);entry.FinishFade=null;
            b.Opacity=to;done?.Invoke();
        }
        if(ms<=0||!canvas.IsVisible){Finish();return;}
        double start=RingFrameClock.Now+Math.Max(0,delay);
        entry.FinishFade=Finish;
        RingFrameClock.Set(entry,now=>{
            if(token!=entry.FadeGeneration){RingFrameClock.Remove(entry);return;}
            double t=RingFrameClock.Progress(now,start,ms);
            b.Opacity=from+(to-from)*RingFrameClock.Smooth(t);
            if(t>=1)Finish();
        });
    }
    private IEnumerable<Entry> Sorted()=>entries.Values.OrderBy(e=>TaskbarBattery.Rank(e.Data.Type)).ThenBy(e=>RingConfig.Current.Order.IndexOf(e.Data.Key) is int i&&i>=0?i:int.MaxValue).ThenBy(e=>e.Data.Key,StringComparer.Ordinal);
    internal void Update(IEnumerable<TaskbarBattery> readings)
    {
        if(disposed)return;
        var c=RingConfig.Current;
        var list=readings.Where(d=>PreviewMode||!c.Hidden.Contains(d.Key)).ToArray();
        var keys=list.Select(d=>d.Key).ToHashSet();bool changed=false;
        foreach(var key in entries.Keys.Where(k=>!keys.Contains(k)).ToArray()){Remove(key);changed=true;}
        foreach(var d in list){
            if(!entries.TryGetValue(d.Key,out var e)){
                if(d.Offline&&!StaticPreview&&(PreviewMode||c.AutoHide))continue;
                var button=create();button.DataContext=d;button.Opacity=0;button.IsHitTestVisible=false;
                e=new Entry{Button=button,Data=d,OfflineAt=d.Offline?DateTimeOffset.UtcNow:null};
                entries.Add(d.Key,e);canvas.Children.Add(button);changed=true;
            }else{
                bool was=e.Data.Offline;
                if(!Equals(e.Data,d)){e.Data=d;e.Button.DataContext=d;}
                if(d.Offline&&!was)e.OfflineAt=DateTimeOffset.UtcNow;
                else if(!d.Offline&&(was||e.Leaving)){
                    e.Generation++;e.OfflineAt=null;e.Leaving=false;
                    if(!e.Pending){e.Button.IsHitTestVisible=true;Fade(e,1,c.Ms(c.InMs));}
                }
                if(!PreviewMode&&!c.AutoHide&&e.Leaving){
                    e.Generation++;e.Leaving=false;
                    if(!e.Pending){Fade(e,1,c.Ms(c.InMs));e.Button.IsHitTestVisible=true;}
                }
            }
        }
        var order=Sorted().Select(e=>e.Data.Key).ToArray();
        if(changed||!previous.SequenceEqual(order))Reflow();
        if(!StaticPreview&&entries.Values.Any(e=>e.OfflineAt.HasValue&&(PreviewMode||c.AutoHide)))timer.Start();else timer.Stop();
    }
    internal void RefreshTimestamps(TaskbarBattery[] rows){foreach(var d in rows)if(entries.TryGetValue(d.Key,out var e)&&e.Data.Updated!=d.Updated){e.Data=d;e.Button.DataContext=d;}}
    internal void RefreshLayout()=>Reflow();
    private void StopMove(){RingFrameClock.Remove(moveKey);moveFrame=null;finishMove=null;}
    internal void Reset(){
        timer.Stop();StopMove();
        foreach(var key in entries.Keys.ToArray())Remove(key);
        previous=[];previousSize=0;Width=0;canvas.Width=0;divider.Visibility=Visibility.Collapsed;
    }
    private void Tick(object? s,EventArgs args)
    {
        var c=RingConfig.Current;
        if(StaticPreview||(!PreviewMode&&!c.AutoHide)){timer.Stop();return;}
        foreach(var e in entries.Values.ToArray()){
            if(e.Leaving||e.OfflineAt is not DateTimeOffset t||!e.Data.Offline||(DateTimeOffset.UtcNow-t).TotalMilliseconds<Math.Max(0,WaitOverride?.Invoke()??c.WaitMs))continue;
            e.Leaving=true;e.Button.IsHitTestVisible=false;int gen=++e.Generation;
            // Keep the old slot occupied throughout the fade; close the gap only after removal.
            Fade(e,0,e.Pending?0:c.Ms(c.OutMs),()=>{
                if(disposed||gen!=e.Generation||!e.Data.Offline)return;
                Remove(e.Data.Key);Reflow();
            });
        }
        if(!entries.Values.Any(e=>e.OfflineAt.HasValue&&!e.Leaving))timer.Stop();
    }
    private void Remove(string k){
        if(!entries.Remove(k,out var e))return;
        e.Generation++;e.FadeGeneration++;RingFrameClock.Remove(e);e.FinishFade=null;canvas.Children.Remove(e.Button);
    }
    private void Reflow()
    {
        if(disposed)return;
        StopMove();
        var c=RingConfig.Current;var es=Sorted().ToArray();
        previous=es.Select(e=>e.Data.Key).ToArray();
        double size=Math.Clamp(c.Size,24,52),gap=Math.Clamp(c.Gap,0,20),step=size+gap;
        double target=es.Length==0?0:es.Length*size+(es.Length-1)*gap+(c.Divider?8:0);
        bool sameAxis=Math.Abs(previousSize-size)<.001;
        previousSize=size;
        Width=Math.Max(Width,target);canvas.Width=Width;canvas.Height=size;
        divider.Visibility=c.Divider&&es.Length>0?Visibility.Visible:Visibility.Collapsed;
        divider.Width=Math.Clamp(c.DividerWidth,.5,4);divider.Height=Math.Clamp(c.DividerHeight,4,size);
        divider.Fill=new SolidColorBrush(RingPreferences.Ink(c.DividerColor));Canvas.SetTop(divider,(size-divider.Height)/2);
        Canvas.SetLeft(divider,2);
        var prop=Canvas.RightProperty;
        var moves=new List<(Entry e,double from,double to)>();
        for(int i=0;i<es.Length;i++){
            var e=es[i];var button=e.Button;
            double to=(es.Length-1-i)*step;
            double from=(double)button.GetValue(prop);
            button.Width=button.Height=size;
            if(e.Pending||!sameAxis||!double.IsFinite(from))from=to;
            button.SetValue(prop,from);
            if(!e.Pending)moves.Add((e,from,to));
        }
        // Right anchoring reverses coordinate order; compare in increasing coordinate order.
        moves=moves.OrderBy(m=>m.to).ToList();
        bool safe=sameAxis&&RingMotionGeometry.CanInterpolate(moves.Select(m=>m.from).ToArray(),moves.Select(m=>m.to).ToArray(),size);
        double duration=SeedImmediately?0:c.Ms(c.SpringMs);
        bool animate=canvas.IsVisible&&safe&&duration>0&&moves.Any(m=>Math.Abs(m.from-m.to)>.01);
        layout();
        void Finish(){
            StopMove();
            foreach(var m in moves)m.e.Button.SetValue(prop,m.to);
            Width=target;canvas.Width=target;layout();
            ConfirmPlacement();
        }
        // Reordering or changing size/alignment may require crossing paths. Snap that
        // layout atomically instead of animating visible rings through one another.
        if(!animate){Finish();return;}
        IEasingFunction easing=c.Motion=="Spring"?new SpringEase{Bounce=c.Bounce/100,EasingMode=EasingMode.EaseIn}:c.Motion=="Quint"?new QuinticEase{EasingMode=EasingMode.EaseOut}:new SineEase{EasingMode=EasingMode.EaseInOut};
        double started=RingFrameClock.Now;
        finishMove=Finish;
        moveFrame=()=>{
            double t=RingFrameClock.Progress(RingFrameClock.Now,started,duration);
            double progress=Math.Clamp(easing.Ease(t),0,1);
            foreach(var m in moves)m.e.Button.SetValue(prop,RingMotionGeometry.Position(m.from,m.to,progress));
            if(t>=1)Finish();
        };
        RingFrameClock.Set(moveKey,_=>moveFrame?.Invoke());
    }
    // Pending rings become visible only after the strip has a verified slot
    // and all existing visible rings have completed their movement.
    internal void ConfirmPlacement(){
        if(disposed||moveFrame!=null||!(PlacementReady?.Invoke()??true))return;
        var c=RingConfig.Current;int index=0;
        foreach(var e in Sorted().Where(e=>e.Pending&&!e.Leaving)){
            e.Pending=false;int gen=++e.Generation;
            Fade(e,1,SeedImmediately?0:c.Ms(c.InMs),()=>{if(e.Generation==gen)e.Button.IsHitTestVisible=true;},index++*c.Ms(c.StaggerMs));
        }
    }
    public void Dispose(){
        disposed=true;canvas.IsVisibleChanged-=VisibilityChanged;timer.Stop();timer.Tick-=Tick;StopMove();canvas.Children.Remove(divider);
        foreach(var k in entries.Keys.ToArray())Remove(k);
    }
}
