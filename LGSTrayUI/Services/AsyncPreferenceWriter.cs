using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// Callers serialize an immutable snapshot on the owner thread. Only disk I/O
// runs on the worker. One writer serializes all paths and coalesces updates
// while an earlier save is in flight.
internal sealed class AsyncPreferenceWriter
{
    internal static readonly AsyncPreferenceWriter Shared=new(RingSettingsStore.WriteAtomic);
    private readonly object gate=new();
    private readonly Dictionary<string,string> pending=new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string,string> write;
    private Task? worker;
    private Exception? failure;
    internal AsyncPreferenceWriter(Action<string,string> write){this.write=write;}
    internal void Enqueue(string path,string snapshot){
        lock(gate){
            pending[path]=snapshot;
            if(worker==null)worker=Task.Run(Drain);
        }
    }
    private void Drain(){
        while(true){
            KeyValuePair<string,string>[] batch;
            lock(gate){
                if(pending.Count==0){worker=null;return;}
                batch=new List<KeyValuePair<string,string>>(pending).ToArray();pending.Clear();
            }
            foreach(var item in batch)try{write(item.Key,item.Value);}
            catch(Exception ex){lock(gate)failure=ex;DiagnosticLogger.LogWarning("Preference write: "+ex.Message);}
        }
    }
    internal async Task<bool> FlushAsync(TimeSpan timeout){
        Task? current;lock(gate)current=worker;
        if(current!=null)try{await current.WaitAsync(timeout).ConfigureAwait(false);}catch(TimeoutException){return false;}
        lock(gate){bool ok=worker==null&&failure==null;failure=null;return ok;}
    }
}
