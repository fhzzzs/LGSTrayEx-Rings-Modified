using System;
namespace LGSTrayUI;
internal static class TaskbarLayoutGeometry
{
    internal static (int before,int after,int total) Dimensions(double width,double left,double right,double offset)
    {
        if (!double.IsFinite(width) || width <= 0 || width > 768) return (0,0,0);
        double l=Math.Clamp(double.IsFinite(left)?left:16,0,64), r=Math.Clamp(double.IsFinite(right)?right:16,0,64);
        double x=Math.Clamp(double.IsFinite(offset)?offset:0,-100,100);
        int before=(int)Math.Ceiling(Math.Max(0,l+x));
        int after=(int)Math.Ceiling(Math.Max(0,r-x));
        int total=before+(int)Math.Ceiling(width)+after;
        return total<=1024?(before,after,total):(0,0,0);
    }
    internal static int PreviewAppStart(bool left,bool centered,int reservedWidth,int availableEnd,int appWidth) {
        int minimum=48+(left?Math.Max(0,reservedWidth):0)+5;
        return centered?Math.Max(minimum,(availableEnd-appWidth)/2):minimum;
    }
}
