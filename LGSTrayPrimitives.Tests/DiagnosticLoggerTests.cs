using System.Diagnostics;
using LGSTrayPrimitives;

namespace LGSTrayPrimitives.Tests;

public sealed class DiagnosticLoggerTests
{
    private static DiagnosticLogEntry Entry(string message,LogSeverity severity=LogSeverity.Info,string category="UI")
        =>new(DateTimeOffset.Now,severity,category,"Test",message);
    private static string Temp()=>Directory.CreateTempSubdirectory("battery-logs-").FullName;
    private static DiagnosticLogWriter Writer(string path,int maxLines=0,long maxBytes=4*1024*1024)
        =>new(path,maxLines,"BatteryLogTest-"+Guid.NewGuid().ToString("N"),_=>{},()=>{},maxBytes);

    [Fact]
    public void DisabledInterpolationDoesNotEvaluateArguments()
    {
        DiagnosticLogger.Initialize(false,false);
        int count=0;
        DiagnosticLogger.Log($"info {++count}");
        DiagnosticLogger.Verbose($"debug {++count}");
        DiagnosticLogger.LogWarning($"warning {++count}");
        DiagnosticLogger.LogError($"error {++count}");
        Assert.Equal(0,count);
    }

    [Fact]
    public void DisabledLoggingHasNoRecurringManagedAllocation()
    {
        DiagnosticLogger.Initialize(false,false);
        for(int i=0;i<100;i++)DiagnosticLogger.Log($"sample {i}");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++){
            DiagnosticLogger.Log($"sample {i}");
            DiagnosticLogger.Verbose($"sample {i}");
        }
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.InRange(bytes,0L,1024L);
    }

    [Fact]
    public async Task ExistingLineLimitStillTrimsOldEntries()
    {
        string dir=Temp(),path=Path.Combine(dir,"diagnostic.log");
        File.WriteAllLines(path,Enumerable.Range(0,200).Select(i=>"old-"+i));
        var writer=Writer(path,20);
        try{
            for(int i=0;i<100;i++)writer.Enqueue(Entry("new-"+i,LogSeverity.Warning));
            Assert.True(await writer.StopAsync(TimeSpan.FromSeconds(10)));
            var lines=File.ReadAllLines(path);
            Assert.Equal(20,lines.Length);
            Assert.Contains("new-99",lines[^1]);
        }finally{await writer.StopAsync(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task BurstUsesBatchesAndCoalescesOnlyOrdinaryRepeatedEvents()
    {
        string dir=Temp(),path=Path.Combine(dir,"diagnostic.log");
        var writer=Writer(path);
        try{
            for(int i=0;i<100;i++)writer.Enqueue(Entry("repeat this"));
            for(int i=0;i<3;i++)writer.Enqueue(Entry("same raw report",LogSeverity.Debug,"Protocol"));
            for(int i=0;i<3;i++)writer.Enqueue(Entry("same failure",LogSeverity.Error));
            Assert.True(await writer.StopAsync(TimeSpan.FromSeconds(10)));
            string[] lines=File.ReadAllLines(path);
            Assert.Equal(106,writer.Written);
            Assert.True(writer.Batches<106);
            Assert.True(writer.Coalesced>0);
            Assert.Contains(lines,l=>l.Contains("additional times"));
            Assert.Equal(3,lines.Count(l=>l.Contains("same raw report")));
            Assert.Equal(3,lines.Count(l=>l.Contains("same failure")));
            Assert.All(lines,l=>Assert.Contains("pid=",l));
        }finally{await writer.StopAsync(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task RotationBoundsArchivesEvenWithUnlimitedLineSetting()
    {
        string dir=Temp(),path=Path.Combine(dir,"diagnostic.log");
        var writer=Writer(path,maxBytes:1024);
        try{
            for(int i=0;i<45;i++)writer.Enqueue(Entry($"{i:D2}:"+new string('x',180),LogSeverity.Warning));
            Assert.True(await writer.StopAsync(TimeSpan.FromSeconds(10)));
            Assert.True(File.Exists(path+".1"));
            Assert.False(File.Exists(path+".4"));
            Assert.All(Directory.GetFiles(dir),f=>Assert.True(new FileInfo(f).Length<=1024));
            Assert.Contains("44:",File.ReadAllText(path));
        }finally{await writer.StopAsync(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task TwoWritersShareTheSameLogWithoutInterleavingLines()
    {
        string dir=Temp(),path=Path.Combine(dir,"diagnostic.log"),mutex="BatteryLogTest-"+Guid.NewGuid().ToString("N");
        var first=new DiagnosticLogWriter(path,0,mutex,_=>{},()=>{});
        var second=new DiagnosticLogWriter(path,0,mutex,_=>{},()=>{});
        try{
            for(int i=0;i<100;i++){
                first.Enqueue(Entry("first-"+i,LogSeverity.Warning));
                second.Enqueue(Entry("second-"+i,LogSeverity.Warning));
            }
            Assert.All(await Task.WhenAll(first.StopAsync(TimeSpan.FromSeconds(10)),second.StopAsync(TimeSpan.FromSeconds(10))),ok=>Assert.True(ok));
            string[] lines=File.ReadAllLines(path);
            Assert.Equal(200,lines.Length);
            Assert.Equal(100,lines.Count(l=>l.Contains("first-")));
            Assert.Equal(100,lines.Count(l=>l.Contains("second-")));
        }finally{await first.StopAsync(TimeSpan.FromSeconds(10));await second.StopAsync(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task SaturationIsBoundedAndImportantMessagesHaveAnEmergencyFile()
    {
        string dir=Temp(),path=Path.Combine(dir,"diagnostic.log"),name="BatteryLogTest-"+Guid.NewGuid().ToString("N");
        var writer=new DiagnosticLogWriter(path,0,name,_=>{},()=>{});
        using var mutex=new Mutex(false,name);
        try{
            Assert.True(mutex.WaitOne(1000));
            try{
                for(int i=0;i<4000;i++)writer.Enqueue(Entry("ordinary-"+i));
                writer.Enqueue(Entry(new string('E',2100000),LogSeverity.Error));
                Assert.InRange(writer.Pending,0,2048);
                Assert.True(writer.Dropped>0);
                Assert.Equal(1,writer.Emergency);
            }finally{mutex.ReleaseMutex();}
            Assert.True(await writer.StopAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains(new string('E',128),File.ReadAllText(path+".emergency-"+Environment.ProcessId));
            Assert.Contains("dropped",File.ReadAllText(path));
        }finally{await writer.StopAsync(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task FailedDestinationReportsLossWithoutThrowingIntoCaller()
    {
        string dir=Temp(),path=Path.Combine(dir,"missing","diagnostic.log");
        int failures=0;
        var writer=new DiagnosticLogWriter(path,0,"BatteryLogTest-"+Guid.NewGuid().ToString("N"),_=>Interlocked.Increment(ref failures),()=>{});
        try{
            writer.Enqueue(Entry("critical",LogSeverity.Error));
            Assert.False(await writer.StopAsync(TimeSpan.FromSeconds(10)));
            Assert.True(failures>0);
            Assert.Equal(1,writer.Unwritten);
        }finally{await writer.StopAsync(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task OldArchiveIsRemovedAndRecentArchiveRetained()
    {
        string dir=Temp(),path=Path.Combine(dir,"diagnostic.log");
        File.WriteAllText(path+".1","recent");File.WriteAllText(path+".2","old");
        File.SetLastWriteTimeUtc(path+".2",DateTime.UtcNow.AddDays(-8));
        var writer=Writer(path);
        try{
            writer.Enqueue(Entry("start"));
            Assert.True(await writer.StopAsync(TimeSpan.FromSeconds(10)));
            Assert.True(File.Exists(path+".1"));Assert.False(File.Exists(path+".2"));
        }finally{await writer.StopAsync(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }
}
