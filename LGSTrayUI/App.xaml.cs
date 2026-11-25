using LGSTrayCore;
using LGSTrayCore.Interfaces;
using LGSTrayCore.Managers;
using LGSTrayPrimitives;
using LGSTrayPrimitives.Interfaces;
using LGSTrayPrimitives.IPC;
using LGSTrayUI.Extensions;
using LGSTrayUI.IconDrawing;
using LGSTrayUI.Interfaces;
using LGSTrayUI.Messages;
using LGSTrayUI.Services;
using MessagePipe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static LGSTrayUI.Extensions.AppExtensions;

namespace LGSTrayUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private bool _hasHandle = false; // Track ownership
    private IHost? _host; // DI container for accessing services
    private Task? _hostStart;
    private bool _exitRequested;
    private string? _restartExecutable;
    internal void RestartInto(string executable){if(_exitRequested)return;_restartExecutable=Path.GetFullPath(executable);RequestExit();}
    private readonly CancellationTokenSource _startupCancellation=new();
    private IEnumerable<IDeviceManager>? _deviceManagers; // Cached device managers for wake handler
    private PowerNotificationWindow? _powerWindow; // Hidden window for Modern Standby notifications
    private TaskbarBatteryWindow? _taskbarBatteries;
    private DeviceScanCoordinator? _scanner;
    private int _resumeInFlight;
    private IPublisher<SystemSuspendingMessage>? _suspendPublisher;
    private IPublisher<SystemResumingMessage>? _resumePublisher;

    /// <summary>
    /// Gets whether logging is enabled (--log flag).
    /// </summary>
    public static bool LoggingEnabled { get; private set; }

    /// <summary>
    /// Gets whether verbose logging is enabled (--verbose flag).
    /// </summary>
    public static bool VerboseLoggingEnabled { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "LGSTrayBattery_SingleInstance";

        // The 'true' here attempts to acquire ownership immediately
        _mutex = new Mutex(true, mutexName, out _hasHandle);

        if (!_hasHandle)
        {
            MessageBox.Show(
                "LGSTray is already running.",
                "LGSTray",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            Shutdown();
            return;
        }

        base.OnStartup(e);

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
        Microsoft.Win32.SystemEvents.PowerModeChanged += AppExtensions_PowerModeChanged;

        EnableEfficiencyMode();

        // STEP 1: Load configuration with validation service
        var validationService = new ConfigurationValidationService(); // Early instantiation before DI
        var builder = Host.CreateEmptyApplicationBuilder(null);
        if (!await LoadAppSettings(builder.Configuration, validationService))
        {
            Shutdown();
            return;
        }
        IConfiguration config = builder.Configuration;
        var appSettings = config.Get<AppSettings>()!;

        // STEP 2: Validate HID++ Software ID (must happen before daemon spawning)
        if (!validationService.ValidateAndEnforceSoftwareId(appSettings))
        {
            return; // Service already handled error display and shutdown
        }

        // STEP 3: Determine logging settings (config + CLI overrides)
        bool enableLogging = appSettings.Logging?.Enabled ?? false;
        bool enableVerbose = appSettings.Logging?.Verbose ?? false;

        // Parse command-line arguments for logging overrides
        if (e.Args.Contains("--log"))
        {
            enableLogging = true;
        }
        if (e.Args.Contains("--verbose"))
        {
            enableVerbose = true;
        }

        // Store in static properties for LGSTrayHIDManager
        LoggingEnabled = enableLogging;
        VerboseLoggingEnabled = enableVerbose;

        // Initialize logging
        int maxLogLines = appSettings.Logging?.MaxLines ?? 1000;
        DiagnosticLogger.WriteFailed += OnDiagnosticLogWriteFailed;
        DiagnosticLogger.Initialize(enableLogging, enableVerbose, maxLogLines);
        var buildInfo = NotifyIconViewModel.AssemblyBuildInfo;
        var buildSuffix = buildInfo.Length > 0 ? $" [{buildInfo}]" : "";
        DiagnosticLogger.Log($"LGSTray {NotifyIconViewModel.AssemblyVersion}{buildSuffix} logging started.");
        if (enableVerbose)
        {
            DiagnosticLogger.Log("Verbose logging enabled.");
        }

        // Load Windows theme based on OS version
        LoadWindowsTheme();

        // Configure battery icon drawing settings
        BatteryIconDrawing.Configure(appSettings.UI);

        // DI setup
        builder.Services.Configure<AppSettings>(config);
        builder.Services.AddSingleton(appSettings);

        // IPC, Settings
        builder.Services.AddLGSMessagePipe(true);
        builder.Services.AddSingleton<UserSettingsWrapper>();
        builder.Services.AddSingleton<ISettingsManager, TomlSettingsManager>();
        builder.Services.AddSingleton<IConfigurationValidationService, ConfigurationValidationService>();

        // Managers
        builder.Services.AddDeviceManager<LGSTrayHIDManager>(builder.Configuration);
        builder.Services.AddDeviceManager<GHubManager>(builder.Configuration);

        // UI
        builder.Services.AddSingleton<ILogiDeviceCollection, LogiDeviceCollection>();
        builder.Services.AddSingleton<ILogiDeviceIconFactory, LogiDeviceIconFactory>();
        builder.Services.AddSingleton<LogiDeviceViewModelFactory>();
        builder.Services.AddSingleton<IDispatcher, WpfDispatcher>();
        builder.Services.AddSingleton<MainTaskbarIconWrapper>();
        builder.Services.AddSingleton<NotifyIconViewModel>();
        builder.Services.AddHostedService(services => services.GetRequiredService<NotifyIconViewModel>());

        // Notifiers
        builder.Services.AddWebSocketClientFactory();
        builder.Services.AddWebserver(builder.Configuration);
        builder.Services.AddMQTTClient(builder.Configuration);
        // Rings notifications use the merged device inventory, not raw provider events.

        var host = builder.Build();
        _host = host; // Store host reference for wake handler

        // Get publishers from DI
        _suspendPublisher = host.Services.GetRequiredService<IPublisher<SystemSuspendingMessage>>();
        _resumePublisher = host.Services.GetRequiredService<IPublisher<SystemResumingMessage>>();

        // Ensure ConfigurationValidationService is instantiated to listen for startup errors
        host.Services.GetRequiredService<IConfigurationValidationService>();

        // Create hidden window for Modern Standby power notifications
        // This works on both S3 (traditional sleep) and S0 (Modern Standby) systems
        _powerWindow = new PowerNotificationWindow(this, _suspendPublisher, _resumePublisher);
        _powerWindow.Show(); // Must call Show() to initialize window handle
        _powerWindow.Hide(); // Then hide immediately

        var trayViewModel=host.Services.GetRequiredService<NotifyIconViewModel>();
        _scanner=new DeviceScanCoordinator(Dispatcher);
        _taskbarBatteries = new TaskbarBatteryWindow(trayViewModel,_scanner);
        _taskbarBatteries.Closed+=(_,_)=>_taskbarBatteries=null;
        host.Services.GetRequiredService<MainTaskbarIconWrapper>().Initialize(trayViewModel,()=>_taskbarBatteries?.ShowSettings(),()=>_taskbarBatteries?.Rescan(),()=>_taskbarBatteries?.ShowDevices(),()=>_taskbarBatteries?.OnlineCount??0);

        // WPF owns the dispatcher lifetime; stop/dispose the host explicitly.
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(
            ()=>Dispatcher.BeginInvoke(new Action(RequestExit)));
        try{
            _hostStart=host.StartAsync(_startupCancellation.Token);
            await _hostStart;
            if(!_exitRequested)UpdateTransaction.MarkReady();
        }catch(OperationCanceledException) when(_exitRequested){}
        catch(Exception ex){DiagnosticLogger.LogError("Host start failed: "+ex.Message);RequestExit();}
    }

    private void AppExtensions_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case Microsoft.Win32.PowerModes.Suspend:
                DiagnosticLogger.Log("System is suspending to sleep (SystemEvents.PowerModeChanged)");

                // Send message to suspend notifications
                _suspendPublisher?.Publish(new SystemSuspendingMessage());
                break;
            // Resume is handled exclusively by WM_POWERBROADCAST (PowerNotificationWindow),
            // which fires for both S3 and Modern Standby. PowerModeChanged.Resume would
            // trigger a duplicate daemon restart ~2s after the first one.
        }
    }

    /// <summary>
    /// Handle system wake - give USB devices time to stabilize, then rediscover.
    /// Called exclusively by WM_POWERBROADCAST (covers both S3 and Modern Standby).
    /// </summary>
    public async Task HandleSystemResumeAsync()
    {
        if(_exitRequested||Interlocked.Exchange(ref _resumeInFlight,1)!=0)return;
        try
        {
            // Wait 2 seconds for USB devices to fully wake and stabilize
            // This delay allows HID devices to enumerate and become responsive
            await Task.Delay(2000,_startupCancellation.Token);
            if(_exitRequested)return;
            await Dispatcher.InvokeAsync(()=>_taskbarBatteries?.PrepareResume());

            // Get device managers from DI container
            if (_deviceManagers == null)
            {
                var host = (Application.Current as App)?._host;
                if (host == null)
                {
                    DiagnosticLogger.LogWarning("Cannot access DI host for device manager rediscovery");
                    return;
                }
                _deviceManagers = host.Services.GetServices<IDeviceManager>();
            }

            // Trigger rediscovery on all active managers
            DiagnosticLogger.Log("Triggering rediscovery on all device managers");
            foreach (var manager in _deviceManagers)
            {
                try{await manager.RediscoverDevices().WaitAsync(TimeSpan.FromSeconds(8),_startupCancellation.Token);}
                catch(Exception ex){DiagnosticLogger.LogWarning($"Resume rediscovery {manager.GetType().Name}: {ex.Message}");}
                if(_exitRequested)return;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogError($"Error during system resume handling: {ex.Message}");
        }finally{Interlocked.Exchange(ref _resumeInFlight,0);}
    }

    internal async void RequestExit()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RequestExit));return;}
        if(_exitRequested)return;
        _exitRequested=true;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        DiagnosticLogger.Log("Fast exit requested");
        var host=_host;_host=null;
        // Cancel the owned HID supervisor before a slower G HUB shutdown can block.
        try{host?.Services.GetService<LGSTrayHIDManager>()?.RequestStop();}catch(Exception ex){DiagnosticLogger.LogWarning("HID stop signal: "+ex.Message);}
        // Hide immediately, even when final preference serialization takes time.
        _taskbarBatteries?.Hide();
        CloseUi(host);
        DiagnosticLogger.Log($"Exit UI closed in {watch.ElapsedMilliseconds} ms");
        try{_startupCancellation.Cancel();}catch(Exception ex){DiagnosticLogger.LogWarning("Startup cancellation: "+ex.Message);}
        if(host!=null){
            try{
                // Dispatcher remains responsive while hosted services finish. The HID child
                // also monitors its parent PID, so it exits if this process terminates.
                await Task.Run(async ()=>{
                    using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    try{
                        if(_hostStart!=null)try{await _hostStart.WaitAsync(stop.Token);}catch(OperationCanceledException){}catch(Exception ex){DiagnosticLogger.LogWarning("Host start during exit: "+ex.Message);}
                        await host.StopAsync(stop.Token);
                        DiagnosticLogger.Log($"Host stopped in {watch.ElapsedMilliseconds} ms");
                    }catch(Exception ex){DiagnosticLogger.LogWarning("Host stop: "+ex.Message);}
                    host.Dispose();
                    DiagnosticLogger.Log($"Host disposed in {watch.ElapsedMilliseconds} ms");
                }).WaitAsync(TimeSpan.FromSeconds(1.5));
            }catch(TimeoutException){DiagnosticLogger.LogWarning("Host cleanup exceeded 1.5 seconds; finishing application exit");}
            catch(Exception ex){DiagnosticLogger.LogWarning("Host cleanup: "+ex.Message);}
        }
        if(!await AsyncPreferenceWriter.Shared.FlushAsync(TimeSpan.FromMilliseconds(750)))DiagnosticLogger.LogWarning("Final preference write incomplete or failed");
        DiagnosticLogger.Log($"Exit total before shutdown: {watch.ElapsedMilliseconds} ms");
        Shutdown();
    }
    private void CloseUi(IHost? host)
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= AppExtensions_PowerModeChanged;
        try{_taskbarBatteries?.Close();}catch(Exception ex){DiagnosticLogger.LogWarning("Ring window cleanup: "+ex.Message);}
        _taskbarBatteries=null;
        try{_scanner?.Dispose();}catch(Exception ex){DiagnosticLogger.LogWarning("Scanner cleanup: "+ex.Message);}_scanner=null;
        PrimaryNotificationPopup.CloseAll();
        // The WPF tray icon must be disposed on its dispatcher, not a worker.
        try{host?.Services.GetService<MainTaskbarIconWrapper>()?.Dispose();}catch(Exception ex){DiagnosticLogger.LogWarning("Tray cleanup: "+ex.Message);}
        try{_powerWindow?.Close();}catch(InvalidOperationException){}
        _powerWindow=null;
    }
    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLogger.Log("Application exiting...");
        var host=_host;_host=null;
        try{host?.Services.GetService<LGSTrayHIDManager>()?.RequestStop();}catch{}
        CloseUi(host);
        if(!_exitRequested)try{AsyncPreferenceWriter.Shared.FlushAsync(TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();}catch{}
        // Session shutdown can bypass RequestExit. Never synchronously wait on
        // background disposal from a dispatcher that is already shutting down.
        if(host!=null)_=Task.Run(async()=>{try{using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(2));await host.StopAsync(stop.Token);host.Dispose();}catch{}});
        // ONLY release if we actually acquired it in OnStartup
        if (_hasHandle)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // Fail silently during exit if the handle is already gone
            }
        }

        _mutex?.Dispose();
        if(_restartExecutable is string executable)try{
            var start=new System.Diagnostics.ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!};
            start.ArgumentList.Add("--wait-parent="+Environment.ProcessId);
            if(File.Exists(UpdateTransaction.MarkerPath(Path.GetDirectoryName(executable)!)))
                start.ArgumentList.Add("--update-from="+PortableLayout.CurrentEntry);
            if(LoggingEnabled)start.ArgumentList.Add("--log");
            System.Diagnostics.Process.Start(start);
        }catch(Exception ex){DiagnosticLogger.LogWarning("Restart failed: "+ex.Message);}
        DiagnosticLogger.Log("Application exit complete");
        base.OnExit(e);
    }

    static async Task<bool> LoadAppSettings(ConfigurationManager config, IConfigurationValidationService validationService)
    {
        return await validationService.LoadAndValidateConfiguration(config);
    }

    /// <summary>
    /// Surfaces a diagnostic-log write failure to the user. Logging is opt-in, so a silent
    /// failure (e.g. a read-only install folder) would otherwise leave the user staring at a
    /// missing log file. Fired at most once per failure streak by <see cref="DiagnosticLogger"/>.
    /// </summary>
    private void OnDiagnosticLogWriteFailed(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            MessageBox.Show(
                $"{message}\n\nCheck that the folder is writable, or move the installation to a " +
                "user-writable location.",
                "LGSTray - Diagnostic Logging",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        });
    }

    private void CrashHandler(object sender, UnhandledExceptionEventArgs args)
    {
        try
        {
            if (args.ExceptionObject is Exception ex)
            {
                // Suppress ObjectDisposedException from MessagePipe's NamedPipeWorker during shutdown.
                // This is a known race condition: the CTS is disposed before RunPublishLoop exits.
                if (ex is ObjectDisposedException ode
                    && ode.StackTrace?.Contains("NamedPipeWorker") == true)
                {
                    DiagnosticLogger.Log("Suppressed NamedPipeWorker ObjectDisposedException during shutdown");
                    return;
                }

                string crashFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LGSTrayUI_Crash.txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[{DateTime.Now}] CRASH OCCURRED");

                // Recursively log exceptions
                Exception? currentEx = ex;
                int depth = 0;
                while (currentEx != null)
                {
                    string prefix = depth == 0 ? "Exception" : $"Inner Exception [{depth}]";
                    sb.AppendLine($"{prefix}: {currentEx.GetType().Name}: {currentEx.Message}");
                    sb.AppendLine($"Stack Trace:\n{currentEx.StackTrace}");

                    if (currentEx is AggregateException aggEx)
                    {
                        sb.AppendLine($"Flattened AggregateException Details:");
                        foreach (var inner in aggEx.Flatten().InnerExceptions)
                        {
                            sb.AppendLine($"- {inner.GetType().Name}: {inner.Message}");
                        }
                    }

                    currentEx = currentEx.InnerException;
                    depth++;
                    if (currentEx != null) sb.AppendLine();
                }

                sb.AppendLine("\nFull ToString():");
                sb.AppendLine(ex.ToString());
                sb.AppendLine("--------------------------------------------------");

                File.AppendAllText(crashFile, sb.ToString());
            }
        }
        catch
        {
            // If logging fails, there's not much we can do.
        }
    }

    /// <summary>
    /// Loads the Fluent WPF theme for the modern Windows 11 UI.
    /// </summary>
    private void LoadWindowsTheme()
    {
        try
        {
#pragma warning disable WPF0001
            ThemeMode = System.Windows.ThemeMode.System;
#pragma warning restore WPF0001
            DiagnosticLogger.Log("Loaded Fluent theme");
        }
        catch (Exception ex)
        {
            // Non-fatal: log error and continue with classic theme
            DiagnosticLogger.LogWarning($"Failed to load Fluent theme, using classic theme: {ex.Message}");
        }
    }
}

/// <summary>
/// Hidden window for receiving power management notifications.
/// Required for Modern Standby (S0) compatibility via RegisterSuspendResumeNotification.
/// Also works with traditional S3 sleep for backwards compatibility.
/// </summary>
internal class PowerNotificationWindow : Window
{
    private IntPtr _notificationHandle = IntPtr.Zero;
    private readonly App _app;
    private readonly IPublisher<SystemSuspendingMessage> _suspendPublisher;
    private readonly IPublisher<SystemResumingMessage> _resumePublisher;

    // P/Invoke declarations for power management
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterSuspendResumeNotification(IntPtr hRecipient, int flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterSuspendResumeNotification(IntPtr handle);

    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_APMSUSPEND = 0x0004;

    public PowerNotificationWindow(
        App app,
        IPublisher<SystemSuspendingMessage> suspendPublisher,
        IPublisher<SystemResumingMessage> resumePublisher)
    {
        _app = app;
        _suspendPublisher = suspendPublisher;
        _resumePublisher = resumePublisher;

        // Create hidden window
        Width = 0;
        Height = 0;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Visibility = Visibility.Hidden;

        // Important: Don't call Show() - we just need the window to exist for message handling
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            // Get window handle and add WndProc hook
            var hwndHelper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(hwndHelper.Handle);

            if (source != null)
            {
                source.AddHook(WndProc);

                // Register for suspend/resume notifications (required for Modern Standby S0)
                _notificationHandle = RegisterSuspendResumeNotification(hwndHelper.Handle, DEVICE_NOTIFY_WINDOW_HANDLE);

                if (_notificationHandle != IntPtr.Zero)
                {
                    DiagnosticLogger.Log("Power notification window registered successfully (Modern Standby + S3 compatible)");
                }
                else
                {
                    DiagnosticLogger.LogWarning("Failed to register for power notifications - wake detection may not work");
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogError($"Error initializing power notification window: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_POWERBROADCAST)
        {
            int powerEvent = (int)wParam;

            if (powerEvent == PBT_APMSUSPEND)
            {
                DiagnosticLogger.Log("System suspending (WM_POWERBROADCAST)");

                // Send message to suspend notifications
                _suspendPublisher.Publish(new SystemSuspendingMessage());
            }
            else if (powerEvent == PBT_APMRESUMEAUTOMATIC)
            {
                DiagnosticLogger.Log("System resumed (WM_POWERBROADCAST)");

                // Send message to resume notifications
                _resumePublisher.Publish(new SystemResumingMessage());

                // Call the app's resume handler asynchronously
                _ = _app.HandleSystemResumeAsync();
            }
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unregister power notifications
        if (_notificationHandle != IntPtr.Zero)
        {
            UnregisterSuspendResumeNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
            DiagnosticLogger.Log("Power notifications unregistered");
        }

        base.OnClosed(e);
    }
}
