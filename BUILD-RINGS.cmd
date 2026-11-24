@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD-RINGS.ps1" %*
if errorlevel 1 (
  echo Build failed. See errors above.
  pause
  exit /b 1
)
echo Build complete. See bin\LGSTrayEx-Rings-Portable-x64.zip
pause
