using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace LGSTrayUI;

internal sealed class ReleaseUpdatePanel:StackPanel
{
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,5)};
    private readonly Button check=new(){Content="检查更新",Margin=new Thickness(0,3,5,3),Padding=new Thickness(10,5,10,5)};
    private readonly Button install=new(){Content="下载并验证",IsEnabled=false,Margin=new Thickness(0,3,5,3),Padding=new Thickness(10,5,10,5)};
    private CancellationTokenSource? operation;
    private RingsRelease? release;
    private string? staged;
    internal ReleaseUpdatePanel(){
        Children.Add(new TextBlock{Text="版本与更新",FontSize=18,FontWeight=FontWeights.SemiBold});
        Children.Add(new TextBlock{Text=$"当前 Rings {RingsUpdateService.Current} · {NotifyIconViewModel.AssemblyBuildInfo}",TextWrapping=TextWrapping.Wrap});
        Children.Add(status);
        var actions=new WrapPanel();actions.Children.Add(check);actions.Children.Add(install);Children.Add(actions);
        var cancel=new Button{Content="取消操作",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,3,5,3)};
        cancel.Click+=(_,_)=>operation?.Cancel();actions.Children.Add(cancel);
        check.Click+=async(_,_)=>{
            if(operation!=null)return;using var cts=new CancellationTokenSource();operation=cts;check.IsEnabled=install.IsEnabled=false;staged=null;release=null;install.Content="下载并验证";
            try{status.Text="正在检查正式发布版本…";release=await RingsUpdateService.CheckAsync(cts.Token);status.Text=release==null?"没有发现更高的正式版本。":$"发现 {release.Tag} · {release.Size/1048576d:F1} MiB\n{release.Notes}";install.IsEnabled=release!=null;}
            catch(OperationCanceledException){status.Text="检查已取消或超时。";}
            catch(Exception ex){status.Text="检查失败："+ex.Message;}
            finally{operation=null;check.IsEnabled=true;}
        };
        install.Click+=async(_,_)=>{
            if(operation!=null)return;
            if(staged!=null){Restart(staged);return;}
            if(release==null)return;
            using var cts=new CancellationTokenSource();operation=cts;check.IsEnabled=install.IsEnabled=false;
            try{
                var progress=new Progress<int>(n=>status.Text=$"下载中 {n}% · 下载后校验 SHA-256");
                staged=await RingsUpdateService.StageAsync(release,progress,cts.Token);
                status.Text="校验通过。新版已解压到独立版本目录，旧版未覆盖。点击重启启用。";
                install.Content="退出并启动新版";install.IsEnabled=true;
            }catch(OperationCanceledException){status.Text="下载已取消或超时。";install.IsEnabled=true;}
            catch(Exception ex){status.Text="更新失败："+ex.Message;install.IsEnabled=true;}
            finally{operation=null;check.IsEnabled=true;}
        };
        var previous=RingsUpdateService.PreviousExecutable();
        if(previous!=null&&!string.Equals(previous,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)){
            var rollback=new Button{Content="回退到上一版本",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Left};
            rollback.Click+=(_,_)=>Restart(previous);Children.Add(rollback);
        }
        Children.Add(new TextBlock{Text="更新仅在手动确认后执行。原程序目录保留；个性化设置继续使用原用户数据目录。",TextWrapping=TextWrapping.Wrap,Opacity=.75,Margin=new Thickness(0,8,0,0)});
        Unloaded+=(_,_)=>operation?.Cancel();
    }
    private void Restart(string executable){
        if(!File.Exists(executable)||Application.Current is not App app)return;
        if(MessageBox.Show("确认退出当前版本并启动：\n"+executable+"\n\n原目录仍保留。","Battery Monitor 更新",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try{if(Environment.ProcessPath is string current)RingsUpdateService.RememberPrevious(current);app.RestartInto(executable);}
        catch(Exception ex){status.Text="重启准备失败："+ex.Message;}
    }
}
