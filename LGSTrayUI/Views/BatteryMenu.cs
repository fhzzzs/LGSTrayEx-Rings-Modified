using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
namespace LGSTrayUI;
internal static class BatteryMenu
{
    private static Brush Ink(string value)=>new SolidColorBrush(RingPreferences.Ink(value));
    private static ControlTemplate Frame(Brush background,Brush border){
        var outer=new FrameworkElementFactory(typeof(Border));outer.SetValue(Border.BackgroundProperty,background);outer.SetValue(Border.BorderBrushProperty,border);outer.SetValue(Border.BorderThicknessProperty,new Thickness(1));outer.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));outer.SetValue(Border.PaddingProperty,new Thickness(5));
        var items=new FrameworkElementFactory(typeof(ItemsPresenter));outer.AppendChild(items);return new ControlTemplate(typeof(ContextMenu)){VisualTree=outer};
    }
    private static ControlTemplate ItemFrame(Brush hover){
        var outer=new FrameworkElementFactory(typeof(Border),"Surface");outer.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));outer.SetValue(Border.PaddingProperty,new Thickness(9,0,9,0));outer.SetValue(FrameworkElement.MinHeightProperty,36d);outer.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetBinding(ContentPresenter.ContentProperty,new Binding("Header"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);outer.AppendChild(content);
        var t=new ControlTemplate(typeof(MenuItem)){VisualTree=outer};var trigger=new Trigger{Property=MenuItem.IsHighlightedProperty,Value=true};trigger.Setters.Add(new Setter(Border.BackgroundProperty,hover,"Surface"));t.Triggers.Add(trigger);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.65));t.Triggers.Add(disabled);return t;
    }
    internal static ContextMenu Create(NotifyIconViewModel vm,Action settings,Action? rescan=null,Action? manage=null,Func<int>? onlineCount=null)
    {
        var menu=new ContextMenu{Width=220,HasDropShadow=true,FontFamily=new FontFamily("Segoe UI"),FontSize=13,Placement=PlacementMode.MousePoint,StaysOpen=false};
        var title=new StackPanel();title.Children.Add(new TextBlock{Text="Battery Monitor",FontWeight=FontWeights.SemiBold});var count=new TextBlock{FontSize=11,Opacity=.72};title.Children.Add(count);
        var header=new MenuItem{Header=title,IsEnabled=false,Focusable=false};menu.Items.Add(header);menu.Items.Add(new Separator());
        MenuItem Add(string caption,string glyph,Action? action=null,ICommand? command=null){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,Width=28,VerticalAlignment=VerticalAlignment.Center});row.Children.Add(new TextBlock{Text=caption,VerticalAlignment=VerticalAlignment.Center});var item=new MenuItem{Header=row,Command=command};if(action!=null)item.Click+=(_,_)=>action();menu.Items.Add(item);return item;}
        Add("Settings","\uE713",settings);Add("Rescan devices","\uE72C",rescan,rescan==null?vm.RediscoverDevicesCommand:null);if(manage!=null)Add("Manage devices","\uE772",manage);menu.Items.Add(new Separator());Add("Exit app","\uE8BB",command:vm.ExitApplicationCommand);
        menu.Opened+=(_,_)=>{bool light=RingPreferences.SystemLight();var fg=Ink(light?"#20242C":"#F5F5F5");menu.Foreground=fg;menu.Template=Frame(Ink(light?"#F8F9FB":"#27292E"),Ink(light?"#D6D9DE":"#45474D"));var template=ItemFrame(Ink(light?"#E8EBEF":"#3B3E45"));foreach(var item in menu.Items.OfType<MenuItem>()){item.Template=template;item.Foreground=fg;}foreach(var sep in menu.Items.OfType<Separator>())sep.Background=Ink(light?"#D6D9DE":"#45474D");count.Text=$"{onlineCount?.Invoke()??vm.LogiDevices.Count(d=>d.IsOnline)} devices online";menu.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(150)));};
        menu.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){menu.IsOpen=false;e.Handled=true;}};
        return menu;
    }
}
