using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Diagnostics;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal sealed class DiagnosticsPanel:StackPanel
{
    internal DiagnosticsPanel(){
        Children.Add(new TextBlock{Text="设备与运行诊断",FontSize=18,FontWeight=FontWeights.SemiBold});
        Children.Add(new TextBlock{Text="显示本地设备 ID、来源和读取耗时。复制报告前可自行检查内容；不会自动发送。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,5)});
        var report=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=430,FontFamily=new FontFamily("Consolas, Microsoft YaHei UI")};
        var actions=new WrapPanel();Children.Add(actions);Children.Add(report);
        void Refresh()=>report.Text=RingDiagnostics.Report(DateTimeOffset.UtcNow);
        Button Action(string label,Action action){var b=new Button{Content=label,Margin=new Thickness(0,4,6,4),Padding=new Thickness(10,5,10,5)};b.Click+=(_,_)=>action();return b;}
        actions.Children.Add(Action("刷新诊断",Refresh));
        actions.Children.Add(Action("复制报告",()=>Clipboard.SetText(report.Text)));
        actions.Children.Add(Action("打开日志目录",()=>Process.Start(new ProcessStartInfo(AppDataPaths.LocalAppDataDir){UseShellExecute=true})));
        Loaded+=(_,_)=>Refresh();
    }
}
