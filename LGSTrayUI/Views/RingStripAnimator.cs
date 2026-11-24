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
internal sealed class RingStripAnimator : IDisposable
{
    private sealed class Entry {internal required Button Button;internal required TaskbarBattery Data;internal DateTimeOffset? OfflineAt;internal bool Leaving;internal int Generation;}
    internal Func<double>? WaitOverride; internal bool PreviewMode; internal bool StaticPreview; internal bool SeedImmediately;
    private readonly Rectangle divider=new(){IsHitTestVisible=false,Visibility=Visibility.Collapsed};
    private readonly Canvas canvas; private readonly Func<Button> create; private readonly Action layout; private readonly Dictionary<string,Entry> entries=new();
    private readonly DispatcherTimer timer; private string[] previous=[]; private int layoutVersion; private bool disposed;
    internal double Width {get;private set;} internal int Count=>entries.Count;
    internal RingStripAnimator(Canvas canvas,Func<Button> create,Action layout){this.canvas=canvas;this.create=create;this.layout=layout;canvas.Children.Add(divider);timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=Tick;}
    private static DoubleAnimation Anim(double from,double to,double ms,IEasingFunction? ease=null,double delay=0)=>new(from,to,TimeSpan.FromMilliseconds(Math.Max(0,ms))){EasingFunction=ease??new SineEase{EasingMode=EasingMode.EaseInOut},BeginTime=TimeSpan.FromMilliseconds(delay),FillBehavior=FillBehavior.Stop};
    private static void Fade(Button b,double to,double ms,Action? done=null){double from=b.Opacity;b.BeginAnimation(UIElement.OpacityProperty,null);b.Opacity=to;if(ms<=0){done?.Invoke();return;}var a=Anim(from,to,ms);a.Completed+=(_,_)=>done?.Invoke();b.BeginAnimation(UIElement.OpacityProperty,a);}
    private IEnumerable<Entry> Sorted()=>entries.Values.OrderBy(e=>RingConfig.Current.Order.IndexOf(e.Data.Key) is int i&&i>=0?i:int.MaxValue).ThenBy(e=>TaskbarBattery.Rank(e.Data.Type)).ThenBy(e=>e.Data.Key,StringComparer.Ordinal);
    internal void Update(IEnumerable<TaskbarBattery> readings){if(disposed)return;var c=RingConfig.Current;var list=readings.Where(d=>PreviewMode||!c.Hidden.Contains(d.Key)).ToArray();var keys=list.Select(d=>d.Key).ToHashSet();bool changed=false;foreach(var key in entries.Keys.Where(k=>!keys.Contains(k)).ToArray()){Remove(key);changed=true;}
        foreach(var d in list){if(!entries.TryGetValue(d.Key,out var e)){if(d.Offline&&!StaticPreview&&(PreviewMode||c.AutoHide))continue;var b=create();b.DataContext=d;b.Opacity=0;b.IsHitTestVisible=false;e=new Entry{Button=b,Data=d,OfflineAt=d.Offline?DateTimeOffset.UtcNow:null};entries.Add(d.Key,e);canvas.Children.Add(b);changed=true;int gen=e.Generation;Fade(b,1,SeedImmediately?0:c.Ms(c.InMs),()=>{if(e.Generation==gen)b.IsHitTestVisible=true;});}
            else {bool was=e.Data.Offline;if(!Equals(e.Data,d)){e.Data=d;e.Button.DataContext=d;}if(d.Offline&&!was){e.OfflineAt=DateTimeOffset.UtcNow;}else if(!d.Offline&&(was||e.Leaving)){e.Generation++;e.OfflineAt=null;e.Leaving=false;e.Button.IsHitTestVisible=true;Fade(e.Button,1,c.Ms(c.InMs));}if(!PreviewMode&&!c.AutoHide&&e.Leaving){e.Generation++;e.Leaving=false;Fade(e.Button,1,c.Ms(c.InMs));e.Button.IsHitTestVisible=true;}}}
        var order=Sorted().Select(e=>e.Data.Key).ToArray();if(changed||!previous.SequenceEqual(order))Reflow();if(!StaticPreview&&entries.Values.Any(e=>e.OfflineAt.HasValue&&(PreviewMode||c.AutoHide)))timer.Start();else timer.Stop();}
    internal void RefreshLayout()=>Reflow();
    internal void Reset(){timer.Stop();layoutVersion++;foreach(var key in entries.Keys.ToArray())Remove(key);previous=[];Width=0;canvas.Width=0;divider.Visibility=Visibility.Collapsed;}
    private void Tick(object? s,EventArgs args){var c=RingConfig.Current;if(StaticPreview){timer.Stop();return;}if(!PreviewMode&&!c.AutoHide){timer.Stop();return;}foreach(var e in entries.Values.ToArray()){if(e.Leaving||e.OfflineAt is not DateTimeOffset t||!e.Data.Offline||(DateTimeOffset.UtcNow-t).TotalMilliseconds<Math.Max(0,WaitOverride?.Invoke()??c.WaitMs))continue;e.Leaving=true;e.Button.IsHitTestVisible=false;int gen=++e.Generation;Fade(e.Button,0,c.Ms(c.OutMs),()=>{if(disposed||gen!=e.Generation||!e.Data.Offline)return;Remove(e.Data.Key);Reflow();});}if(!entries.Values.Any(e=>e.OfflineAt.HasValue&&!e.Leaving))timer.Stop();}
    private void Remove(string k){if(!entries.Remove(k,out var e))return;e.Generation++;canvas.Children.Remove(e.Button);}
    private void Reflow(){if(disposed)return;var c=RingConfig.Current;var es=Sorted().ToArray();previous=es.Select(e=>e.Data.Key).ToArray();double size=Math.Clamp(c.Size,24,52),gap=Math.Clamp(c.Gap,0,20),step=size+gap,target=es.Length==0?0:es.Length*size+(es.Length-1)*gap+(c.Divider?8:0);double old=Width;Width=Math.Max(old,target);canvas.Width=Width;canvas.Height=size;divider.Visibility=c.Divider&&es.Length>0?Visibility.Visible:Visibility.Collapsed;divider.Width=Math.Clamp(c.DividerWidth,.5,4);divider.Height=Math.Clamp(c.DividerHeight,4,size);divider.Fill=new SolidColorBrush(RingPreferences.Ink(c.DividerColor));Canvas.SetTop(divider,(size-divider.Height)/2);if(c.Left){Canvas.SetRight(divider,2);divider.ClearValue(Canvas.LeftProperty);}else{Canvas.SetLeft(divider,2);divider.ClearValue(Canvas.RightProperty);}layout();int version=++layoutVersion;double duration=c.Ms(c.SpringMs),delay=c.Ms(c.StaggerMs);var moves=new List<(Entry e,double from,double to)>();
        for(int i=0;i<es.Length;i++){var e=es[i];var b=e.Button;b.Width=b.Height=size;var prop=c.Left?Canvas.LeftProperty:Canvas.RightProperty;var opposite=c.Left?Canvas.RightProperty:Canvas.LeftProperty;double to=(c.Left?i:es.Length-1-i)*step;double from=(double)b.GetValue(prop);b.BeginAnimation(opposite,null);b.ClearValue(opposite);if(double.IsNaN(from))from=to;b.BeginAnimation(prop,null);b.SetValue(prop,from);if(Math.Abs(to-from)>.01)moves.Add((e,from,to));else b.SetValue(prop,to);}
        // Closest to the gap starts first; the others follow toward the free edge.
        moves=moves.OrderBy(m=>m.from>m.to?m.to:-m.to).ToList();double total=duration+Math.Max(0,moves.Count-1)*delay;
        for(int i=0;i<moves.Count;i++){var m=moves[i];var prop=c.Left?Canvas.LeftProperty:Canvas.RightProperty;IEasingFunction easing=c.Motion=="Spring"?new SpringEase{Bounce=c.Bounce/100}:c.Motion=="Quint"?new QuinticEase{EasingMode=EasingMode.EaseOut}:new SineEase{EasingMode=EasingMode.EaseInOut};if(duration>0){var a=Anim(m.from,m.to,duration,easing,i*delay);a.FillBehavior=FillBehavior.HoldEnd;m.e.Button.BeginAnimation(prop,a);}else m.e.Button.SetValue(prop,m.to);}
        void Finish(){if(disposed||version!=layoutVersion)return;foreach(var m in moves){var prop=c.Left?Canvas.LeftProperty:Canvas.RightProperty;m.e.Button.BeginAnimation(prop,null);m.e.Button.SetValue(prop,m.to);}Width=target;canvas.Width=target;layout();}if(moves.Count==0||total<=0)Finish();else{var end=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(total+20)};end.Tick+=(_,_)=>{end.Stop();Finish();};end.Start();}}
    public void Dispose(){disposed=true;timer.Stop();canvas.Children.Remove(divider);foreach(var k in entries.Keys.ToArray())Remove(k);}
}
