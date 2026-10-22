@echo off
setlocal
echo ==========================================================
echo Building PrinterToolkit.exe (Native Windows Application)...
echo ==========================================================

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo [ERROR] .NET Framework 4.0/4.8 compiler not found at %CSC%
    pause
    exit /b 1
)

"%CSC%" /target:winexe ^
  /out:"%~dp0PrinterToolkit.exe" ^
  /win32icon:"%~dp0app.ico" ^
  /win32manifest:"%~dp0src\app.manifest" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.Management.dll ^
  /r:System.ServiceProcess.dll ^
  /r:System.IO.Compression.dll ^
  /r:System.IO.Compression.FileSystem.dll ^
  /optimize+ ^
  "%~dp0src\*.cs"

if %errorlevel% equ 0 (
    echo.
    echo [SUCCESS] Binary compiled: "%~dp0PrinterToolkit.exe"
) else (
    echo.
    echo [ERROR] Build failed with code %errorlevel%
)

endlocal
