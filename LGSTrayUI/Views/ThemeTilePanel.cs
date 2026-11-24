using System;
using System.Windows;
using System.Windows.Controls;

namespace LGSTrayUI;

// Responsive, equal-width rows. Even the last partial row uses the full content width.
internal static class ThemeTileGeometry
{
    internal const double Gap=12, PreferredWidth=260;
    internal static int Columns(double width,int count)
    {
        if(count<=0)return 1;
        if(!double.IsFinite(width))return Math.Min(4,count);
        int fit=(int)Math.Clamp(Math.Floor((Math.Max(0,width)+Gap)/(PreferredWidth+Gap)),1,4);
        // Four preset cards should be a balanced 2x2 or 1x4 rather than 3+1.
        if(count==4&&fit==3)fit=2;
        return Math.Min(count,fit);
    }
    internal static double CellWidth(double width,int cells)=>Math.Max(0,(width-Gap*Math.Max(0,cells-1))/Math.Max(1,cells));
}
internal sealed class ThemeTilePanel : Panel
{
    private double[] rowHeights=Array.Empty<double>();
    private int measuredColumns=1;
    protected override Size MeasureOverride(Size availableSize)
    {
        int count=InternalChildren.Count;
        if(count==0){rowHeights=Array.Empty<double>();return new Size(0,0);}
        double width=double.IsFinite(availableSize.Width)?Math.Max(0,availableSize.Width):Math.Min(4,count)*(ThemeTileGeometry.PreferredWidth+ThemeTileGeometry.Gap)-ThemeTileGeometry.Gap;
        measuredColumns=ThemeTileGeometry.Columns(width,count);
        rowHeights=new double[(count+measuredColumns-1)/measuredColumns];
        double height=0;
        for(int row=0;row<rowHeights.Length;row++){
            int start=row*measuredColumns,cells=Math.Min(measuredColumns,count-start);
            double cell=ThemeTileGeometry.CellWidth(width,cells);
            for(int i=0;i<cells;i++){
                var child=InternalChildren[start+i];
                child.Measure(new Size(cell,double.PositiveInfinity));
                rowHeights[row]=Math.Max(rowHeights[row],child.DesiredSize.Height);
            }
            height+=rowHeights[row]+(row==0?0:ThemeTileGeometry.Gap);
        }
        return new Size(width,height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        int count=InternalChildren.Count;
        double y=0;
        for(int row=0;row<rowHeights.Length;row++){
            int start=row*measuredColumns,cells=Math.Min(measuredColumns,count-start);
            double cell=ThemeTileGeometry.CellWidth(finalSize.Width,cells);
            for(int i=0;i<cells;i++)InternalChildren[start+i].Arrange(new Rect(i*(cell+ThemeTileGeometry.Gap),y,cell,rowHeights[row]));
            y+=rowHeights[row]+ThemeTileGeometry.Gap;
        }
        return finalSize;
    }
}
