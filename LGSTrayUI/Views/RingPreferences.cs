using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;
namespace LGSTrayUI;

internal sealed class RingPreferences
{
    public int SchemaVersion {get;set;}=3;
    public Dictionary<string,string> PhysicalSourceIds {get;set;}=new();
    public Dictionary<string,string> PhysicalEvidenceIds {get;set;}=new();
    public Dictionary<string,string> PhysicalSourceEvidence {get;set;}=new();
    public Dictionary<string,string> ControllerHistoryHints {get;set;}=new();
    public string Normal {get;set;}="#34C759"; public string Low {get;set;}="#FFCC00"; public string Critical {get;set;}="#FF3B30";
    public string Offline {get;set;}="#8E8E93"; public string Icon {get;set;}="#F5F5F5"; public string Text {get;set;}="#F5F5F5";
    public bool AutoContrast {get;set;}=true; public string Track {get;set;}="#FFFFFF"; public string Charging {get;set;}="#0A84FF"; public string Background {get;set;}="#0020242C";
    public string ChargeHighlight {get;set;}="#A5E2FF"; public string ChargeBolt {get;set;}="#64D2FF";
    public bool ChargeAnimate {get;set;}=true; public bool ChargeShowBolt {get;set;}=true;
    public double ChargePeriod {get;set;}=1800; public double ChargeBrightness {get;set;}=70; public double ChargeLength {get;set;}=15;
    public double ChargeBoltScale {get;set;}=100; public double ChargeBoltX {get;set;}=0; public double ChargeBoltY {get;set;}=0; public double ChargeBoltOpacity {get;set;}=100;
    public string DividerColor {get;set;}="#31FFFFFF"; public double DividerHeight {get;set;}=26; public double DividerWidth {get;set;}=1;
    public double RingWidth {get;set;}=4; public double Gradient {get;set;}=30; public double TrackOpacity {get;set;}=7;
    public double IconScale {get;set;}=100; public double IconStroke {get;set;}=1.9; public double IconX {get;set;}=0; public double IconY {get;set;}=0;
    public double TextX {get;set;}=0; public double TextY {get;set;}=50; public double FontSize {get;set;}=14.5; public string Font {get;set;}="Segoe UI";
    public bool Bold {get;set;}=true; public bool Rounded {get;set;}=true; public double Size {get;set;}=38; public double Gap {get;set;}=4;
    public double LeftGap {get;set;}=16; public double RightGap {get;set;}=16; public double X {get;set;}=0; public double Y {get;set;}=0;
    public bool Divider {get;set;}=false; public bool Animate {get;set;}=true; public string Motion {get;set;}="Spring";
    public double Speed {get;set;}=1; public double GrayMs {get;set;}=300; public double WaitMs {get;set;}=3000; public double OutMs {get;set;}=700; public double InMs {get;set;}=500;
    public double BatteryMs {get;set;}=500; public double ColorMs {get;set;}=500; public double SpringMs {get;set;}=500; public double StaggerMs {get;set;}=75; public double Bounce {get;set;}=6;
    public double LowAt {get;set;}=20; public double CriticalAt {get;set;}=10; public bool AutoAdd {get;set;}=true; public bool AutoHide {get;set;}=true;
    public bool Alerts {get;set;}=true; public bool Sound {get;set;}=true; public string Rules {get;set;}="20:30,10:15,5:5";
    public bool ConnectionAlerts {get;set;}=true;
    public bool FullChargeAlerts {get;set;}=false;
    public bool QuietNotifications {get;set;}=false;
    public bool SuppressFullScreen {get;set;}=true;
    public double NotificationSeconds {get;set;}=4;
    public double NotificationWidth {get;set;}=300;
    public double NotificationOpacity {get;set;}=100;
    public string NotificationCorner {get;set;}="BottomRight";
    public string NotificationMonitor {get;set;}="Primary";
    public bool AutoDeduplicate {get;set;}=true;
    public Dictionary<string,string> AutomaticAliases {get;set;}=new();
    public List<string> ModelGroupRoots {get;set;}=[];
    public Dictionary<string,string> DeviceAliases {get;set;}=new();
    public Dictionary<string,string> PreferredSources {get;set;}=new();
    public List<string> IgnoredDevices {get;set;}=[];
    public List<string> SeparateDeviceSources {get;set;}=[];
    public List<string> Order {get;set;}=[]; public List<string> Hidden {get;set;}=[]; public Dictionary<string,string> Names {get;set;}=new();
    public Dictionary<string,string> DeviceRules {get;set;}=new(); public Dictionary<string,string> DeviceColors {get;set;}=new();
    public Dictionary<string,Dictionary<string,string>> DevicePalette {get;set;}=new();
    public Dictionary<string,Dictionary<int,AlertAudio>> AlertSounds {get;set;}=new();
    internal string DeviceInk(string key,string field,string fallback)=>DevicePalette.TryGetValue(key,out var map)&&map.TryGetValue(field,out var value)?value:fallback;
    private static DateTimeOffset lightChecked; private static bool lightCached;
    internal static bool SystemLight(){var now=DateTimeOffset.UtcNow;if((now-lightChecked).TotalSeconds<3)return lightCached;lightChecked=now;try{return lightCached=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0) is int n&&n!=0;}catch{return lightCached;}}
    internal string Foreground(string key,string field,bool light)=>DeviceInk(key,field,AutoContrast&&light?(field=="Track"?"#000000":"#20242C"):field=="Icon"?Icon:field=="Text"?Text:Track);
    internal AlertAudio Audio(string key,int threshold)=>AlertSounds.TryGetValue(key,out var own)&&own.TryGetValue(threshold,out var a)?a:AlertSounds.TryGetValue("",out var global)&&global.TryGetValue(threshold,out var b)?b:new AlertAudio{Sound=threshold<=5?"Alert":threshold<=10?"Bell":"Soft Ding"};
    public double PollSeconds {get;set;}=2; public double BleSeconds {get;set;}=15; public bool UseGip {get;set;}=true; public bool UseXInput {get;set;}=true; public bool UseBle {get;set;}=true;
    public bool AsusHidEnabled {get;set;}=true; public bool UseAstro {get;set;}=true; public bool UseControllerHid {get;set;}=true;
    public bool GameSirCycloneQuery {get;set;}=false;
    public bool Hover {get;set;}=true;
    internal double Ms(double n)=>Animate?Math.Clamp(n,0,10000)/Math.Clamp(Speed,.5,2):0;
    internal static Color Ink(string s, string fallback="#FFFFFF") {try{return (Color)ColorConverter.ConvertFromString(s);}catch{return (Color)ColorConverter.ConvertFromString(fallback);}}
    internal Color Accent(TaskbarBattery d){string band=d.Offline?"Offline":d.Charging?"Charging":d.Level is not int n?"Offline":n<CriticalAt?"Critical":n<LowAt?"Low":"Normal";string fallback=band switch{"Offline"=>Offline,"Charging"=>Charging,"Critical"=>Critical,"Low"=>Low,_=>Normal};if(band is not ("Offline" or "Charging") && DeviceColors.TryGetValue(d.Key,out var legacy))fallback=legacy;return Ink(DeviceInk(d.Key,band,fallback));}
}
public sealed class AlertAudio {public string Sound {get;set;}="Soft Ding";public double Volume {get;set;}=65;}
internal static class RingConfig
{
    internal static RingPreferences Current {get;private set;}=Load();
    internal static event Action? Changed;
    internal static event Action<PreferenceArea>? AreasChanged;
    private static PreferenceFingerprint? previous;
    private static bool? systemLight;
    internal static void RefreshSystemTheme(){bool light=RingPreferences.SystemLight();if(systemLight==light)return;bool initialized=systemLight.HasValue;systemLight=light;if(initialized)Changed?.Invoke();}
    private static string PathName=>Path.Combine(AppDataPaths.LocalAppDataDir,"rings-settings.json");
    private static DispatcherTimer? timer;
    private static RingPreferences Load()=>RingSettingsStore.Load(PathName);
    internal static void CaptureBaseline()=>previous=PreferenceFingerprint.Capture(Current);
    internal static void Notify(){var next=PreferenceFingerprint.Capture(Current);var areas=previous?.Diff(next)??PreferenceArea.All;previous=next;AreasChanged?.Invoke(areas);Changed?.Invoke();timer??=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};timer.Stop();timer.Tick-=SaveTick;timer.Tick+=SaveTick;timer.Start();}
    private static void SaveTick(object? s,EventArgs e){timer?.Stop();Save();}
    internal static void Save(){try{AsyncPreferenceWriter.Shared.Enqueue(PathName,RingSettingsStore.Snapshot(Current));previous=PreferenceFingerprint.Capture(Current);}catch(Exception ex){DiagnosticLogger.LogWarning("Ring settings: "+ex.Message);}}
    internal static void Flush(){timer?.Stop();Save();}
    internal static void Export(string path)=>RingSettingsStore.Save(path,Current);
    internal static void Import(string path){Current=RingSettingsStore.Parse(File.ReadAllText(path));Notify();}
    internal static void Reset(){Current=new();Notify();}
}
