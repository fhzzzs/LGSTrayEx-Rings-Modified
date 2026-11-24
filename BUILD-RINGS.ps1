[CmdletBinding()]
param([switch]$NoOpen,[switch]$Test)
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
$version='3.2.6-rings.9'
if($Test) {
  & $dotnet test LGSTrayBattery.sln -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
}
$out=Join-Path $PSScriptRoot 'bin\Release\Publish\win-x64\Standalone'
$zip=Join-Path $PSScriptRoot 'bin\LGSTrayEx-Rings-Portable-x64.zip'
# Remove only the known generated output folder. Never remove user settings or SDK.
if(Test-Path $out){Remove-Item -LiteralPath $out -Recurse -Force}
if(Test-Path $zip){Remove-Item -LiteralPath $zip -Force}
foreach($project in @('LGSTrayHID','LGSTrayUI')) {
  # The HID worker and UI share a portable directory. Self-contained explicitly bundles Desktop Runtime.
  & $dotnet publish "$project\$project.csproj" -c Release -r win-x64 --self-contained true /p:PublishProfile=Standalone /p:Version=$version /p:PublishTrimmed=false /p:PublishSingleFile=true
  if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
# Native SDK is needed ONLY for a source build. The resulting DLL statically links
# the C++ runtime, and the portable archive includes it automatically.
function Find-VcVars {
  $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
  if (!(Test-Path $vswhere)) { return $null }
  $path=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
  if (!$path) { return $null }
  $vc=Join-Path ($path | Select-Object -First 1) 'VC\Auxiliary\Build\vcvars64.bat'
  $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
  $headers=Get-ChildItem $sdk -Directory -ErrorAction SilentlyContinue | Where-Object {
    (Test-Path (Join-Path $_.FullName 'um\xamlom.h')) -and
    (Test-Path (Join-Path $_.FullName 'cppwinrt\winrt\Windows.UI.Xaml.h'))
  }
  if ((Test-Path $vc) -and $headers) { return $vc }
  return $null
}
$vcvars=Find-VcVars
if (!$vcvars) {
  Write-Host 'Native layout source build needs MSVC x64 and Windows SDK (developer only).'
  Write-Host 'Portable users do not install any build tools.'
  if ($env:CI -eq 'true') { throw 'Windows CI image is missing C++/Windows SDK build tools.' }
  $answer=Read-Host 'Install missing Visual Studio Build Tools via official winget package? (Y/N; several GB)'
  if ($answer -notmatch '^[Yy]$') { throw 'Native build canceled; no portable archive generated.' }
  if (!(Get-Command winget.exe -ErrorAction SilentlyContinue)) { throw 'winget is unavailable. Use official Visual Studio Build Tools with Desktop development with C++.' }
  & winget.exe install --id Microsoft.VisualStudio.2022.BuildTools --exact --source winget --accept-source-agreements --accept-package-agreements --override '--wait --passive --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended'
  if ($LASTEXITCODE -ne 0) { throw 'C++ build tools installation failed or requires restart.' }
  $vcvars=Find-VcVars
  if (!$vcvars) { throw 'Missing C++ or Windows SDK headers after installation.' }
}
$nativeCmd=Join-Path $PSScriptRoot 'NativeLayout\BUILD-NATIVE.cmd'
& $env:ComSpec /d /c "call `"$vcvars`" && call `"$nativeCmd`" `"$out`""
if ($LASTEXITCODE -ne 0) { throw 'Native layout DLL compilation failed.' }
foreach($file in @('LGSTray.exe','LGSTrayHID.exe','hidapi.dll','appsettings.toml','NativeLayout.dll')) {
  if (!(Test-Path (Join-Path $out $file) -PathType Leaf)) { throw "Missing portable file: $file" }
}
Copy-Item LICENSE,MODIFICATIONS.md,INSTALL.md -Destination $out -Force
@'
Battery Monitor - Rings.9 (Windows 11 x64)
Extract the complete archive to a writable directory, then run LGSTray.exe.
The .NET runtime is bundled. No SDK, Windhawk, or mod installation is required.
Keep LGSTrayHID.exe and the supplied DLLs beside LGSTray.exe.
NativeLayout.dll is experimental. Left placement automatically attempts a native XAML reservation; unsupported builds fall back to safe positioning. See INSTALL.md.
Source and license: https://github.com/fhzzzs (modified edition); see MODIFICATIONS.md.
'@ | Set-Content (Join-Path $out 'START-HERE.txt') -Encoding UTF8
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
Write-Host "Portable package: $zip"
if(!$NoOpen) { Invoke-Item $out }
