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
    private void BuildNotifications(RingPreferences c)
    {
        var intro=new StackPanel();intro.Children.Add(new TextBlock{Text="电量提醒",FontSize=18,FontWeight=FontWeights.SemiBold});intro.Children.Add(Label("设置设备电量不足时的通知和声音。"));
        var toggles=new WrapPanel();Check(toggles,"设备连接 / 断开提醒",c.ConnectionAlerts,v=>c.ConnectionAlerts=v);Check(toggles,"低电量提醒",c.Alerts,v=>c.Alerts=v);Check(toggles,"播放提示音",c.Sound,v=>c.Sound=v);foreach(FrameworkElement toggle in toggles.Children)toggle.Margin=new Thickness(0,8,24,6);intro.Children.Add(toggles);
        Check(intro,"充电完成提醒（可选）",c.FullChargeAlerts,v=>c.FullChargeAlerts=v);
        Check(intro,"勿扰模式",c.QuietNotifications,v=>c.QuietNotifications=v);
        Check(intro,"全屏时静默",c.SuppressFullScreen,v=>c.SuppressFullScreen=v);
        Choice(intro,"显示器",c.NotificationMonitor,["Primary","Active"],v=>c.NotificationMonitor=v);
        Choice(intro,"通知位置",c.NotificationCorner,["BottomRight","BottomLeft","TopRight","TopLeft"],v=>c.NotificationCorner=v);
        Slider(intro,"通知宽度 (DIP)",c.NotificationWidth,240,420,10,v=>c.NotificationWidth=v);
        Slider(intro,"普通通知显示秒数",c.NotificationSeconds,2,15,1,v=>c.NotificationSeconds=v);
        Slider(intro,"通知不透明度 %",c.NotificationOpacity,60,100,5,v=>c.NotificationOpacity=v);
        var choices=new[]{(Key:"",Name:"所有设备（全局）")}.Concat(devices.Select(d=>(d.Key,d.Name))).ToArray();
        var selector=new ComboBox{ItemsSource=choices.Select(x=>x.Name).ToArray(),SelectedIndex=Math.Max(0,Array.FindIndex(choices,x=>x.Key==notificationDevice)),Margin=new Thickness(0,4,0,0)};
        intro.Children.Add(Label("应用到"));intro.Children.Add(selector);body.Children.Add(PanelCard(intro));
        string key=choices[selector.SelectedIndex].Key;bool global=key=="";
        string Rules()=>global?c.Rules:c.DeviceRules.GetValueOrDefault(key,c.Rules);
        void SetRules(IEnumerable<(int p,int minutes)> rules){string value=string.Join(",",rules.OrderByDescending(x=>x.p).Select(x=>$"{x.p}:{x.minutes}"));if(global)c.Rules=value;else c.DeviceRules[key]=value;RingConfig.Notify();}
        selector.SelectionChanged+=(_,_)=>{if(selector.SelectedIndex>=0){notificationDevice=choices[selector.SelectedIndex].Key;Build();}};
        if(!global){var inherit=new DockPanel();var reset=Btn("恢复全局规则",()=>{c.DeviceRules.Remove(key);c.AlertSounds.Remove(key);RingConfig.Notify();Build();});DockPanel.SetDock(reset,Dock.Right);inherit.Children.Add(reset);inherit.Children.Add(Label(c.DeviceRules.ContainsKey(key)||c.AlertSounds.ContainsKey(key)?"此设备使用独立规则":"此设备继承全局规则"));body.Children.Add(inherit);}
        var heading=new DockPanel{Margin=new Thickness(0,8,0,8)};
        var add=Btn("＋ 添加阈值",()=>{var rules=RingAlerts.Parse(Rules()).ToArray();var used=rules.Select(x=>x.p).ToHashSet();int n=Enumerable.Range(1,20).Reverse().FirstOrDefault(x=>!used.Contains(x));if(n==0)n=Enumerable.Range(1,100).FirstOrDefault(x=>!used.Contains(x));if(n>0){SetRules(rules.Append((n,15)));Build();}});DockPanel.SetDock(add,Dock.Right);heading.Children.Add(add);
        heading.Children.Add(new TextBlock{Text="提醒规则",FontSize=18,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});body.Children.Add(heading);
        body.Children.Add(new TextBlock{Text="达到阈值时提醒。重复间隔为 0 表示只提醒一次。修改数值后按 Enter 或离开输入框自动保存。",Opacity=.7,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        foreach(var rule in RingAlerts.Parse(Rules()).OrderByDescending(x=>x.p).ToArray())
        {
            int threshold=rule.p,minutes=rule.minutes;var card=new StackPanel();
            var top=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,9)};
            var remove=Btn("删除",()=>{if(c.AlertSounds.TryGetValue(key,out var map))map.Remove(threshold);SetRules(RingAlerts.Parse(Rules()).Where(x=>x.p!=threshold));Build();});DockPanel.SetDock(remove,Dock.Right);top.Children.Add(remove);
            var mark=new Border{Width=5,Height=32,CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,10,0),Background=BrushFor("#8E8E93")};DockPanel.SetDock(mark,Dock.Left);top.Children.Add(mark);
            var title=new TextBlock{FontSize=18,FontWeight=FontWeights.SemiBold};var summary=new TextBlock{FontSize=11,Opacity=.7};var header=new StackPanel();header.Children.Add(title);header.Children.Add(summary);top.Children.Add(header);card.Children.Add(top);
            void Header(){title.Text=$"剩余电量 {threshold}%";summary.Text=minutes==0?"仅提醒一次":$"每 {minutes} 分钟重复提醒";mark.Background=BrushFor(threshold<c.CriticalAt?c.Critical:threshold<c.LowAt?c.Low:c.Normal);}
            Header();
            var fields=new Grid{Margin=new Thickness(0,0,0,8)};fields.ColumnDefinitions.Add(new ColumnDefinition());fields.ColumnDefinitions.Add(new ColumnDefinition());
            var pbox=new TextBox{Text=threshold.ToString(),HorizontalAlignment=HorizontalAlignment.Stretch};var mbox=new TextBox{Text=minutes.ToString(),HorizontalAlignment=HorizontalAlignment.Stretch};
            var first=new StackPanel{Margin=new Thickness(0,0,10,0)};first.Children.Add(Label("电量阈值 (%)"));first.Children.Add(pbox);fields.Children.Add(first);var second=new StackPanel();second.Children.Add(Label("重复提醒间隔 (分钟)"));second.Children.Add(mbox);Grid.SetColumn(second,1);fields.Children.Add(second);card.Children.Add(fields);
            var validation=new TextBlock{Foreground=BrushFor("#FF6464"),TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,6)};card.Children.Add(validation);
            void Commit(){if(!int.TryParse(pbox.Text,out int pp)||pp<1||pp>100||!int.TryParse(mbox.Text,out int mm)||mm<0||mm>1440){validation.Text="请输入 1–100% 与 0–1440 分钟。";validation.Visibility=Visibility.Visible;return;}var rules=RingAlerts.Parse(Rules()).ToArray();if(pp!=threshold&&rules.Any(x=>x.p==pp)){validation.Text="此阈值已存在，请选择不同的百分比。";validation.Visibility=Visibility.Visible;return;}validation.Visibility=Visibility.Collapsed;if(pp==threshold&&mm==minutes)return;int old=threshold;if(pp!=old&&c.AlertSounds.TryGetValue(key,out var map)&&map.Remove(old,out var moved))map[pp]=moved;threshold=pp;minutes=mm;SetRules(rules.Where(x=>x.p!=old).Append((pp,mm)));Header();}
            foreach(var field in new[]{pbox,mbox}){field.LostKeyboardFocus+=(_,_)=>Commit();field.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};}
            var audio=c.Audio(key,threshold);double volume=audio.Volume;
            void SetAudio(string sound,double vol){if(!c.AlertSounds.TryGetValue(key,out var map))c.AlertSounds[key]=map=new();map[threshold]=new AlertAudio{Sound=sound,Volume=vol};RingConfig.Notify();}
            card.Children.Add(new Border{Height=1,Background=BrushFor("#307F7F7F"),Margin=new Thickness(0,5,0,10)});var soundLabel=Label("提示音");soundLabel.Margin=new Thickness(0,0,0,8);card.Children.Add(soundLabel);
            var soundRow=new Grid{Margin=new Thickness(0,0,0,8)};soundRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});soundRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});soundRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var sound=new ComboBox{ItemsSource=RingSounds.Options().Append(audio.Sound).Distinct().ToArray(),SelectedItem=audio.Sound,MinWidth=0,Height=36,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};
            var import=Btn("导入 WAV…",()=>{var f=new OpenFileDialog{Filter="WAV 音频|*.wav"};if(f.ShowDialog()==true)try{var choice=RingSounds.Import(f.FileName);SetAudio(choice,volume);sound.ItemsSource=RingSounds.Options();sound.SelectedItem=choice;}catch(Exception ex){MessageBox.Show(ex.Message);}});import.Height=36;import.MinWidth=96;import.Margin=new Thickness(0);import.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(import,2);soundRow.Children.Add(import);
            var listen=Btn("▶ 试听",()=>RingSounds.Play(new AlertAudio{Sound=sound.SelectedItem as string??audio.Sound,Volume=volume},message=>MessageBox.Show(message)));listen.Height=36;listen.MinWidth=72;listen.Margin=new Thickness(0,0,10,0);listen.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(listen,1);soundRow.Children.Add(listen);soundRow.Children.Add(sound);card.Children.Add(soundRow);
            var volumePanel=new StackPanel();Slider(volumePanel,"音量 %",volume,0,100,1,v=>{volume=v;SetAudio(sound.SelectedItem as string??audio.Sound,v);});card.Children.Add(volumePanel);
            var systemNote=new TextBlock{Text="Windows 默认提示音使用系统音量。",Opacity=.7,FontSize=11,Margin=new Thickness(0,4,0,0)};card.Children.Add(systemNote);
            void SoundState(){bool system=Equals(sound.SelectedItem,"Windows Default"),silent=Equals(sound.SelectedItem,"None");volumePanel.Visibility=system||silent?Visibility.Collapsed:Visibility.Visible;systemNote.Visibility=system?Visibility.Visible:Visibility.Collapsed;listen.IsEnabled=!silent;}
            sound.SelectionChanged+=(_,_)=>{if(sound.SelectedItem is string choice){SetAudio(choice,volume);SoundState();}};SoundState();body.Children.Add(PanelCard(card,14));
        }
        if(!RingAlerts.Parse(Rules()).Any())body.Children.Add(Label("暂无提醒规则，点击添加阈值开始。"));
    }
}
