using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LGSTrayPrimitives;
using Windows.Gaming.Input;

namespace LGSTrayUI;

// Each provider has its own schedule, cancellation and backoff. The view
// consumes snapshots but does not drive the hardware polling loop.
internal sealed class DeviceScanCoordinator:IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly ProviderScheduler scheduler;
    private bool disposed,eventsSubscribed,rawEventsSubscribed;
    private int gamepadRescanQueued;
    internal event Action? Changed;
    internal IEnumerable<TaskbarBattery> Readings=>scheduler.Readings;
    private static Task<IReadOnlyList<TaskbarBattery>> Worker(Func<IEnumerable<TaskbarBattery>> read,CancellationToken token)
        =>Task.Run<IReadOnlyList<TaskbarBattery>>(()=>read().ToArray(),token);
    internal DeviceScanCoordinator(Dispatcher dispatcher)
    {
        this.dispatcher=dispatcher;
        scheduler=new ProviderScheduler(new IBatteryProvider[]{
            new BatteryProvider("ble",c=>c.UseBle,c=>TimeSpan.FromSeconds(Math.Clamp(c.BleSeconds,5,120)),TimeSpan.FromSeconds(25),BluetoothBatteryReader.ReadAsync),
            new BatteryProvider("gip",c=>c.UseGip,c=>TimeSpan.FromSeconds(Math.Clamp(c.PollSeconds,.5,60)),TimeSpan.FromSeconds(5),t=>Worker(GipBatteryReader.Read,t)),
            new BatteryProvider("xinput",c=>c.UseXInput,c=>TimeSpan.FromSeconds(Math.Clamp(c.PollSeconds,.5,60)),TimeSpan.FromSeconds(5),t=>Worker(XInputBatteryReader.Read,t)),
            new BatteryProvider("asus",c=>c.UseAsus,c=>TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(30),t=>Worker(()=>AsusBatteryReader.Read(t),t)),
            new BatteryProvider("astro",c=>c.UseAstro,c=>TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(15),t=>Worker(()=>AsusBatteryReader.ReadAstro(t),t)),
            new BatteryProvider("controller-hid",c=>c.UseControllerHid,c=>TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(15),t=>Worker(()=>AsusBatteryReader.ReadControllerInput(t),t))
        },dispatcher,()=>Changed?.Invoke());
        try{Gamepad.GamepadAdded+=GamepadChanged;try{Gamepad.GamepadRemoved+=GamepadChanged;eventsSubscribed=true;}catch{Gamepad.GamepadAdded-=GamepadChanged;}}
        catch(Exception ex){DiagnosticLogger.LogWarning("Gamepad change registration: "+ex.Message);}
        try{RawGameController.RawGameControllerAdded+=RawGamepadChanged;try{RawGameController.RawGameControllerRemoved+=RawGamepadChanged;rawEventsSubscribed=true;}catch{RawGameController.RawGameControllerAdded-=RawGamepadChanged;}}
        catch(Exception ex){DiagnosticLogger.LogWarning("Raw controller change registration: "+ex.Message);}
    }
    private void GamepadChanged(object? sender,Gamepad gamepad)=>QueueGamepadRescan();
    private void RawGamepadChanged(object? sender,RawGameController gamepad)=>QueueGamepadRescan();
    private void QueueGamepadRescan()
    {
        if(dispatcher.HasShutdownStarted||Interlocked.Exchange(ref gamepadRescanQueued,1)!=0)return;
        dispatcher.BeginInvoke(new Action(()=>{Interlocked.Exchange(ref gamepadRescanQueued,0);if(disposed)return;scheduler.Rescan("gip");scheduler.Rescan("xinput");scheduler.Rescan("controller-hid");}));
    }
    internal void RequestRescan(){if(!disposed)scheduler.Rescan();}
    internal void Poll(DateTimeOffset now){if(!disposed)scheduler.Poll();}
    public void Dispose(){if(disposed)return;disposed=true;if(eventsSubscribed){try{Gamepad.GamepadAdded-=GamepadChanged;Gamepad.GamepadRemoved-=GamepadChanged;}catch{}}if(rawEventsSubscribed){try{RawGameController.RawGameControllerAdded-=RawGamepadChanged;RawGameController.RawGameControllerRemoved-=RawGamepadChanged;}catch{}}scheduler.Dispose();}
}
