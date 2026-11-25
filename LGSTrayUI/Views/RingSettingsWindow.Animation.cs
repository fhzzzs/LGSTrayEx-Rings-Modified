using LGSTrayPrimitives;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace LGSTrayUI;
internal sealed partial class RingSettingsWindow
{
    private void Play(string type){demoTimer.Stop();demo=type;stage=0;sample=Samples();animator.Reset();largeAnimator.Reset();
        if(type=="uncharge")sample[0]=sample[0] with{Charging=true,Status="充电中"};
        if(type=="in")sample=sample.Take(3).ToArray();SeedPreview();
        if(type=="battery"){stage=3;Schedule(200);return;}
        if(type is "charge" or "uncharge" or "fullcharge"){stage=5;Schedule(type=="uncharge"?650:200);return;}
        if(type=="in"){status.Text="上线 · Fade In";stage=1;Schedule(250);return;}
        sample[3]=sample[3] with{Status="离线"};UpdatePreview();status.Text="离线 · 渐隐与补位";stage=1;Schedule((skip?C.Ms(C.GrayMs):C.WaitMs)+C.Ms(C.OutMs)+C.Ms(C.SpringMs)+3*C.Ms(C.StaggerMs)+300);}
    private void Schedule(double ms){demoTimer.Interval=TimeSpan.FromMilliseconds(Math.Max(100,ms));demoTimer.Start();}
    private void DemoTick(object? sender,EventArgs e){demoTimer.Stop();
        if(stage==3||stage==4){sample[0]=sample[0] with{Level=stage==3?5:85};UpdatePreview();status.Text="往返电量";stage=stage==3?4:2;Schedule(Math.Max(C.Ms(C.BatteryMs),C.Ms(C.ColorMs))+450);}
        else if(stage==5){bool charging=demo!="uncharge";sample[0]=sample[0] with{Charging=charging,Status=demo=="fullcharge"?"已充满":charging?"充电中":"已连接",Level=demo=="fullcharge"?100:85};UpdatePreview();status.Text=demo=="fullcharge"?"已充满 · 静态闪电":charging?"正在充电 · 流光预览":"停止充电 · 恢复电量配色";if(looping&&demo!="charge"){stage=2;Schedule(1800);}}
        else if(stage==1&&demo!="out"){sample=Samples();UpdatePreview();status.Text="上线 · Fade In";stage=2;Schedule(C.Ms(C.InMs)+C.Ms(C.SpringMs)+3*C.Ms(C.StaggerMs)+300);}
        else if(looping){Play(demo);}else{status.Text="预览完成";}}
}
