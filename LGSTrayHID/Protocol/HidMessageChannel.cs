using LGSTrayHID.HidApi;
using LGSTrayHID.Routing;
using LGSTrayPrimitives;
using static LGSTrayHID.HidApi.HidApi;

namespace LGSTrayHID.Protocol;

// Each native handle has one owner. The actual read loop, not an async-void
// thread entry stub, signals completion. Close only after reader termination
// AND receiver disposal; a temporary read error must not close a live writer.
public class HidMessageChannel : IDisposable
{
    private sealed class Reader(HidDevicePtr device,int size){
        internal readonly HidDevicePtr Device=device;
        internal readonly int Size=size;
        internal readonly TaskCompletionSource<bool> Completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly HidMessageRouter _router;
    private readonly CancellationTokenSource _cancellationSource=new();
    private readonly object _gate=new();
    private readonly List<Reader> _readers=[];
    private readonly TaskCompletionSource<bool> _readySignal=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    private static int _activeReaders;
    public static int ActiveReaders=>Volatile.Read(ref _activeReaders);
    public HidMessageChannel(HidMessageRouter router)=>_router=router;
    public void StartReading(HidDevicePtr devShort,HidDevicePtr devLong){
        lock(_gate){
            if(_disposed)throw new ObjectDisposedException(nameof(HidMessageChannel));
            if(_readers.Count!=0)throw new InvalidOperationException("HID readers already started");
            _readers.Add(new(devShort,7));_readers.Add(new(devLong,20));
            int started=0;
            try{
                foreach(var reader in _readers){
                    var thread=new Thread(()=>ReadLoop(reader)){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name=reader.Size==7?"HID-SHORT-Reader":"HID-LONG-Reader"};
                    thread.Start();started++;
                }
                _readySignal.TrySetResult(true);
            }catch{
                for(int i=started;i<_readers.Count;i++)_readers[i].Completed.TrySetResult(true);
                _cancellationSource.Cancel();throw;
            }
        }
    }
    public Task WaitUntilReadyAsync()=>_readySignal.Task;
    private void ReadLoop(Reader reader){
        Interlocked.Increment(ref _activeReaders);
        try{
            byte[] buffer=new byte[reader.Size];var token=_cancellationSource.Token;
            int errors=0;
            while(!token.IsCancellationRequested){
                int count=reader.Device.Read(buffer,buffer.Length,250);
                if(token.IsCancellationRequested)break;
                if(count<0){
                    if(++errors>=5){DiagnosticLogger.LogWarning($"HID {reader.Size}b reader stopped after five failures");break;}
                    if(token.WaitHandle.WaitOne(500))break;
                    continue;
                }
                errors=0;
                if(count<7||count>buffer.Length)continue;
                if(DiagnosticLogger.IsVerboseEnabled&&(buffer[0]!=0x10||buffer[2]!=0))
                    DiagnosticLogger.Verbose($"DEBUG RAW RX [{reader.Size}b]: {Convert.ToHexString(buffer.AsSpan(0,count))}");
                // Router may retain the frame in its response queue.
                var frame=buffer.AsSpan(0,count).ToArray();
                try{_router.RouteMessageAsync(frame).GetAwaiter().GetResult();}
                catch(Exception ex){if(token.IsCancellationRequested)break;DiagnosticLogger.LogWarning("HID route: "+ex.Message);}
            }
        }catch(Exception ex){DiagnosticLogger.LogWarning("HID reader: "+ex.Message);}
        finally{Interlocked.Decrement(ref _activeReaders);reader.Completed.TrySetResult(true);}
    }
    public void Dispose(){
        Reader[] readers;
        lock(_gate){
            if(_disposed)return;_disposed=true;
            _cancellationSource.Cancel();readers=_readers.ToArray();_readers.Clear();
        }
        var all=Task.WhenAll(readers.Select(r=>r.Completed.Task));
        if(all.Wait(TimeSpan.FromSeconds(2)))Release(readers);
        else{
            DiagnosticLogger.LogWarning("HID reader shutdown pending; defer native close until the actual read exits.");
            _=ReleaseAfterAsync(all,readers);
        }
    }
    private async Task ReleaseAfterAsync(Task all,Reader[] readers){await all.ConfigureAwait(false);Release(readers);}
    private void Release(Reader[] readers){
        var closed=new HashSet<nint>();
        foreach(var reader in readers){
            nint handle=reader.Device;
            if(handle!=0&&closed.Add(handle))try{HidClose(reader.Device);}catch(Exception ex){DiagnosticLogger.LogWarning("HID close: "+ex.Message);}
        }
        _cancellationSource.Dispose();
        DiagnosticLogger.Log($"HID readers released; active readers={ActiveReaders}");
    }
}
