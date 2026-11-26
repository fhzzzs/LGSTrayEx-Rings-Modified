using System.Runtime.CompilerServices;

namespace LGSTrayPrimitives;

// Existing call sites keep their string API. Interpolated calls select the
// handler overload and skip evaluating arguments when logging is disabled.
public static class DiagnosticLogger
{
    private static readonly object gate=new();
    private static readonly string _logFilePath=Path.Combine(AppDataPaths.LocalAppDataDir,"diagnostic.log");
    private static DiagnosticLogWriter? writer;
    private static volatile bool verbose;
    private static string? lastWriteError;
    private static int failureReported;
    public static string LogFilePath=>_logFilePath;
    public static string? LastWriteError=>Volatile.Read(ref lastWriteError);
    public static event Action<string>? WriteFailed;
    public static bool IsEnabled=>Volatile.Read(ref writer)!=null;
    public static bool IsVerboseEnabled=>verbose&&IsEnabled;
    public static string Metrics=>Volatile.Read(ref writer)?.Metrics??"disabled";
    static DiagnosticLogger()=>AppDomain.CurrentDomain.ProcessExit+=(_,_)=>Shutdown(TimeSpan.FromMilliseconds(1500));

    public static void Initialize(bool enableLogging,bool enableVerbose,int maxLines=1000)
    {
        lock(gate){
            var previous=Interlocked.Exchange(ref writer,null);
            verbose=false;
            if(previous!=null&&!previous.StopAsync(TimeSpan.FromMilliseconds(1500)).GetAwaiter().GetResult()){
                ReportFailure("Previous log writer did not finish within the shutdown deadline.");
                return;
            }
            Volatile.Write(ref lastWriteError,null);
            Interlocked.Exchange(ref failureReported,0);
            if(!enableLogging&&!enableVerbose)return;
            try{
                Volatile.Write(ref writer,new DiagnosticLogWriter(_logFilePath,maxLines,"LOG_WRITE",ReportFailure,ReportRecovery));
                verbose=enableVerbose;
            }catch(Exception ex){ReportFailure(ex.Message);}
        }
    }

    public static void Log(string message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
        =>Write(LogSeverity.Info,message,callerMember,file);
    public static void Verbose(string message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
        =>Write(LogSeverity.Debug,message,callerMember,file);
    public static void LogWarning(string message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
        =>Write(LogSeverity.Warning,message,callerMember,file);
    public static void LogError(string message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
        =>Write(LogSeverity.Error,message,callerMember,file);

    public static void Log(ref LogMessageHandler message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
    {if(message.Enabled)Log(message.GetText(),callerMember,file);}
    public static void Verbose(ref VerboseLogMessageHandler message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
    {if(message.Enabled)Verbose(message.GetText(),callerMember,file);}
    public static void LogWarning(ref LogMessageHandler message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
    {if(message.Enabled)LogWarning(message.GetText(),callerMember,file);}
    public static void LogError(ref LogMessageHandler message,[CallerMemberName]string callerMember="",[CallerFilePath]string file="")
    {if(message.Enabled)LogError(message.GetText(),callerMember,file);}

    private static void Write(LogSeverity level,string message,string caller,string file)
    {
        var sink=Volatile.Read(ref writer);
        if(sink==null||level==LogSeverity.Debug&&!verbose)return;
        sink.Enqueue(new(DateTimeOffset.Now,level,Category(file),caller,message??""));
    }
    internal static string Category(string file)
    {
        if(file.Contains("LGSTrayHID",StringComparison.OrdinalIgnoreCase)||
           file.Contains("Protocol",StringComparison.OrdinalIgnoreCase)||
           file.Contains("BatteryReader",StringComparison.OrdinalIgnoreCase)||
           file.Contains("GameSir",StringComparison.OrdinalIgnoreCase))return "Protocol";
        if(file.Contains("Diagnostics",StringComparison.OrdinalIgnoreCase)||
           file.Contains("Scheduler",StringComparison.OrdinalIgnoreCase)||
           file.Contains("FrameClock",StringComparison.OrdinalIgnoreCase))return "Performance";
        if(file.Contains("Device",StringComparison.OrdinalIgnoreCase))return "Device";
        if(file.Contains("LGSTrayUI",StringComparison.OrdinalIgnoreCase))return "UI";
        return "Core";
    }
    private static void ReportRecovery()=>Interlocked.Exchange(ref failureReported,0);
    private static void ReportFailure(string error)
    {
        Volatile.Write(ref lastWriteError,error);
        if(Interlocked.Exchange(ref failureReported,1)!=0)return;
        string message=$"Diagnostic logging: '{_logFilePath}': {error}";
        try{Console.Error.WriteLine(message);}catch{}
        try{WriteFailed?.Invoke(message);}catch{}
    }
    // Safe to call on the WPF dispatcher: the writer never waits on that dispatcher.
    public static bool Shutdown(TimeSpan timeout)
    {
        DiagnosticLogWriter? sink;
        lock(gate){verbose=false;sink=Interlocked.Exchange(ref writer,null);}
        if(sink==null)return true;
        bool ok=sink.StopAsync(timeout).GetAwaiter().GetResult();
        if(!ok)ReportFailure("Log shutdown did not drain successfully before the deadline.");
        return ok;
    }
}

[InterpolatedStringHandler]
public ref struct LogMessageHandler
{
    private DefaultInterpolatedStringHandler inner;
    private readonly bool enabled;
    public LogMessageHandler(int literalLength,int formattedCount,out bool shouldAppend)
    {
        enabled=shouldAppend=DiagnosticLogger.IsEnabled;
        inner=enabled?new DefaultInterpolatedStringHandler(literalLength,formattedCount):default;
    }
    public void AppendLiteral(string value)=>inner.AppendLiteral(value);
    public void AppendFormatted<T>(T value)=>inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value,string? format)=>inner.AppendFormatted(value,format);
    public void AppendFormatted<T>(T value,int alignment)=>inner.AppendFormatted(value,alignment);
    public void AppendFormatted<T>(T value,int alignment,string? format)=>inner.AppendFormatted(value,alignment,format);
    public void AppendFormatted(ReadOnlySpan<char> value)=>inner.AppendFormatted(value);
    public void AppendFormatted(ReadOnlySpan<char> value,int alignment=0,string? format=null)=>inner.AppendFormatted(value,alignment,format);
    public bool Enabled=>enabled;
    public string GetText()=>enabled?inner.ToStringAndClear():"";
}

[InterpolatedStringHandler]
public ref struct VerboseLogMessageHandler
{
    private DefaultInterpolatedStringHandler inner;
    private readonly bool enabled;
    public VerboseLogMessageHandler(int literalLength,int formattedCount,out bool shouldAppend)
    {
        enabled=shouldAppend=DiagnosticLogger.IsVerboseEnabled;
        inner=enabled?new DefaultInterpolatedStringHandler(literalLength,formattedCount):default;
    }
    public void AppendLiteral(string value)=>inner.AppendLiteral(value);
    public void AppendFormatted<T>(T value)=>inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value,string? format)=>inner.AppendFormatted(value,format);
    public void AppendFormatted<T>(T value,int alignment)=>inner.AppendFormatted(value,alignment);
    public void AppendFormatted<T>(T value,int alignment,string? format)=>inner.AppendFormatted(value,alignment,format);
    public void AppendFormatted(ReadOnlySpan<char> value)=>inner.AppendFormatted(value);
    public void AppendFormatted(ReadOnlySpan<char> value,int alignment=0,string? format=null)=>inner.AppendFormatted(value,alignment,format);
    public bool Enabled=>enabled;
    public string GetText()=>enabled?inner.ToStringAndClear():"";
}
