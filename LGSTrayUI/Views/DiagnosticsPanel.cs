using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal sealed class DiagnosticsPanel:StackPanel
{
    internal DiagnosticsPanel(){
        Children.Add(new TextBlock{Text="设备与运行诊断",FontSize=18,FontWeight=FontWeights.SemiBold});
        Children.Add(new TextBlock{Text="显示本地设备 ID、来源和读取耗时。复制报告前可自行检查内容；不会自动发送。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,5)});
        var report=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=430,FontFamily=new FontFamily("Consolas, Microsoft YaHei UI")};
        var actions=new WrapPanel();Children.Add(actions);Children.Add(report);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)};Children.Add(status);
        void Refresh(){report.Text=RingDiagnostics.Report(DateTimeOffset.UtcNow);status.Text=$"报告已刷新 · {report.Text.Length} 字符";}
        async Task CopyAsync(){
            Refresh();string snapshot=report.Text;
            for(int attempt=0;attempt<5;attempt++)try{
                Clipboard.SetDataObject(snapshot,true);
                if(Clipboard.GetText(TextDataFormat.UnicodeText)==snapshot){status.Text=$"已完整复制 · {snapshot.Length} 字符";return;}
            }catch(ExternalException){await Task.Delay(80);}
            status.Text="剪贴板复制或校验失败，请使用导出 TXT。";
        }
        void Export(){
            Refresh();var dialog=new SaveFileDialog{Filter="Text file (*.txt)|*.txt",FileName="battery-diagnostics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"};
            if(dialog.ShowDialog()!=true)return;
            try{File.WriteAllText(dialog.FileName,report.Text,System.Text.Encoding.UTF8);status.Text=$"已完整导出 · {report.Text.Length} 字符";}
            catch(Exception ex){status.Text="导出失败："+ex.Message;}
        }
        Button Action(string label,Action action){var b=new Button{Content=label,Margin=new Thickness(0,4,6,4),Padding=new Thickness(10,5,10,5)};b.Click+=(_,_)=>action();return b;}
        actions.Children.Add(Action("刷新诊断",Refresh));
        var inspect=Action("扫描硬件兼容性",()=>{});
        inspect.Click+=async(_,_)=>{
            inspect.IsEnabled=false;status.Text="正在枚举 HID 接口（只读）";
            try{RingDiagnostics.HardwareInventory=await Task.Run(AsusBatteryReader.InspectHardware);Refresh();}
            catch(Exception ex){status.Text="硬件枚举失败："+ex.Message;}
            finally{inspect.IsEnabled=true;}
        };
        actions.Children.Add(inspect);
        actions.Children.Add(Action("导入设备名称目录",()=>{
            var dialog=new OpenFileDialog{Filter="JSON catalog (*.json)|*.json"};
            if(dialog.ShowDialog()!=true)return;
            try{int count=PeripheralDisplayCatalog.Import(dialog.FileName);status.Text=$"已导入 {count} 个名称分类，下次扫描生效。不会新增未知 HID 指令。";}
            catch(Exception ex){status.Text="目录导入失败："+ex.Message;}
        }));
        actions.Children.Add(Action("查看协议目录",()=>{
            var text=string.Join(Environment.NewLine,System.Linq.Enumerable.Select(PeripheralCatalog.Asus,p=>$"{p.Pid:X4} · {p.Name} · {p.Protocol}"));
            var window=new Window{Title="Battery Monitor · 兼容性目录",Width=650,Height=580,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Content=new TextBox{Text="协议适配目录（非全型号实机认证）"+Environment.NewLine+text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
            window.ShowDialog();
        }));
        actions.Children.Add(Action("复制报告",()=>{_ = CopyAsync();}));
        actions.Children.Add(Action("导出完整 TXT",Export));
        actions.Children.Add(Action("打开日志目录",()=>Process.Start(new ProcessStartInfo(AppDataPaths.LocalAppDataDir){UseShellExecute=true})));
        Loaded+=(_,_)=>Refresh();
    }
}
