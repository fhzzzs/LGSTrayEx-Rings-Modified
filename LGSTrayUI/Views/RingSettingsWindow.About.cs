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
    private void BuildAbout(){var card=new StackPanel();card.Children.Add(new TextBlock{Text="Battery Monitor",FontSize=26,FontWeight=FontWeights.SemiBold});card.Children.Add(Label(NotifyIconViewModel.AssemblyVersion+" · 非官方修改版"));card.Children.Add(new TextBlock{Text="Modified by fhzzzs",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,16,0,4)});card.Children.Add(GithubLink("github.com/fhzzzs","https://github.com/fhzzzs"));body.Children.Add(PanelCard(card));body.Children.Add(PanelCard(new ReleaseUpdatePanel()));var credits=new StackPanel();TitleLine(credits,"原项目与许可证");credits.Children.Add(Label("Community fork · strain08"));credits.Children.Add(GithubLink("LGSTrayEx","https://github.com/strain08/LGSTrayEx"));credits.Children.Add(Label("Original project · andyvorld"));credits.Children.Add(GithubLink("LGSTrayBattery","https://github.com/andyvorld/LGSTrayBattery"));credits.Children.Add(Label("GPL-3.0 · 保留原作者版权及许可。此修改版不代表原作者背书。"));body.Children.Add(PanelCard(credits));}
    private FrameworkElement GithubLink(string label,string url){var link=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(label)){NavigateUri=new Uri(url)};link.RequestNavigate+=(_,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message);}e.Handled=true;};return new TextBlock(link){Margin=new Thickness(0,4,0,8)};}
}
