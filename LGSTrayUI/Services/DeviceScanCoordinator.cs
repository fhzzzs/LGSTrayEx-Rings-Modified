using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LGSTrayPrimitives;
using Windows.Gaming.Input;

namespace LGSTrayUI;

// The dispatcher owns the snapshots; workers only return completed results.
// A device-change event accelerates discovery, periodic polling remains a fallback.
internal sealed class DeviceScanCoordinator:IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Action changed;
    private readonly CancellationTokenSource stop=new();
    private readonly CancellationToken token;
    private IReadOnlyList<TaskbarBattery> ble=[],controllers=[],asus=[];
    private bool disposed,bleBusy,controllerBusy,asusBusy,eventsSubscribed;
    private DateTimeOffset nextBle,nextController,nextAsus;
    private int bleRequest,controllerRequest,asusRequest;
    private bool lastBle=true,lastGip=true,lastXInput=true;
    internal IEnumerable<TaskbarBattery> Readings=>ble.Concat(controllers).Concat(asus);
    internal DeviceScanCoordinator(Dispatcher dispatcher,Action changed)
    {
        this.dispatcher=dispatcher;this.changed=changed;token=stop.Token;
        try{
            Gamepad.GamepadAdded+=GamepadChanged;
            try{Gamepad.GamepadRemoved+=GamepadChanged;eventsSubscribed=true;}
            catch{Gamepad.GamepadAdded-=GamepadChanged;}
        }catch(Exception ex){DiagnosticLogger.LogWarning("Gamepad change registration: "+ex.Message);}
    }
    private void GamepadChanged(object? sender,Gamepad gamepad)
    {
        if(dispatcher.HasShutdownStarted)return;
        dispatcher.BeginInvoke(new Action(()=>{if(disposed)return;controllerRequest++;nextController=DateTimeOffset.MinValue;Poll(DateTimeOffset.UtcNow);}));
    }
    internal void RequestRescan()
    {
        if(disposed)return;
        bleRequest++;controllerRequest++;asusRequest++;
        nextBle=nextController=nextAsus=DateTimeOffset.MinValue;
    }
    internal void Poll(DateTimeOffset now)
    {
        if(disposed)return;
        var c=RingConfig.Current;
        if(lastBle!=c.UseBle){lastBle=c.UseBle;bleRequest++;nextBle=DateTimeOffset.MinValue;}
        if(lastGip!=c.UseGip||lastXInput!=c.UseXInput){lastGip=c.UseGip;lastXInput=c.UseXInput;controllerRequest++;nextController=DateTimeOffset.MinValue;}
        if(!c.UseBle&&ble.Count>0){ble=[];changed();}
        if(!c.UseGip&&!c.UseXInput&&controllers.Count>0){controllers=[];changed();}
        if(c.UseBle&&!bleBusy&&now>=nextBle)_=ScanBle();
        if((c.UseGip||c.UseXInput)&&!controllerBusy&&now>=nextController)_=ScanControllers();
        if(!asusBusy&&now>=nextAsus)_=ScanAsus();
    }
    private async Task ScanBle()
    {
        bleBusy=true;int generation=bleRequest;
        try{
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var rows=await BluetoothBatteryReader.ReadAsync(timeout.Token);
            if(!disposed&&RingConfig.Current.UseBle&&generation==bleRequest&&!ble.SequenceEqual(rows)){ble=rows;changed();}
        }catch(OperationCanceledException){}catch(Exception ex){DiagnosticLogger.LogWarning("BLE discovery: "+ex.Message);}
        finally{bleBusy=false;nextBle=generation!=bleRequest?DateTimeOffset.MinValue:DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(RingConfig.Current.BleSeconds,5,120));}
    }
    private async Task ScanControllers()
    {
        controllerBusy=true;int generation=controllerRequest;bool gip=lastGip,xinput=lastXInput;
        try{
            var rows=await Task.Run(()=>{
                var result=gip?GipBatteryReader.Read().ToArray():Array.Empty<TaskbarBattery>();
                return result.Length>0?result:xinput?XInputBatteryReader.Read().ToArray():Array.Empty<TaskbarBattery>();
            },token);
            if(!disposed&&generation==controllerRequest&&!controllers.SequenceEqual(rows)){controllers=rows;changed();}
        }catch(OperationCanceledException){}catch(Exception ex){DiagnosticLogger.LogWarning("Controller discovery: "+ex.Message);}
        finally{controllerBusy=false;nextController=generation!=controllerRequest?DateTimeOffset.MinValue:DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(RingConfig.Current.PollSeconds,.5,60));}
    }
    private async Task ScanAsus()
    {
        asusBusy=true;int generation=asusRequest;
        try{
            var rows=await Task.Run(AsusBatteryReader.Read,token);
            if(!disposed&&generation==asusRequest&&!asus.SequenceEqual(rows)){asus=rows;changed();}
        }catch(OperationCanceledException){}catch(Exception ex){DiagnosticLogger.LogWarning("ASUS discovery: "+ex.Message);}
        finally{asusBusy=false;nextAsus=generation!=asusRequest?DateTimeOffset.MinValue:DateTimeOffset.UtcNow.AddSeconds(10);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(eventsSubscribed){try{Gamepad.GamepadAdded-=GamepadChanged;Gamepad.GamepadRemoved-=GamepadChanged;}catch{}}
        stop.Cancel();stop.Dispose();
    }
}
