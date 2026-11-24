using LGSTrayPrimitives;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LGSTrayUI;

public partial class TaskbarBatteryWindow : Window
{
    private readonly NotifyIconViewModel _tray;
    private readonly HashSet<LogiDeviceViewModel> _watched = [];
    private readonly DispatcherTimer _positionTimer;
    private nint _handle;
    private TaskbarPlacement? _position;
    private bool _refreshPending;
    private bool _closed;
    private ContextMenu? _openMenu;
    private readonly Dictionary<string, TaskbarBattery> _remembered = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _knownPath = Path.Combine(AppDataPaths.LocalAppDataDir, "taskbar-devices.json");
    private IReadOnlyList<TaskbarBattery> _bluetooth = Array.Empty<TaskbarBattery>();
    private IReadOnlyList<TaskbarBattery> _controllers = Array.Empty<TaskbarBattery>();
    private bool _scanningBluetooth;
    private IReadOnlyList<TaskbarBattery> _asus=Array.Empty<TaskbarBattery>();
    private bool _scanningAsus;
    private DateTimeOffset _nextAsus;
    private DateTimeOffset _nextBluetooth;
    private DateTimeOffset _nextControllers;
    private readonly RingStripAnimator _animator;
    private RingSettingsWindow? _settings;
    private readonly RingAlerts _alerts = new();
    private readonly GroupedConnectionTracker _connections = new(DateTimeOffset.UtcNow);
    private DeviceGroup[] _groups = [];
    private TaskbarBattery[] _rows = [];

    public TaskbarBatteryWindow(NotifyIconViewModel tray)
    {
        _tray = tray;
        InitializeComponent();
        _animator = new RingStripAnimator(RingItems, CreateRingButton, UpdatePlacement);
        RingConfig.Changed += SettingsChanged;
        _handle = new WindowInteropHelper(this).EnsureHandle();
        TaskbarPlacement.MakeToolWindow(_handle);
        HwndSource.FromHwnd(_handle)?.AddHook(HotkeyHook); RegisterHotKey(_handle,1001,0x4003,0x42);
        _tray.LogiDevices.CollectionChanged += DevicesChanged;
        LoadKnownDevices();
        WatchDevices();
        RefreshReadings();
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += Reposition;
        _positionTimer.Start();
        Reposition(null, EventArgs.Empty);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RegisterHotKey(nint h,int id,uint mods,uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint h,int id);
    private nint HotkeyHook(nint hwnd,int msg,nint wp,nint lp,ref bool handled){if(msg==0x0312&&wp==1001){ShowSettings();handled=true;}return 0;}
    private void DevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WatchDevices();
        QueueRefresh();
    }

    private void WatchDevices()
    {
        foreach (var device in _watched)
            device.PropertyChanged -= DeviceChanged;
        _watched.Clear();
        foreach (var device in _tray.LogiDevices)
            if (_watched.Add(device)) device.PropertyChanged += DeviceChanged;
    }

    private void DeviceChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_refreshPending || _closed) return;
        _refreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(RefreshReadings));
    }

    private void RefreshReadings()
    {
        _refreshPending = false;
        if (_closed) return;
        var c=RingConfig.Current;
        var logi=TaskbarBattery.ReadSources(_tray.LogiDevices);
        var current=logi.Concat(_bluetooth).Concat(_controllers).Concat(_asus)
            .GroupBy(d=>d.Key,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>DeviceIdentityManager.Best(g,null),StringComparer.Ordinal);
        bool inventoryChanged=false;
        // Migrate legacy synthetic model IDs only when a unique source can be identified.
        // Keep ambiguous legacy configuration on disk, but never display a fake model record.
        var migrated=new HashSet<string>();
        foreach(var key in _remembered.Keys.Where(k=>k.StartsWith("logi:",StringComparison.Ordinal)).ToArray()){
            var matches=current.Keys.Where(k=>k.StartsWith(key+":",StringComparison.Ordinal)).ToArray();
            if(matches.Length==0)continue;
            if(matches.Length==1){DeviceIdentityManager.Inherit(c,key,matches[0]);if(c.Hidden.Contains(key)&&!c.Hidden.Contains(matches[0]))c.Hidden.Add(matches[0]);migrated.Add(matches[0]);}
            _remembered.Remove(key);inventoryChanged=true;
        }
        var previousKeys=_groups.Select(g=>g.Reading.Key).ToHashSet(StringComparer.Ordinal);
        var previousRaw=_remembered.Keys.ToHashSet(StringComparer.Ordinal);
        foreach(var d in current.Values){
            if(!_remembered.TryGetValue(d.Key,out var old)||old.Name!=d.Name||old.Type!=d.Type||old.PhysicalId!=d.PhysicalId)inventoryChanged=true;
            _remembered[d.Key]=d;
        }
        foreach(var key in _remembered.Keys.Where(k=>!current.ContainsKey(k)).ToArray())
            _remembered[key]=_remembered[key] with{Level=null,Charging=false,Status="离线",ShortLabel=null};
        var previousAliases=new Dictionary<string,string>(c.AutomaticAliases);
        int previousModelRoots=c.ModelGroupRoots.Count;
        _groups=DeviceIdentityManager.Group(_remembered.Values,c);
        if(previousAliases.Count!=c.AutomaticAliases.Count||previousAliases.Any(p=>!c.AutomaticAliases.TryGetValue(p.Key,out var v)||v!=p.Value)||previousModelRoots!=c.ModelGroupRoots.Count)inventoryChanged=true;
        foreach(var group in _groups){
            var key=group.Reading.Key;
            if(group.Sources.All(d=>!previousRaw.Contains(d.Key)&&!migrated.Contains(d.Key))&&!previousKeys.Contains(key)){
                if(!c.AutoAdd&&!c.Hidden.Contains(key))c.Hidden.Add(key);
                if(c.Order.Count>0&&!c.Order.Contains(key))c.Order.Add(key);
            }
        }
        // XInput is a fallback slot, not a permanent hardware identity. Do not resurrect
        // obsolete fallback slots in the main list while GIP is the active provider.
        // The raw records are still available for explicit linking or cleanup.
        bool gipActive=_controllers.Any(d=>!d.Offline&&d.Key.StartsWith("gip:",StringComparison.Ordinal));
        var rows=_groups.Where(g=>!g.Ignored&&!(c.AutoDeduplicate&&gipActive&&g.Sources.All(d=>d.Offline&&d.Key.StartsWith("xinput:",StringComparison.Ordinal))))
            .Select(g=>g.Reading).Select(d=>c.Names.TryGetValue(d.Key,out var name)&&!string.IsNullOrWhiteSpace(name)?d with{Name=name}:d).ToList();
        _rows=rows.ToArray();
        if(inventoryChanged){SaveKnownDevices();RingConfig.Save();}
        _animator.Update(rows);
        foreach(var child in RingItems.Children) if(child is Button b) ToolTipService.SetIsEnabled(b,RingConfig.Current.Hover);
        CheckNotifications();
        if (_settings != null) _settings.UpdateDevices(rows,_groups);
    }
    private void SettingsChanged(){ _connections.Suppress(DateTimeOffset.UtcNow,TimeSpan.FromSeconds(2)); RefreshReadings(); _animator.RefreshLayout(); }
    private void UpdatePlacement(){ if (_closed) return; Dispatcher.BeginInvoke(new Action(()=>Reposition(null,EventArgs.Empty))); }
    internal void ShowSettings(){ if(_openMenu!=null)_openMenu.IsOpen=false; if(_settings!=null){_settings.Activate();return;} _settings=new RingSettingsWindow(_tray, _rows, _groups, ForgetKnown); _settings.Closed+=(_,_)=>_settings=null; _settings.Show(); _settings.Activate(); }
    internal int OnlineCount=>_rows.Count(d=>!d.Offline);
    internal void ShowDevices(){ShowSettings();_settings?.OpenDevices();}
    private void ForgetKnown(string key){var group=_groups.FirstOrDefault(g=>g.Reading.Key==key);if(group==null||!group.Reading.Offline)return;var active=TaskbarBattery.ReadSources(_tray.LogiDevices).Concat(_bluetooth).Concat(_controllers).Concat(_asus).Select(d=>d.Key).ToHashSet();foreach(var d in group.Sources)if(!active.Contains(d.Key))_remembered.Remove(d.Key);SaveKnownDevices();QueueRefresh();}
    private static string Normalize(string name){var n=new string(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());if(n.Contains("G735"))return "G735";if(n.Contains("HARPEACEMINI")&&!n.Contains("HARPEII"))return "HARPEACEMINI";return n;}
    private static void MoveTo(UIElementCollection children, UIElement item, int position)
    {
        int previous = children.IndexOf(item);
        if (previous == position) return;
        if (previous >= 0) children.RemoveAt(previous);
        children.Insert(position, item);
    }
    private sealed record KnownDevice(DeviceType Type, string Name, string Key, string? PhysicalId=null);
    private void LoadKnownDevices()
    {
        try
        {
            if (!File.Exists(_knownPath)) return;
            foreach (var item in JsonSerializer.Deserialize<KnownDevice[]>(File.ReadAllText(_knownPath)) ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Key) || TaskbarBattery.Rank(item.Type) >= 4) continue;
                _remembered[item.Key] = new(item.Type, item.Name, null, "离线", false, DateTimeOffset.MinValue, item.Key, PhysicalId:item.PhysicalId);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogWarning($"Taskbar inventory load: {ex.Message}"); }
    }
    private void SaveKnownDevices()
    {
        try
        {
            var inventory = _remembered.Values.Select(d => new KnownDevice(d.Type, d.Name, d.Key,d.PhysicalId)).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_knownPath)!);
            string temp = _knownPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(inventory));
            File.Move(temp, _knownPath, true);
        }
        catch (Exception ex) { DiagnosticLogger.LogWarning($"Taskbar inventory save: {ex.Message}"); }
    }
    private async Task ScanBluetoothAsync()
    {
        _scanningBluetooth = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            _bluetooth = await BluetoothBatteryReader.ReadAsync(timeout.Token);
            if (!_closed) QueueRefresh();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagnosticLogger.LogWarning($"BLE discovery: {ex.Message}"); }
        finally { _scanningBluetooth = false; _nextBluetooth = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(RingConfig.Current.BleSeconds,5,120)); }
    }
    private async Task ScanAsusAsync(){
        _scanningAsus=true;
        try{var readings=await Task.Run(AsusBatteryReader.Read);if(!_closed){_asus=readings;QueueRefresh();}}
        catch(Exception ex){DiagnosticLogger.LogWarning("ASUS battery: "+ex.Message);if(!_closed){_asus=Array.Empty<TaskbarBattery>();QueueRefresh();}}
        finally{_scanningAsus=false;_nextAsus=DateTimeOffset.UtcNow.AddSeconds(10);}
    }
    private void PollExtraDevices()
    {
        var now = DateTimeOffset.UtcNow;
        // ROG OMNI is an automatic hardware provider, not a user preference.
        if(!_scanningAsus&&now>=_nextAsus)_=ScanAsusAsync();
        if (now >= _nextControllers)
        {
            _nextControllers = now.AddSeconds(Math.Clamp(RingConfig.Current.PollSeconds,.5,60));
            var c=RingConfig.Current; var gip=c.UseGip?GipBatteryReader.Read().ToArray():Array.Empty<TaskbarBattery>();
            // Avoid double enumeration when WGI sees gamepads; XInput is fallback only.
            var readings = gip.Length>0?gip:c.UseXInput?XInputBatteryReader.Read().ToArray():Array.Empty<TaskbarBattery>();
            if (!_controllers.SequenceEqual(readings)) { _controllers = readings; QueueRefresh(); }
        }
        if (!RingConfig.Current.UseBle) { if (_bluetooth.Count>0) { _bluetooth=Array.Empty<TaskbarBattery>(); QueueRefresh(); } }
        else if (!_scanningBluetooth && now >= _nextBluetooth) _ = ScanBluetoothAsync();
    }

    private void CheckNotifications()
    {
        var now=DateTimeOffset.UtcNow;
        if(!_tray.RediscoverDevicesEnabled)_connections.Suppress(now,TimeSpan.FromSeconds(5));
        var c=RingConfig.Current;
        foreach(var change in _connections.Observe(_groups.Where(g=>!g.Ignored),now))
        {
            if(!c.ConnectionAlerts)continue;
            var d=change.Device;
            string name=c.Names.TryGetValue(d.Key,out var custom)&&!string.IsNullOrWhiteSpace(custom)?custom:d.Name;
            PrimaryNotificationPopup.Show(name,change.Online?"已连接"+(d.Level is int n?$" · {n}%":""):"已断开连接",
                change.Online?"#356A95":"#9A520C",TimeSpan.FromSeconds(4),deviceKey:d.Key);
        }
        if(!_connections.IsSuppressed(now))_alerts.Check(_rows);
    }

    private void Reposition(object? sender, EventArgs e)
    {
        PollExtraDevices();
        CheckNotifications();
        NativeTaskbarSlot.Instance.Update(_animator.Width);
        if (_openMenu?.IsOpen == true) {
            return;
        }
        if (!TaskbarPlacement.TryReadCustom(out var placement, _animator.Width))
        {
            if (IsVisible)
            {
                if(_openMenu!=null)_openMenu.IsOpen=false;
                Hide();
            }
            _position = null;
            return;
        }
        bool changed = _position != placement;
        if (changed)
        {
            if(_openMenu!=null)_openMenu.IsOpen=false;
            Width = placement.LogicalWidth;
            Height = placement.LogicalHeight;

            _position = placement;
        }
        if (!IsVisible)
        {
            placement.Apply(_handle);
            Show();
            TaskbarPlacement.MakeToolWindow(_handle);
        }
        // Explorer can raise its own topmost window while the taskbar bounds stay the same.
        // Do not raise the rings over a context menu or detail panel.
        if (_openMenu?.IsOpen != true)
            placement.Apply(_handle);
    }

    private Button CreateRingButton()
    {
        var b=new Button { Style=(Style)FindResource("RingButton"), Focusable=false, IsTabStop=false };
        // Left click intentionally has no action. Right click uses the native WPF context menu.
        var menu=BatteryMenu.Create(_tray, ShowSettings, Rescan, ShowDevices, ()=>_rows.Count(d=>!d.Offline));
        menu.PlacementTarget=b;menu.Placement=PlacementMode.Top;
        b.ContextMenu=menu;
        menu.Opened+=(_,_)=>_openMenu=menu;
        menu.Closed+=(_,_)=>{if(ReferenceEquals(_openMenu,menu))_openMenu=null;};
        b.Unloaded+=(_,_)=>{if(menu.IsOpen)menu.IsOpen=false;};
        return b;
    }
    internal void Rescan(){_connections.Suppress(DateTimeOffset.UtcNow,TimeSpan.FromSeconds(15));_nextBluetooth=_nextControllers=_nextAsus=DateTimeOffset.MinValue;PollExtraDevices();if(_tray.RediscoverDevicesCommand.CanExecute(null))_tray.RediscoverDevicesCommand.Execute(null);}

    private void ForgetOffline(object sender, RoutedEventArgs e)
    {
        // Explicit cleanup of previously discovered, now absent devices (not active provider rows).
        var activeKeys = TaskbarBattery.ReadSources(_tray.LogiDevices).Select(d => d.Key)
            .Concat(_bluetooth.Select(d => d.Key)).Concat(_controllers.Select(d => d.Key)).Concat(_asus.Select(d=>d.Key)).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _remembered.Where(p => p.Value.Offline && !activeKeys.Contains(p.Key)).Select(p => p.Key).ToArray())
            _remembered.Remove(key);
        SaveKnownDevices();
        QueueRefresh();
    }


    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        NativeTaskbarSlot.Instance.Dispose();
        UnregisterHotKey(_handle,1001); HwndSource.FromHwnd(_handle)?.RemoveHook(HotkeyHook);
        RingConfig.Changed -= SettingsChanged; _animator.Dispose(); _settings?.Close(); RingConfig.Save();
        _stop.Cancel();
        _stop.Dispose();
        _positionTimer.Stop();
        _positionTimer.Tick -= Reposition;
        _tray.LogiDevices.CollectionChanged -= DevicesChanged;
        foreach (var device in _watched) device.PropertyChanged -= DeviceChanged;
        if(_openMenu!=null)_openMenu.IsOpen=false;
        base.OnClosed(e);
    }
}
