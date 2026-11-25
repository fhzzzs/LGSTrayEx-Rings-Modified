using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;

namespace LGSTrayUI;

// All custom ring animation work shares the WPF compositor frame clock.
// No rendering subscription survives the last active participant.
internal static class RingFrameClock
{
    private static readonly Stopwatch watch=Stopwatch.StartNew();
    private static readonly Dictionary<object,Action<double>> frames=new();
    internal static double Now=>watch.Elapsed.TotalMilliseconds;
    internal static int ActiveCount=>frames.Count;
    internal static void Set(object key,Action<double> frame){
        bool empty=frames.Count==0;frames[key]=frame;
        if(empty)CompositionTarget.Rendering+=Render;
    }
    internal static void Remove(object key){
        if(frames.Remove(key)&&frames.Count==0)CompositionTarget.Rendering-=Render;
    }
    private static void Render(object? sender,EventArgs e){
        double now=Now;
        foreach(var pair in frames.ToArray())
            if(frames.TryGetValue(pair.Key,out var current)&&ReferenceEquals(current,pair.Value))current(now);
    }
    internal static double Progress(double now,double start,double duration)=>duration<=0?1:Math.Clamp((now-start)/duration,0,1);
    internal static double Smooth(double t)=>(1-Math.Cos(Math.PI*Math.Clamp(t,0,1)))/2;
}
