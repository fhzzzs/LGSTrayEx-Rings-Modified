[CmdletBinding()]
param([switch]$NoOpen,[switch]$Test,[ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$RingsVersion='2.0.0')
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { throw 'Windows x64 required.' }
function Test-Sdk($exe) {
  if (!$exe -or !(Test-Path $exe -PathType Leaf)) { return $false }
  $sdks = & $exe --list-sdks
  return ($LASTEXITCODE -eq 0 -and [bool]($sdks -match '^10\.'))
}
$local=Join-Path $PSScriptRoot '.sdk-x64'
$dotnet=Join-Path $local 'dotnet.exe'
$system=Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'
if (Test-Sdk $system) { $dotnet=$system }
elseif (!(Test-Sdk $dotnet)) {
  Write-Host 'No .NET 10 x64 SDK found. Downloading SDK into this project only (source build)...'
  New-Item -ItemType Directory -Force $local | Out-Null
  $script=Join-Path $local 'dotnet-install.ps1'
  Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Channel 10.0 -Architecture x64 -InstallDir $local -NoPath
  if ($LASTEXITCODE -ne 0 -or !(Test-Sdk $dotnet)) { throw 'SDK installation failed.' }
}
$env:DOTNET_ROOT=Split-Path $dotnet -Parent
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'

$version=$RingsVersion
if($Test) {
  & $dotnet test LGSTrayBattery.sln -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
}
$out=Join-Path $PSScriptRoot 'bin\Release\Publish\win-x64\Standalone'
$zip=Join-Path $PSScriptRoot 'bin\Battery-Monitor-Windows-x64.zip'
# Remove only the known generated output folder. Never remove user settings or SDK.
if(Test-Path $out){Remove-Item -LiteralPath $out -Recurse -Force}
if(Test-Path $zip){Remove-Item -LiteralPath $zip -Force}
$app=Join-Path $out 'app'
New-Item -ItemType Directory -Force $app | Out-Null
foreach($project in @('LGSTrayHID','LGSTrayUI')) {
  # Publish both self-contained executables into the same private app folder.
  & $dotnet publish "$project\$project.csproj" -c Release -r win-x64 --self-contained true /p:PublishProfile=Standalone /p:Version=$version /p:PublishTrimmed=false /p:PublishSingleFile=true -o "$app"
  if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
foreach($file in @('LGSTray.exe','LGSTrayHID.exe','hidapi.dll','appsettings.toml')) {
  if (!(Test-Path (Join-Path $app $file) -PathType Leaf)) { throw "Missing portable file: $file" }
}
# Keep internal assembly identities, but give the published single-file executables their public names.
Move-Item -LiteralPath (Join-Path $app 'LGSTray.exe') -Destination (Join-Path $app 'Battery Monitor.exe')
Move-Item -LiteralPath (Join-Path $app 'LGSTrayHID.exe') -Destination (Join-Path $app 'Battery Monitor HID.exe')
# Keep debug symbols out of the end-user package; source builds still generate them.
Get-ChildItem -LiteralPath $app -Filter '*.pdb' -Recurse -File | Remove-Item -Force
$docs=Join-Path $app 'docs'
New-Item -ItemType Directory -Force $docs | Out-Null
Copy-Item LICENSE,MODIFICATIONS.md,INSTALL.md,DEVICE-COMPATIBILITY.md,MEASURE-BATTERY-MONITOR.ps1 -Destination $docs -Force
@'
Battery Monitor (Windows 11 x64)
Extract the complete ZIP, then launch the outer Battery Monitor.exe.
Keep the app folder beside the launcher. The .NET 10 desktop runtime and HID worker are bundled.
Configuration: app\appsettings.toml. User settings/logs: %LOCALAPPDATA%\LGSTray.
Unofficial fork of strain08/LGSTrayEx, originally andyvorld/LGSTrayBattery; modified by fhzzzs.
Source and license: see LICENSE and MODIFICATIONS.md in this folder.
'@ | Set-Content (Join-Path $docs 'START-HERE.txt') -Encoding UTF8
# Windows 11 includes .NET Framework 4.x. Compile a tiny launcher using its inbox
# compiler; no duplicate .NET 10 runtime and no C++/Explorer build dependency.
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path $csc -PathType Leaf)){throw 'Windows .NET Framework compiler missing.'}
$entry=Join-Path $out 'Battery Monitor.exe'
$icon=Join-Path $PSScriptRoot 'LGSTrayUI\Resources\BatteryMonitor.ico'
$launcher=Join-Path $PSScriptRoot 'tools\PortableLauncher\Program.cs'
$metadata=Join-Path $env:TEMP ('battery-monitor-assembly-'+[Guid]::NewGuid().ToString('N')+'.cs')
@"
using System.Reflection;
[assembly: AssemblyTitle("Battery Monitor")]
[assembly: AssemblyProduct("Battery Monitor")]
[assembly: AssemblyDescription("Wireless device battery monitor; unofficial fork of LGSTrayEx and LGSTrayBattery")]
[assembly: AssemblyCopyright("Original authors: andyvorld and strain08; modifications by fhzzzs")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
"@ | Set-Content -LiteralPath $metadata -Encoding UTF8
& $csc /nologo /target:winexe /platform:x64 /optimize+ /debug- "/win32icon:$icon" "/out:$entry" $launcher $metadata
$launcherExit=$LASTEXITCODE
Remove-Item -LiteralPath $metadata -Force
if($launcherExit -ne 0 -or !(Test-Path $entry -PathType Leaf)){throw 'Portable launcher build failed.'}
$rootItems=@(Get-ChildItem -LiteralPath $out -Force)
if($rootItems.Count -ne 2 -or !(Test-Path $app -PathType Container)){throw 'Unexpected portable root layout.'}
# Launch from a different working directory, verify forwarding and dependency
# resolution without needing a desktop session or a connected peripheral.
$probe=Start-Process -FilePath $entry -ArgumentList '--package-check' -WorkingDirectory $env:TEMP -PassThru
if(!$probe.WaitForExit(35000)){try{$probe.Kill()}catch{};throw 'Portable launcher smoke test timed out.'}
if($probe.ExitCode -ne 0){throw "Portable launcher smoke test failed: $($probe.ExitCode)"}
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
Write-Host "Portable package: $zip"
if(!$NoOpen) { Invoke-Item $out }
