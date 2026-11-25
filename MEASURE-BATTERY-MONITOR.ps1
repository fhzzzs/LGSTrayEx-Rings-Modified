[CmdletBinding()]
param(
  [ValidateRange(1,168)][int]$Hours=168,
  [ValidateRange(5,3600)][int]$IntervalSeconds=60,
  [string]$Output=(Join-Path (Get-Location) ("battery-monitor-performance-"+(Get-Date -Format yyyyMMdd-HHmmss)+".csv"))
)
$ErrorActionPreference='Stop'
if(-not ('BatteryMonitorMetrics' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class BatteryMonitorMetrics {
 [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr h,uint flags);
}
'@
}
$stop=(Get-Date).AddHours($Hours)
Write-Host "Sampling Battery Monitor UI and HID worker. CSV: $Output. Ctrl+C to stop."
while((Get-Date) -lt $stop){
  $now=[DateTimeOffset]::UtcNow.ToString('o')
  $rows=@()
  foreach($p in @(Get-Process -Name 'Battery Monitor','Battery Monitor HID','LGSTray','LGSTrayHID' -ErrorAction SilentlyContinue)){
    try {
      $rows += [pscustomobject]@{
        UTC=$now; Process=$p.ProcessName; PID=$p.Id; StartUTC=$p.StartTime.ToUniversalTime().ToString('o');
        WorkingSetMiB=[math]::Round($p.WorkingSet64/1MB,2); PrivateMiB=[math]::Round($p.PrivateMemorySize64/1MB,2);
        PeakWorkingSetMiB=[math]::Round($p.PeakWorkingSet64/1MB,2); Threads=$p.Threads.Count; Handles=$p.HandleCount;
        GDI=[BatteryMonitorMetrics]::GetGuiResources($p.Handle,0); USER=[BatteryMonitorMetrics]::GetGuiResources($p.Handle,1);
        CPUSeconds=[math]::Round($p.TotalProcessorTime.TotalSeconds,2); Status='running'
      }
    } catch { Write-Warning "Process $($p.Id) ended or could not be sampled: $_" }
    finally { $p.Dispose() }
  }
  if($rows.Count -eq 0){$rows=@([pscustomobject]@{UTC=$now; Process=''; PID=''; StartUTC=''; WorkingSetMiB=''; PrivateMiB=''; PeakWorkingSetMiB=''; Threads=''; Handles=''; GDI=''; USER=''; CPUSeconds=''; Status='not running'})}
  $rows | Export-Csv -LiteralPath $Output -NoTypeInformation -Append -Encoding UTF8
  Start-Sleep -Seconds $IntervalSeconds
}
Write-Host "Sampling completed: $Output"
