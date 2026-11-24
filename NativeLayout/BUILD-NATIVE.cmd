@echo off
setlocal
if "%~1"=="" exit /b 2
set "OUT=%~1"
if not exist "%OUT%" mkdir "%OUT%"
where cl.exe >nul 2>nul
if errorlevel 1 (
  echo MSVC x64 tools not initialized.
  exit /b 2
)
pushd "%OUT%"
cl /nologo /std:c++20 /EHsc /O2 /MT /LD /DUNICODE /D_UNICODE /DWINVER=0x0A00 /D_WIN32_WINNT=0x0A00 "%~dp0NativeLayout.cpp" /link /MACHINE:X64 /OUT:NativeLayout.dll user32.lib ole32.lib oleaut32.lib runtimeobject.lib windowsapp.lib
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" del /q NativeLayout.obj NativeLayout.lib NativeLayout.exp 2>nul
popd
exit /b %RC%
