[CmdletBinding()]
param([switch]$NoOpen,[switch]$Test,[ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$RingsVersion='11.0.0')
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

$version="3.2.6-rings.$RingsVersion"
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
foreach($file in @('LGSTray.exe','LGSTrayHID.exe','hidapi.dll','appsettings.toml')) {
  if (!(Test-Path (Join-Path $out $file) -PathType Leaf)) { throw "Missing portable file: $file" }
}
Copy-Item LICENSE,MODIFICATIONS.md,INSTALL.md -Destination $out -Force
@'
Battery Monitor - Rings (Windows 11 x64)
Extract the complete archive to a writable directory, then run LGSTray.exe.
The .NET runtime is bundled. No SDK, Windhawk, or mod installation is required.
Keep LGSTrayHID.exe and the supplied DLLs beside LGSTray.exe.
Source and license: https://github.com/fhzzzs (modified edition); see MODIFICATIONS.md.
'@ | Set-Content (Join-Path $out 'START-HERE.txt') -Encoding UTF8
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
Write-Host "Portable package: $zip"
if(!$NoOpen) { Invoke-Item $out }
