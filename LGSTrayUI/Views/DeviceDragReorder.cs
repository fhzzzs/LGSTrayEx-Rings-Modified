using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LGSTrayUI;

// Live slot insertion: mouse capture stays on the list, not the moving child.
internal sealed class DeviceDragReorder
{
    private readonly StackPanel list;
    private readonly Action<bool> active;
    private readonly Action<string[]> commit;
    private readonly DispatcherTimer scrollTimer=new(){Interval=TimeSpan.FromMilliseconds(30)};
    private FrameworkElement? dragged;
    private readonly Dictionary<FrameworkElement,int> categories=new();
    private UIElement[] original=[];
    private Point down;
    private double grabY;
    private bool moving;
    internal DeviceDragReorder(StackPanel list,Action<bool> active,Action<string[]> commit)
    {
        this.list=list;this.active=active;this.commit=commit;
        list.PreviewMouseMove+=(_,e)=>{if(dragged==null)return;if(e.LeftButton!=MouseButtonState.Pressed){Finish(false);return;}Move(e.GetPosition(list));e.Handled=true;};
        list.PreviewMouseLeftButtonUp+=(_,e)=>{if(dragged==null)return;Move(e.GetPosition(list));Finish(true);e.Handled=true;};
        list.LostMouseCapture+=(_,e)=>{if(dragged!=null&&e.OriginalSource==list)Finish(false);};
        list.Unloaded+=(_,_)=>Finish(false);
        scrollTimer.Tick+=(_,_)=>{if(!moving)return;var scroll=AncestorScroll();if(scroll==null)return;var p=Mouse.GetPosition(scroll);double delta=p.Y<32?-12:p.Y>scroll.ViewportHeight-32?12:0;if(delta==0)return;double old=scroll.VerticalOffset;scroll.ScrollToVerticalOffset(Math.Clamp(old+delta,0,scroll.ScrollableHeight));scroll.UpdateLayout();Move(Mouse.GetPosition(list));};
    }
    private ScrollViewer? AncestorScroll(){for(DependencyObject? p=list;p!=null;p=VisualTreeHelper.GetParent(p))if(p is ScrollViewer s)return s;return null;}
    internal void Attach(FrameworkElement card,FrameworkElement grip,string key,int category=0)
    {
        categories[card]=category;card.Tag=key;card.RenderTransform=new TranslateTransform();
        grip.PreviewMouseLeftButtonDown+=(_,e)=>{
            if(dragged!=null)return;list.UpdateLayout();down=e.GetPosition(list);grabY=down.Y-Top(card)-Offset(card);
            original=list.Children.Cast<UIElement>().ToArray();dragged=card;moving=false;
            if(!Mouse.Capture(list)){dragged=null;return;}active(true);e.Handled=true;
        };
    }
    private static double Top(FrameworkElement card)=>LayoutInformation.GetLayoutSlot(card).Top+card.Margin.Top;
    private static double Offset(FrameworkElement card)=>((TranslateTransform)card.RenderTransform).Y;
    private static void OffsetTo(FrameworkElement card,double y,bool animate)
    {
        var t=(TranslateTransform)card.RenderTransform;t.BeginAnimation(TranslateTransform.YProperty,null);t.Y=0;
        if(!animate){t.Y=y;return;}var c=RingConfig.Current;double duration=c.Ms(c.SpringMs);
        if(duration<=0||Math.Abs(y)<.1)return;
        IEasingFunction ease=c.Motion=="Spring"?new SpringEase{Bounce=c.Bounce/100}:c.Motion=="Quint"?new QuinticEase{EasingMode=EasingMode.EaseOut}:new SineEase{EasingMode=EasingMode.EaseInOut};
        t.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(y,0,TimeSpan.FromMilliseconds(duration)){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
    }
    // Calculate the insertion slot from the dragged card's center, not where its handle lands.
    internal static int TargetIndex(int index,double center,double[] centers)
    {
        if(index<0||index>=centers.Length)return index;
        int target=index;const double hysteresis=3;
        while(target+1<centers.Length&&center>(centers[target]+centers[target+1])/2+hysteresis)target++;
        while(target>0&&center<(centers[target]+centers[target-1])/2-hysteresis)target--;return target;
    }
    private void Move(Point point)
    {
        var card=dragged;if(card==null)return;
        if(!moving){if(Math.Abs(point.Y-down.Y)<SystemParameters.MinimumVerticalDragDistance&&Math.Abs(point.X-down.X)<SystemParameters.MinimumHorizontalDragDistance)return;moving=true;Panel.SetZIndex(card,100);card.Opacity=.92;scrollTimer.Start();}
        double desired=point.Y-grabY;var cards=list.Children.Cast<FrameworkElement>().ToArray();int index=Array.IndexOf(cards,card);if(index<0)return;
        int target=TargetIndex(index,desired+card.ActualHeight/2,cards.Select(x=>Top(x)+x.ActualHeight/2).ToArray());
        int category=categories[card];int first=Array.FindIndex(cards,x=>categories[x]==category),last=Array.FindLastIndex(cards,x=>categories[x]==category);
        target=Math.Clamp(target,first,last);
        if(target!=index){var positions=cards.ToDictionary(x=>x,x=>Top(x)+Offset(x));list.Children.RemoveAt(index);list.Children.Insert(target,card);list.UpdateLayout();foreach(var sibling in cards.Where(x=>x!=card))OffsetTo(sibling,positions[sibling]-Top(sibling),true);}
        OffsetTo(card,desired-Top(card),false);
    }
    private void Finish(bool save)
    {
        var card=dragged;if(card==null)return;dragged=null;scrollTimer.Stop();bool changed=moving;moving=false;
        if(Mouse.Captured==list)Mouse.Capture(null);
        if(changed){var positions=list.Children.Cast<FrameworkElement>().ToDictionary(x=>x,x=>Top(x)+Offset(x));if(!save){list.Children.Clear();foreach(var item in original)list.Children.Add(item);list.UpdateLayout();}
            foreach(FrameworkElement item in list.Children)OffsetTo(item,positions.GetValueOrDefault(item,Top(item))-Top(item),true);
            card.Opacity=1;Panel.SetZIndex(card,0);
            if(save)commit(list.Children.Cast<FrameworkElement>().Select(x=>(string)x.Tag).ToArray());
        }
        active(false);
    }
}
