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
    private readonly PhysicalDeviceCenter _deviceCenter=new(DateTimeOffset.UtcNow);
    private Dictionary<string,TaskbarBattery> _remembered=>_deviceCenter.Inventory;
    private readonly DeviceScanCoordinator _scanner;
    private readonly string _knownPath=Path.Combine(AppDataPaths.LocalAppDataDir,"taskbar-devices.json");
    private bool _placementReady,_placementQueued;
    private readonly RingStripAnimator _animator;
    private RingSettingsWindow? _settings;
    private readonly RingAlerts _alerts = new();

    private DeviceGroup[] _groups = [];
    private TaskbarBattery[] _rows = [];

    public TaskbarBatteryWindow(NotifyIconViewModel tray)
    {
        _tray = tray;
        InitializeComponent();
        _scanner=new DeviceScanCoordinator(Dispatcher,QueueRefresh);
        _animator = new RingStripAnimator(RingItems, CreateRingButton, UpdatePlacement){PlacementReady=()=>_placementReady};
        RingConfig.Changed += SettingsChanged;
        _handle = new WindowInteropHelper(this).EnsureHandle();
        TaskbarPlacement.MakeToolWindow(_handle);
        HwndSource.FromHwnd(_handle)?.AddHook(HotkeyHook); RegisterHotKey(_handle,1001,0x4003,0x42);
        _tray.LogiDevices.CollectionChanged += DevicesChanged;
        foreach(var d in DeviceInventoryStore.Load(_knownPath))_remembered[d.Key]=d;
        WatchDevices();
        RefreshReadings();
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += Reposition;
        _positionTimer.Start();
        Reposition(null, EventArgs.Empty);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RegisterHotKey(nint h,int id,uint mods,uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint h,int id);
    private nint HotkeyHook(nint hwnd,int msg,nint wp,nint lp,ref bool handled){
        if(msg==0x0312&&wp==1001){ShowSettings();handled=true;}
        if(msg==0x0219&&(wp==7||wp==0x8000||wp==0x8004))_scanner.RequestRescan();
        return 0;
    }
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
        bool changed=_deviceCenter.RefreshInventory(TaskbarBattery.ReadSources(_tray.LogiDevices).Concat(_scanner.Readings),c);
        if(changed){SaveKnownDevices();RingConfig.Save();}
        ApplySnapshot(ReadSnapshot(),true);
    }
    private void SettingsChanged(){ _deviceCenter.Suppress(DateTimeOffset.UtcNow,TimeSpan.FromSeconds(2)); RefreshReadings(); _animator.RefreshLayout(); }
    private void UpdatePlacement(){if(_closed)return;_placementReady=false;if(_placementQueued)return;_placementQueued=true;Dispatcher.BeginInvoke(new Action(()=>{_placementQueued=false;Reposition(null,EventArgs.Empty);}));}
    internal void ShowSettings(){ if(_openMenu!=null)_openMenu.IsOpen=false; if(_settings!=null){_settings.Activate();return;} _settings=new RingSettingsWindow(_tray, _rows, _groups, ForgetKnown); _settings.Closed+=(_,_)=>_settings=null; _settings.Show(); _settings.Activate(); }
    internal int OnlineCount=>_rows.Count(d=>!d.Offline);
    internal void ShowDevices(){ShowSettings();_settings?.OpenDevices();}
    private void ForgetKnown(string key){var group=_groups.FirstOrDefault(g=>g.Reading.Key==key);if(group==null||!group.Reading.Offline)return;var active=TaskbarBattery.ReadSources(_tray.LogiDevices).Concat(_scanner.Readings).Select(d=>d.Key).ToHashSet();foreach(var d in group.Sources)if(!active.Contains(d.Key))_remembered.Remove(d.Key);SaveKnownDevices();QueueRefresh();}
    private void SaveKnownDevices(){try{DeviceInventoryStore.Save(_knownPath,_remembered.Values);}catch(Exception ex){DiagnosticLogger.LogWarning("Device inventory save: "+ex.Message);}}
    private void PollExtraDevices()=>_scanner.Poll(DateTimeOffset.UtcNow);

    private PhysicalDeviceSnapshot ReadSnapshot()
    {
        var now=DateTimeOffset.UtcNow;
        if(!_tray.RediscoverDevicesEnabled)_deviceCenter.Suppress(now,TimeSpan.FromSeconds(5));
        return _deviceCenter.Advance(RingConfig.Current,now);
    }
    private void ApplySnapshot(PhysicalDeviceSnapshot snapshot,bool force=false)
    {
        var c=RingConfig.Current;
        bool changed=force||!_rows.SequenceEqual(snapshot.Rows);
        _groups=snapshot.Groups;_rows=snapshot.Rows;
        if(changed){
            _animator.Update(_rows);
            foreach(var child in RingItems.Children)if(child is Button b)ToolTipService.SetIsEnabled(b,c.Hover);
            _settings?.UpdateDevices(_rows,_groups);
        }
        foreach(var change in snapshot.Changes){
            if(!c.ConnectionAlerts||!RingNotificationPolicy.CanNotify(c))continue;
            var d=change.Device;
            string name=c.Names.TryGetValue(d.Key,out var custom)&&!string.IsNullOrWhiteSpace(custom)?custom:d.Name;
            PrimaryNotificationPopup.Show(name,change.Online?"已连接"+(d.Level is int n?$" · {n}%":""):"已断开连接",
                change.Online?"#356A95":"#9A520C",TimeSpan.FromSeconds(c.NotificationSeconds),deviceKey:d.Key);
        }
        _alerts.Check(_rows,snapshot.Suppressed);
    }

    private void Reposition(object? sender, EventArgs e)
    {
        if(_closed)return;
        PollExtraDevices();
        ApplySnapshot(ReadSnapshot());
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
            _position = null;_placementReady=false;
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
        if (_openMenu?.IsOpen != true){
            placement.Apply(_handle);
            _placementReady=true;
            _animator.ConfirmPlacement();
        }
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
    internal void Rescan(){_deviceCenter.Suppress(DateTimeOffset.UtcNow,TimeSpan.FromSeconds(15));_scanner.RequestRescan();PollExtraDevices();if(_tray.RediscoverDevicesCommand.CanExecute(null))_tray.RediscoverDevicesCommand.Execute(null);}

    private void ForgetOffline(object sender, RoutedEventArgs e)
    {
        // Explicit cleanup of previously discovered, now absent devices (not active provider rows).
        var activeKeys = TaskbarBattery.ReadSources(_tray.LogiDevices).Concat(_scanner.Readings).Select(d=>d.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _remembered.Where(p => p.Value.Offline && !activeKeys.Contains(p.Key)).Select(p => p.Key).ToArray())
            _remembered.Remove(key);
        SaveKnownDevices();
        QueueRefresh();
    }


    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        UnregisterHotKey(_handle,1001); HwndSource.FromHwnd(_handle)?.RemoveHook(HotkeyHook);
        RingConfig.Changed -= SettingsChanged; _animator.Dispose(); _settings?.Close(); RingConfig.Flush();
        _scanner.Dispose();
        _positionTimer.Stop();
        _positionTimer.Tick -= Reposition;
        _tray.LogiDevices.CollectionChanged -= DevicesChanged;
        foreach (var device in _watched) device.PropertyChanged -= DeviceChanged;
        if(_openMenu!=null)_openMenu.IsOpen=false;
        base.OnClosed(e);
    }
}
