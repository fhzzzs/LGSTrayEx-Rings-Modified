using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace LGSTrayUI;

// All custom ring animation work shares the WPF compositor frame clock.
// No rendering subscription survives the last active participant.
internal static class RingFrameClock
{
    private static readonly Stopwatch watch=Stopwatch.StartNew();
    private static readonly Dictionary<object,Action<double>> frames=new();
    private static readonly HashSet<object> lowRate=new();
    private static DispatcherTimer? ambientTimer;
    private static bool usingCompositor;
    private static KeyValuePair<object,Action<double>>[]? frameSnapshot;
    internal static double Now=>watch.Elapsed.TotalMilliseconds;
    internal static int ActiveCount=>frames.Count;
    internal static void Set(object key,Action<double> frame,bool ambient=false){
        frames[key]=frame;frameSnapshot=null;
        if(ambient)lowRate.Add(key);else lowRate.Remove(key);
        SelectClock();
    }
    internal static void Remove(object key){
        if(frames.Remove(key)){lowRate.Remove(key);frameSnapshot=null;SelectClock();}
    }
    private static void SelectClock(){
        bool fullRate=frames.Count>lowRate.Count;
        if(fullRate!=usingCompositor){
            if(fullRate)CompositionTarget.Rendering+=Render;else CompositionTarget.Rendering-=Render;
            usingCompositor=fullRate;
        }
        if(fullRate||frames.Count==0){ambientTimer?.Stop();return;}
        if(ambientTimer==null){
            ambientTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(1000d/30)};
            ambientTimer.Tick+=Render;
        }
        ambientTimer.Start();
    }
    private static void Render(object? sender,EventArgs e){
        double now=Now;
        foreach(var pair in (frameSnapshot??=frames.ToArray()))
            if(frames.TryGetValue(pair.Key,out var current)&&ReferenceEquals(current,pair.Value))current(now);
    }
    internal static double Progress(double now,double start,double duration)=>duration<=0?1:Math.Clamp((now-start)/duration,0,1);
    internal static double Smooth(double t)=>(1-Math.Cos(Math.PI*Math.Clamp(t,0,1)))/2;
}
