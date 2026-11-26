using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("LGSTrayPrimitives.Tests")]
namespace LGSTrayPrimitives;

internal enum LogSeverity { Debug,Info,Warning,Error }
internal readonly record struct DiagnosticLogEntry(DateTimeOffset Time,LogSeverity Severity,string Category,string Caller,string Message)
{
    internal bool Critical=>Severity>=LogSeverity.Warning;
    internal long Characters=>(long)Message.Length+Caller.Length+Category.Length+128;
    internal bool CanCoalesce=>!Critical&&Category!="Protocol";
    internal bool SameEvent(DiagnosticLogEntry other)=>CanCoalesce&&Severity==other.Severity&&Category==other.Category&&Caller==other.Caller&&Message==other.Message;
    private string LevelLabel=>Severity switch{LogSeverity.Debug=>"VERBOSE",LogSeverity.Info=>"INFO",LogSeverity.Warning=>"WARNING",_=>"ERROR"};
    internal string Format()=> $"[{Time:yyyy-MM-dd HH:mm:ss.fff}] [{Caller}] [{LevelLabel}] [{Category}] [pid={Environment.ProcessId}]: {Message}";
}

// Bounded producer queue. Only the worker uses the shared named mutex and main
// log file; the mutex is never held across await or while notifying subscribers.
internal sealed class DiagnosticLogWriter
{
    private const int Capacity=2048,Reserve=128,BatchSize=128,TrimCheckInterval=100;
    private const long CharacterCapacity=2*1024*1024,CriticalReserve=256*1024;
    private const long DefaultMaxBytes=4*1024*1024;
    private const int ArchiveCount=3;
    private static readonly UTF8Encoding Utf8=new(false);
    private readonly object gate=new(),emergencyGate=new();
    private readonly Queue<DiagnosticLogEntry> queue=new(128);
    private readonly SemaphoreSlim signal=new(0,1);
    private readonly string path,mutexName,emergencyPath;
    private readonly int maxLines;
    private readonly long maxBytes;
    private readonly Action<string> failure;
    private readonly Action recovery;
    private readonly Task worker;
    private bool accepting=true;
    private long queuedCharacters,dropped,emergency,unwritten,written,batches,coalesced,persistedDropped,maintenanceFailures;
    private DateTime nextCleanupUtc;
    private int writesSinceTrim;
    private bool initialized;
    internal long Dropped=>Interlocked.Read(ref dropped);
    internal long Emergency=>Interlocked.Read(ref emergency);
    internal long Unwritten=>Interlocked.Read(ref unwritten);
    internal long Written=>Interlocked.Read(ref written);
    internal long Batches=>Interlocked.Read(ref batches);
    internal long Coalesced=>Interlocked.Read(ref coalesced);
    internal int Pending {get{lock(gate)return queue.Count;}}
    internal string Metrics=>$"pending={Pending}; written={Written}; batches={Batches}; coalesced={Coalesced}; dropped={Dropped}; emergency={Emergency}; unwritten={Unwritten}; maintenance failures={Interlocked.Read(ref maintenanceFailures)}";
    internal DiagnosticLogWriter(string path,int maxLines,string mutexName,Action<string> failure,Action recovery,long maxBytes=DefaultMaxBytes)
    {
        this.path=path;this.maxLines=maxLines;this.mutexName=mutexName;this.failure=failure;this.recovery=recovery;
        this.maxBytes=Math.Max(1024,maxBytes);
        emergencyPath=path+".emergency-"+Environment.ProcessId;
        worker=Task.Run(RunAsync);
    }
    internal void Enqueue(DiagnosticLogEntry entry)
    {
        lock(gate){
            int limit=entry.Critical?Capacity:Capacity-Reserve;
            long charLimit=entry.Critical?CharacterCapacity:CharacterCapacity-CriticalReserve;
            if(accepting&&queue.Count<limit&&entry.Characters<=charLimit-queuedCharacters){
                queue.Enqueue(entry);queuedCharacters+=entry.Characters;
                if(signal.CurrentCount==0)signal.Release();
                return;
            }
        }
        if(entry.Critical)EmergencyWrite(entry);
        else{
            Interlocked.Increment(ref dropped);
            lock(gate)if(accepting&&signal.CurrentCount==0)signal.Release();
        }
    }
    internal async Task<bool> StopAsync(TimeSpan timeout)
    {
        lock(gate){
            if(accepting){accepting=false;if(signal.CurrentCount==0)signal.Release();}
        }
        try{await worker.WaitAsync(timeout).ConfigureAwait(false);return Unwritten==0&&Dropped==Interlocked.Read(ref persistedDropped);}
        catch(TimeoutException){return false;}
        catch(Exception ex){NotifyFailure(ex.Message);return false;}
    }
    private async Task RunAsync()
    {
        try{
            using var mutex=new Mutex(false,mutexName);
            var batch=new List<DiagnosticLogEntry>(BatchSize);
            long reportedDropped=0;
            while(true){
                await signal.WaitAsync().ConfigureAwait(false);
                // Batch bursts, but never run an idle periodic timer.
                bool delay;lock(gate)delay=accepting;
                if(delay)await Task.Delay(35).ConfigureAwait(false);
                while(true){
                    batch.Clear();
                    bool stopped;
                    lock(gate){
                        while(batch.Count<BatchSize&&queue.TryDequeue(out var entry)){
                            queuedCharacters-=entry.Characters;batch.Add(entry);
                        }
                        stopped=!accepting&&queue.Count==0;
                    }
                    long loss=Dropped;
                    if(batch.Count==0&&loss==reportedDropped){if(stopped)return;break;}
                    string? error=null;
                    bool ok=false;
                    for(int retry=0;retry<3;retry++){
                        bool acquired=false;
                        try{
                            try{acquired=mutex.WaitOne(200);}catch(AbandonedMutexException){acquired=true;}
                            if(acquired){
                                WriteBatch(batch,loss-reportedDropped);
                                ok=true;
                            }else error="Shared log mutex timed out.";
                        }catch(Exception ex){error=ex.Message;break;}
                        finally{if(acquired)mutex.ReleaseMutex();}
                        if(ok)break;
                        if(retry<2)await Task.Delay(40).ConfigureAwait(false);
                    }
                    if(ok){reportedDropped=loss;Interlocked.Exchange(ref persistedDropped,loss);try{recovery();}catch{}}
                    else{
                        foreach(var entry in batch){
                            if(entry.Critical)EmergencyWrite(entry);
                            else Interlocked.Increment(ref dropped);
                        }
                        // Preserve the pending loss count until a later successful write.
                        // Do not retry an empty queue endlessly if the directory is unavailable.
                        NotifyFailure(error??"Log batch write failed.");
                        if(stopped)return;
                        lock(gate)if(queue.Count==0)break;
                    }
                    if(stopped){
                        lock(gate)if(queue.Count==0)return;
                    }
                }
            }
        }catch(Exception ex){
            // Drain anything that was accepted before an unexpected worker failure.
            List<DiagnosticLogEntry> remaining=new();
            lock(gate){accepting=false;while(queue.TryDequeue(out var entry))remaining.Add(entry);queuedCharacters=0;}
            foreach(var entry in remaining){if(entry.Critical)EmergencyWrite(entry);else Interlocked.Increment(ref dropped);}
            NotifyFailure(ex.Message);
        }
    }
    private void WriteBatch(List<DiagnosticLogEntry> batch,long lost)
    {
        var now=DateTime.UtcNow;
        if(now>=nextCleanupUtc){
            nextCleanupUtc=now.AddDays(1);
            try{CleanupArchives();}catch{Interlocked.Increment(ref maintenanceFailures);}
        }
        if(!initialized){
            try{TrimLines();}catch{Interlocked.Increment(ref maintenanceFailures);}
            initialized=true;
        }
        StreamWriter? output=null;
        long size=0;
        int physicalLines=0;
        try{
            void Open(){
                var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete,16384);
                size=stream.Length;
                try{output=new StreamWriter(stream,Utf8,8192);}catch{stream.Dispose();throw;}
            }
            void WriteLine(string line){
                long bytes=Utf8.GetByteCount(line)+Utf8.GetByteCount(Environment.NewLine);
                if(output==null)Open();
                if(size>0&&size+bytes>maxBytes){
                    output!.Dispose();output=null;Rotate();Open();
                }
                output!.WriteLine(line);
                size+=bytes;physicalLines++;
#if DEBUG
                try{Console.WriteLine(line);}catch{}
#endif
            }
            if(lost>0)WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [WARNING] [Logging] [pid={Environment.ProcessId}]: {lost} low-priority log entries dropped by bounded queue or write failure.");
            for(int i=0;i<batch.Count;){
                var first=batch[i];int next=i+1;
                while(next<batch.Count&&first.SameEvent(batch[next]))next++;
                WriteLine(first.Format());
                if(next-i>1){
                    WriteLine($"[{batch[next-1].Time:yyyy-MM-dd HH:mm:ss.fff}] [INFO] [Logging] [pid={Environment.ProcessId}]: previous event repeated {next-i-1} additional times.");
                    Interlocked.Add(ref coalesced,next-i-1);
                }
                i=next;
            }
        }finally{output?.Dispose();}
        Interlocked.Add(ref written,batch.Count);
        Interlocked.Increment(ref batches);
        writesSinceTrim+=physicalLines;
        if(maxLines>0&&writesSinceTrim>=TrimCheckInterval){
            writesSinceTrim=0;
            // A maintenance failure does not turn an already persisted batch into lost data.
            try{TrimLines();}catch{Interlocked.Increment(ref maintenanceFailures);}
        }
    }
    private void Rotate()
    {
        for(int i=ArchiveCount;i>=2;i--){
            string previous=path+"."+(i-1);
            if(File.Exists(previous))File.Move(previous,path+"."+i,true);
        }
        if(File.Exists(path))File.Move(path,path+".1",true);
    }
    private void CleanupArchives()
    {
        var cutoff=DateTime.UtcNow.AddDays(-7);
        for(int i=1;i<=ArchiveCount;i++){
            string archive=path+"."+i;
            if(File.Exists(archive)&&File.GetLastWriteTimeUtc(archive)<cutoff)File.Delete(archive);
        }
        // Clean only our own named emergency artifacts; never touch user files.
        string? dir=Path.GetDirectoryName(path);
        if(string.IsNullOrEmpty(dir)||!Directory.Exists(dir))return;
        string prefix=Path.GetFileName(path)+".emergency-";
        foreach(var file in Directory.EnumerateFiles(dir,prefix+"*")){
            string suffix=Path.GetFileName(file)[prefix.Length..];
            if(suffix.EndsWith(".1",StringComparison.Ordinal))suffix=suffix[..^2];
            if(int.TryParse(suffix,out int pid)&&pid!=Environment.ProcessId&&File.GetLastWriteTimeUtc(file)<cutoff)
                try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private void TrimLines()
    {
        if(!File.Exists(path))return;
        if(new FileInfo(path).Length>maxBytes){Rotate();return;}
        if(maxLines<=0)return;
        var tail=new Queue<string>();long lines=0;
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,Utf8,true)){
            while(reader.ReadLine() is string line){
                if(tail.Count==maxLines)tail.Dequeue();
                tail.Enqueue(line);lines++;
            }
        }
        if(lines<=maxLines)return;
        string temp=path+".trim-"+Environment.ProcessId;
        try{File.WriteAllLines(temp,tail,Utf8);File.Move(temp,path,true);}
        finally{try{File.Delete(temp);}catch{}}
    }
    // Only exceptional saturation or an inaccessible shared log reaches this
    // synchronous path. Preserve the complete warning/error in a separate
    // process-specific file; report explicitly if even that cannot be written.
    private void EmergencyWrite(DiagnosticLogEntry entry)
    {
        string? error=null;
        try{
            lock(emergencyGate){
                if(File.Exists(emergencyPath)&&new FileInfo(emergencyPath).Length>=maxBytes)
                    File.Move(emergencyPath,emergencyPath+".1",true);
                File.AppendAllText(emergencyPath,entry.Format()+Environment.NewLine,Utf8);
                Interlocked.Increment(ref emergency);
            }
        }catch(Exception ex){
            Interlocked.Increment(ref unwritten);
            error=ex.Message;
            try{Console.Error.WriteLine(entry.Format());}catch{}
        }
        NotifyFailure(error==null?"Important log diverted to "+emergencyPath:"Important log could not be persisted: "+error);
    }
    private void NotifyFailure(string message){try{failure(message);}catch{}}
}
