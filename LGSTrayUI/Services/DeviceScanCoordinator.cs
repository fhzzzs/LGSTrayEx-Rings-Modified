using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Interop;
using LGSTrayPrimitives;
using Windows.Gaming.Input;

namespace LGSTrayUI;

// Each provider has its own schedule, cancellation and backoff. The view
// consumes snapshots but does not drive the hardware polling loop.
internal sealed class DeviceScanCoordinator:IDisposable
{
    private readonly DeviceChangeBatch deviceChanges;
    private readonly ProviderScheduler scheduler;
    private readonly GameSirPassiveInput gameSir = new();
    private bool disposed,suspended,eventsSubscribed,rawEventsSubscribed;
    internal event Action? Changed;
    internal IEnumerable<TaskbarBattery> Readings=>scheduler.Readings;
    private static Task<IReadOnlyList<TaskbarBattery>> Worker(Func<IReadOnlyList<TaskbarBattery>> read,CancellationToken token)
        =>Task.Run(read,token);
    internal DeviceScanCoordinator(Dispatcher dispatcher)
    {
        scheduler=new ProviderScheduler(new IBatteryProvider[]{
            new BatteryProvider("ble",c=>c.UseBle,c=>TimeSpan.FromSeconds(Math.Clamp(c.BleSeconds,5,120)),TimeSpan.FromSeconds(25),BluetoothBatteryReader.ReadAsync),
            new BatteryProvider("gip",c=>c.UseGip,c=>TimeSpan.FromSeconds(Math.Clamp(c.PollSeconds,.5,60)),TimeSpan.FromSeconds(5),t=>GipBatteryReader.ReadAsync(RingConfig.Current.GameSirPassiveEnabled,t)),
            new BatteryProvider("xinput",c=>c.UseXInput,c=>TimeSpan.FromSeconds(Math.Clamp(c.PollSeconds,.5,60)),TimeSpan.FromSeconds(5),t=>Worker(XInputBatteryReader.Read,t)),
            new BatteryProvider("asus",c=>c.AsusHidEnabled,c=>TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(30),t=>Worker(()=>AsusBatteryReader.Read(t),t)),
            new BatteryProvider("astro",c=>c.UseAstro,c=>TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(15),t=>Worker(()=>AsusBatteryReader.ReadAstro(t),t)),
            new BatteryProvider("controller-hid",c=>c.UseControllerHid,c=>TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(15),t=>Worker(()=>AsusBatteryReader.ReadControllerInput(t),t)),
            new BatteryProvider("gamesir-hid",c=>c.UseControllerHid&&c.GameSirPassiveEnabled,c=>TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(10),t=>Task.Run(()=>gameSir.Read(t),t))
        },dispatcher,()=>Changed?.Invoke());
        deviceChanges = new DeviceChangeBatch(dispatcher, scope =>
        {
            GameSirFastBattery.Invalidate();
            scheduler.HardwareChanged(scope);
        });
        try{Gamepad.GamepadAdded+=GamepadChanged;try{Gamepad.GamepadRemoved+=GamepadChanged;eventsSubscribed=true;}catch{Gamepad.GamepadAdded-=GamepadChanged;}}
        catch(Exception ex){DiagnosticLogger.LogWarning("Gamepad change registration: "+ex.Message);}
        try{RawGameController.RawGameControllerAdded+=RawGamepadChanged;try{RawGameController.RawGameControllerRemoved+=RawGamepadChanged;rawEventsSubscribed=true;}catch{RawGameController.RawGameControllerAdded-=RawGamepadChanged;}}
        catch(Exception ex){DiagnosticLogger.LogWarning("Raw controller change registration: "+ex.Message);}
    }
    private void GamepadChanged(object? sender,Gamepad gamepad)=>deviceChanges.Queue(DeviceChangeScope.Controllers);
    private void RawGamepadChanged(object? sender,RawGameController gamepad)=>deviceChanges.Queue(DeviceChangeScope.Controllers);
    internal void HardwareChanged()=>deviceChanges.Queue(DeviceChangeScope.All);
    internal void AttachGameSirInput(HwndSource window){gameSir.Attach(window);ApplyGameSirSettings();RingDiagnostics.GameSirInputStatus=()=>gameSir.Diagnostics;}
    private void ApplyGameSirSettings(){var c=RingConfig.Current;GameSirGipTelemetry.Instance.Enable(c.UseGip&&c.GameSirPassiveEnabled);gameSir.Enable(c.UseControllerHid&&c.GameSirPassiveEnabled);}
    internal void Suspend()
    {
        if (disposed || suspended) return;
        suspended = true;
        deviceChanges.Reset(paused: true);
        GameSirGipTelemetry.Instance.Enable(false);
        gameSir.Enable(false);
        scheduler.Suspend();
    }

    internal void Resume()
    {
        if (disposed) return;
        suspended = false;
        deviceChanges.Reset();
        GameSirFastBattery.Invalidate();
        ApplyGameSirSettings();
        scheduler.Resume();
    }

    internal void RequestRescan()
    {
        if (disposed || suspended) return;
        deviceChanges.Reset();
        GameSirFastBattery.Invalidate();
        ApplyGameSirSettings();
        scheduler.Rescan();
    }

    internal void ApplyProviderSettings()
    {
        if (disposed || suspended) return;
        ApplyGameSirSettings();
        scheduler.Poll();
    }

    public void Dispose(){if(disposed)return;disposed=true;deviceChanges.Dispose();if(eventsSubscribed){try{Gamepad.GamepadAdded-=GamepadChanged;Gamepad.GamepadRemoved-=GamepadChanged;}catch{}}if(rawEventsSubscribed){try{RawGameController.RawGameControllerAdded-=RawGamepadChanged;RawGameController.RawGameControllerRemoved-=RawGamepadChanged;}catch{}}GameSirGipTelemetry.Instance.Enable(false);RingDiagnostics.GameSirInputStatus=null;gameSir.Dispose();scheduler.Dispose();}
}
