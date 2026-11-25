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
    private string paletteDevice="";
    private void BuildDevicePalette(Panel parent,RingPreferences c){TitleLine(parent,"独立设备配色");var list=OrderedDevices();if(list.Count==0){parent.Children.Add(Label("连接设备后可单独调整颜色。"));return;}
        var panel=new StackPanel();var combo=new ComboBox{ItemsSource=list.Select(d=>d.Name).ToArray(),SelectedIndex=Math.Max(0,list.FindIndex(d=>d.Key==paletteDevice)),Margin=new Thickness(0,0,0,8)};panel.Children.Add(combo);var detail=new StackPanel();panel.Children.Add(detail);parent.Children.Add(PanelCard(ExpandableRow("device-palette","按设备自定义颜色",panel)));
        void Load(bool focus){detail.Children.Clear();if(combo.SelectedIndex<0)return;var d=list[combo.SelectedIndex];paletteDevice=d.Key;if(focus)PreviewDevice(d);
            detail.Children.Add(Label("默认继承全局。开启单项覆盖后可编辑 RGB / HEX / RGBA；模拟电量可查看不同状态。"));detail.Children.Add(Btn("在顶部预览此设备",()=>PreviewDevice(d)));
            var fields=new[]{("正常圆环","Normal",c.Normal),("低电量圆环","Low",c.Low),("极低电量圆环","Critical",c.Critical),("充电圆环","Charging",c.Charging),("充电流光","ChargeHighlight",c.ChargeHighlight),("充电闪电","ChargeBolt",c.ChargeBolt),("离线颜色","Offline",c.Offline),("设备图标","Icon",c.Icon),("百分比","Text",c.Text),("圆环轨道","Track",c.Track)};
            foreach(var (name,key,fallback) in fields){var row=new StackPanel();bool legacy=c.DeviceColors.TryGetValue(d.Key,out var old)&&(key is "Normal" or "Low" or "Critical");string inherited=legacy?old!:fallback;bool has=c.DevicePalette.TryGetValue(d.Key,out var map)&&map.ContainsKey(key);var check=new CheckBox{Content="自定义 · "+name,IsChecked=has||legacy,Margin=new Thickness(0,4,0,4)};row.Children.Add(check);var edit=new StackPanel{Visibility=check.IsChecked==true?Visibility.Visible:Visibility.Collapsed};row.Children.Add(edit);void Store(string v){if(!c.DevicePalette.TryGetValue(d.Key,out var target))c.DevicePalette[d.Key]=target=new();target[key]=v;}
                ColorEditor(edit,name,c.DeviceInk(d.Key,key,inherited),v=>{Store(v);if(sample[0].Key!=d.Key)PreviewDevice(d);});check.Checked+=(_,_)=>{if(sample[0].Key!=d.Key)PreviewDevice(d);Store(c.DeviceInk(d.Key,key,inherited));edit.Visibility=Visibility.Visible;RingConfig.Notify();};check.Unchecked+=(_,_)=>{if(c.DevicePalette.TryGetValue(d.Key,out var target))target.Remove(key);if(legacy){foreach(var band in new[]{"Normal","Low","Critical"}.Where(b=>b!=key)){if(!c.DevicePalette.TryGetValue(d.Key,out var keep))c.DevicePalette[d.Key]=keep=new();keep.TryAdd(band,old!);}c.DeviceColors.Remove(d.Key);}edit.Visibility=Visibility.Collapsed;RingConfig.Notify();};detail.Children.Add(row);}
            detail.Children.Add(Btn("全部恢复继承全局",()=>{c.DevicePalette.Remove(d.Key);c.DeviceColors.Remove(d.Key);RingConfig.Notify();Load(true);}));}
        combo.SelectionChanged+=(_,_)=>Load(true);Load(false);
    }
    private List<TaskbarBattery> OrderedDevices()=>DeviceOrder.Sort(devices,C.Order).ToList();
    private void BuildDevicePage(RingPreferences c){
        Check(body,"自动添加新设备",c.AutoAdd,v=>c.AutoAdd=v);Check(body,"自动合并重复设备（硬件身份 / 唯一罗技型号）",c.AutoDeduplicate,v=>c.AutoDeduplicate=v);body.Children.Add(Label("默认顺序：鼠标、耳机、键盘、手柄。拖动左侧手柄可自定义全部设备顺序，恢复默认排序可撤销。"));
        var list=new StackPanel();body.Children.Add(list);var reorder=new DeviceDragReorder(list,v=>dragging=v,keys=>{C.Order=keys.Concat(C.Order.Where(k=>!keys.Contains(k))).ToList();RingConfig.Notify();});foreach(var d in OrderedDevices()){
            var panel=new StackPanel();var line=new DockPanel{LastChildFill=true};var grip=new TextBlock{Text="⠿",FontSize=24,Cursor=System.Windows.Input.Cursors.SizeAll,Width=30,VerticalAlignment=VerticalAlignment.Center,Background=Brushes.Transparent};DockPanel.SetDock(grip,Dock.Left);line.Children.Add(grip);
            var eye=new CheckBox{Content="显示",IsChecked=!c.Hidden.Contains(d.Key),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(eye,Dock.Right);line.Children.Add(eye);eye.Checked+=(_,_)=>{c.Hidden.Remove(d.Key);RingConfig.Notify();};eye.Unchecked+=(_,_)=>{if(!c.Hidden.Contains(d.Key))c.Hidden.Add(d.Key);RingConfig.Notify();};
            var ring=new BatteryRing{Width=42,Height=42,DataContext=d,Margin=new Thickness(2,0,10,0)};DockPanel.SetDock(ring,Dock.Left);line.Children.Add(ring);var labels=new StackPanel();var title=new TextBlock{Text=c.Names.GetValueOrDefault(d.Key,d.Name),FontSize=15,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};var state=new TextBlock{Text=d.DisplayStatus,Opacity=.7};labels.Children.Add(title);labels.Children.Add(state);line.Children.Add(labels);panel.Children.Add(line);
            TextEntry(panel,"自定义名称",c.Names.GetValueOrDefault(d.Key,d.Name),v=>c.Names[d.Key]=v);var remove=Btn("移除记录",()=>forget(d.Key));remove.Visibility=d.Offline?Visibility.Visible:Visibility.Collapsed;panel.Children.Add(remove);
            var sourceLabels=new Dictionary<string,TextBlock>(StringComparer.Ordinal);var prior=d;
            deviceLiveUpdates[d.Key]=(reading,liveGroup)=>{
                if(!TaskbarBattery.SamePresentation(prior,reading)){
                    prior=reading;ring.DataContext=reading;state.Text=reading.DisplayStatus;
                    title.Text=c.Names.GetValueOrDefault(reading.Key,reading.Name);
                    remove.Visibility=reading.Offline?Visibility.Visible:Visibility.Collapsed;
                }
                if(liveGroup!=null)foreach(var source in liveGroup.Sources)if(sourceLabels.TryGetValue(source.Key,out var label)){
                    string value=SourceStatus(source);if(label.Text!=value)label.Text=value;
                }
            };
            var group=groups.FirstOrDefault(g=>g.Reading.Key==d.Key);if(group!=null){var sources=new StackPanel{Margin=new Thickness(4,4,4,4)};sources.Children.Add(Label(string.Join(" · ",group.Sources.Select(x=>DeviceIdentityManager.Source(x.Key)).Distinct())));if(group.Inferred)sources.Children.Add(Label(group.Sources.Any(ControllerAliasMatcher.IsGip)?"自动匹配：兼容控制器来源或重连记录（非序列号确认）；若为不同设备，可解除合并。":"自动匹配：唯一 Logitech 型号（非序列号确认）；若为两台设备，可解除合并。"));foreach(var source in group.Sources){var label=Label(SourceStatus(source));sourceLabels[source.Key]=label;sources.Children.Add(label);}var options=group.Sources.Select(x=>x.Key).Prepend("").ToArray();var selector=new ComboBox{ItemsSource=options.Select(k=>k==""?"自动选择":DeviceIdentityManager.Source(k)+" · "+k).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(options,c.PreferredSources.GetValueOrDefault(d.Key,"")))};sources.Children.Add(Label("首选数据来源（离线时自动回退）"));sources.Children.Add(selector);deviceDropdowns.Add(selector);selector.SelectionChanged+=(_,_)=>{if(selector.SelectedIndex<0)return;var key=options[selector.SelectedIndex];if(key=="")c.PreferredSources.Remove(d.Key);else c.PreferredSources[d.Key]=key;RingConfig.Notify();};if(group.Sources.Length>1)sources.Children.Add(Btn("解除合并",()=>{DeviceIdentityManager.Split(c,group);RingConfig.Notify();Build();}));sources.Children.Add(Btn("忽略设备",()=>{if(!c.IgnoredDevices.Contains(d.Key))c.IgnoredDevices.Add(d.Key);RingConfig.Notify();Build();}));panel.Children.Add(ExpandableRow("sources:"+d.Key,"数据来源 · "+group.Sources.Length,sources));}

            var card=PanelCard(panel);card.Background=BrushFor("#147F7F7F");list.Children.Add(card);reorder.Attach(card,grip,d.Key);
        }BuildIdentityTools(c);var dd=Advanced();Check(dd,"Windows Gaming Input / GIP",c.UseGip,v=>c.UseGip=v);Check(dd,"XInput（与 GIP 并行）",c.UseXInput,v=>c.UseXInput=v);dd.Children.Add(Label("XInput 只提供槽位及电量档位。唯一兼容 GIP 手柄与无独立电量的 XInput 槽位可临时配对；多个 GIP 或 XInput 设备时不猜测。蓝牙来源须有身份依据或手动合并。"));dd.Children.Add(Label("G735：LIGHTSPEED 模式可通过 G HUB 获取读数；蓝牙电量服务可作为备用。"));Check(dd,"BLE 电池服务",c.UseBle,v=>c.UseBle=v);Check(dd,"ASUS / ROG HID",c.AsusHidEnabled,v=>c.AsusHidEnabled=v);Check(dd,"ASTRO 基站电量",c.UseAstro,v=>c.UseAstro=v);Check(dd,"手柄原生 HID 被动读取",c.UseControllerHid,v=>c.UseControllerHid=v);Check(dd,"GameSir G7 Pro / Cyclone 2 被动 HID 读取（实验性）",c.GameSirPassiveEnabled,v=>c.GameSirPassiveEnabled=v);dd.Children.Add(Label("默认关闭；仅监听厂商接口，不发送主动查询、不切换模式。Windows Gaming Input 不依赖此选项。"));dd.Children.Add(Label("原生读取只监听现有报告，不切换手柄模式；若与厂商软件或游戏冲突，可单独关闭对应来源。"));Slider(dd,"手柄轮询 (秒)",c.PollSeconds,.5,60,.5,v=>c.PollSeconds=v);Slider(dd,"BLE 轮询 (秒)",c.BleSeconds,5,120,5,v=>c.BleSeconds=v);body.Children.Add(Btn("恢复默认排序",()=>{c.Order.Clear();RingConfig.Notify();Build();}));
    }
    private static string SourceStatus(TaskbarBattery source)=>$"{DeviceIdentityManager.Source(source.Key)} · {source.Name} · {source.DisplayStatus}";
    private void BuildIdentityTools(RingPreferences c){
        var panel=new StackPanel();TitleLine(panel,"设备身份管理");panel.Children.Add(Label("只有确认是同一台物理设备时才合并。同型号的两台设备不要合并；合并后保留主设备设置。"));
        var candidates=groups.Where(g=>!g.Ignored).ToArray();
        string Describe(DeviceGroup g)=>c.Names.GetValueOrDefault(g.Reading.Key,g.Reading.Name)+" · "+string.Join(" / ",g.Sources.Select(d=>DeviceIdentityManager.Source(d.Key)).Distinct())+(g.Reading.Offline?" · 离线":"");
        if(candidates.Length>=2){var primary=new ComboBox{ItemsSource=candidates.Select(Describe).ToArray(),SelectedIndex=0};var secondary=new ComboBox();deviceDropdowns.Add(primary);deviceDropdowns.Add(secondary);DeviceGroup[] eligible=[];void Update(){if(primary.SelectedIndex<0)return;var a=candidates[primary.SelectedIndex];eligible=candidates.Where(g=>g.Reading.Key!=a.Reading.Key&&g.Reading.Type==a.Reading.Type).ToArray();secondary.ItemsSource=eligible.Select(Describe).ToArray();secondary.SelectedIndex=eligible.Length>0?0:-1;};primary.SelectionChanged+=(_,_)=>Update();panel.Children.Add(Label("保留的主设备"));panel.Children.Add(primary);panel.Children.Add(Label("合并的重复记录"));panel.Children.Add(secondary);Update();panel.Children.Add(Btn("合并选中的设备",()=>{if(primary.SelectedIndex<0||secondary.SelectedIndex<0)return;var a=candidates[primary.SelectedIndex];var b=eligible[secondary.SelectedIndex];if(MessageBox.Show(this,"确认这两个记录属于同一台物理设备？","合并设备",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;DeviceIdentityManager.Merge(c,a,b);RingConfig.Notify();Build();}));}
        else panel.Children.Add(Label("至少需要两个设备记录才能手动合并。"));
        panel.Children.Add(Btn("清理已断开的历史设备记录",()=>{
            var offline=groups.Where(g=>g.Reading.Offline&&g.Sources.All(s=>s.Offline)).Select(g=>g.Reading.Key).ToArray();
            if(offline.Length==0)return;
            if(MessageBox.Show(this,"仅清理已消失的离线历史。仍被系统枚举的设备不会移除，个性化设置保留。","清理离线历史",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            foreach(var key in offline)forget(key);
        }));
        if(c.SeparateDeviceSources.Count>0)panel.Children.Add(Btn("恢复自动匹配（清除拆分例外）",()=>{c.SeparateDeviceSources.Clear();RingConfig.Notify();Build();}));
        foreach(var group in groups.Where(g=>g.Ignored)){var g=group;panel.Children.Add(Btn("恢复忽略的设备 · "+Describe(g),()=>{c.IgnoredDevices.Remove(g.Reading.Key);RingConfig.Notify();Build();}));}
        foreach(var key in c.IgnoredDevices.Where(k=>!groups.Any(g=>g.Reading.Key==k)).ToArray()){var k=key;panel.Children.Add(Btn("清除历史忽略规则 · "+k,()=>{c.IgnoredDevices.Remove(k);RingConfig.Notify();Build();}));}
        body.Children.Add(PanelCard(panel));
    }
}
