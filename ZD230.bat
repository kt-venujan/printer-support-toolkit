@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Zebra ZD230 Printer Diagnostic & Repair Toolkit
color 0B

:: ============================================================
:: CONFIGURATION
:: ============================================================
set "PRINTER=ZDesigner ZD230-203dpi ZPL"
set "SUPPORT_URL=https://www.zebra.com/us/en/support-downloads/printers/desktop/zd230.html"
set "OUT_DIR=%USERPROFILE%\Desktop\ZD230_Diagnostics"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"
set "REPORT=%OUT_DIR%\ZD230_Diagnostic_Report.txt"

:: Check for Administrator privileges (needed for Spooler control)
net session >nul 2>&1
if %errorlevel% neq 0 (
    color 0E
    echo ============================================================
    echo [WARNING] NOT RUNNING AS ADMINISTRATOR!
    echo Features like "Clear Print Spooler" require Admin rights.
    echo Please right-click this .bat file and "Run as administrator".
    echo ============================================================
    pause
    color 0B
)

:: Start by generating and showing the report automatically
goto GENERATE_REPORT

:: ============================================================
:: 1. GENERATE & SHOW REPORT FIRST
:: ============================================================
:GENERATE_REPORT
cls
echo ============================================================
echo   GENERATING ZEBRA ZD230 DIAGNOSTIC REPORT... PLEASE WAIT
echo ============================================================
echo Scanning Printer Status, Driver Versions, Spooler, and USB Ports...

(
    echo ============================================================
    echo ZEBRA ZD230 DIAGNOSTIC REPORT
    echo Computer : %COMPUTERNAME%
    echo User     : %USERNAME%
    echo Date/Time: %DATE% %TIME%
    echo ============================================================
    echo.
    echo [1] PRINTER CONNECTION ^& STATUS
    powershell -NoProfile -Command "$p = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ZDesigner|ZD230|Zebra' }; if ($p) { $p | Format-List Name,DriverName,PortName,PrinterStatus,WorkOffline,JobCount } else { Write-Output 'No Zebra/ZD230 printer found in Windows.' }"
    echo.
    echo [2] INSTALLED DRIVER VERSION ^& DETAILS
    powershell -NoProfile -Command "Get-CimInstance Win32_PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ZDesigner|ZD230|Zebra' } | Format-List Name,Version,DriverPath,InfName,SupportedPlatform"
    powershell -NoProfile -Command "Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ZDesigner|ZD230|Zebra' } | Format-List Name,Manufacturer,MajorVersion,DriverVersion,InfPath"
    echo.
    echo [3] DETECTED USB PRINTER PORTS
    powershell -NoProfile -Command "Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^USB|Zebra|ZD230' } | Format-List Name,Description,PortMonitor"
    echo.
    echo [4] LIVE USB PLUG-AND-PLAYHARDWARE STATUS
    powershell -NoProfile -Command "$u = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -match 'Zebra|ZD230|ZDesigner|USB Printing Support' }; if ($u) { $u | Format-List Status,Class,FriendlyName,InstanceId } else { Write-Output 'DISCONNECTED: No active Zebra USB hardware detected right now.' }"
    echo.
    echo [5] PRINT SPOOLER SERVICE STATUS
    sc query Spooler
    echo.
    echo [6] STUCK PRINT QUEUE JOBS
    powershell -NoProfile -Command "Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ZDesigner|ZD230|Zebra' } | ForEach-Object { Write-Output ('--- Queue for: ' + $_.Name); Get-PrintJob -PrinterName $_.Name -ErrorAction SilentlyContinue | Format-List ID,DocumentName,UserName,JobStatus,SubmittedTime,Size }"
    echo.
    echo [7] OFFICIAL ZEBRA DRIVER UPDATE LINK
    echo %SUPPORT_URL%
    echo ============================================================
) > "%REPORT%" 2>&1

echo.
echo [DONE] Report saved to: "%REPORT%"
echo Opening report in Notepad...
start "" notepad "%REPORT%"
timeout /t 2 >nul
goto MENU

:: ============================================================
:: MAIN INTERACTIVE MENU (Shown after Report)
:: ============================================================
:MENU
cls
echo ============================================================
echo        ZEBRA ZD230 ACTION MENU (POST-REPORT OPTIONS)
echo ============================================================
echo  Report File : %REPORT%
echo ------------------------------------------------------------
echo  [1] Clear Print Spooler (Fix stuck jobs ^& restart spooler)
echo  [2] Driver Update Check ^& Reinstall Driver Option
echo  [3] Verify Connection ^& Live USB Port Detector (Reconnect Test)
echo  [4] Refresh / View Diagnostic Report Again
echo  [5] Open Windows Printers ^& Scanners Control Panel
echo  [0] Exit Toolkit
echo ============================================================
choice /c 123450 /n /m "Select an option [1-5, or 0 to Exit]: "

if errorlevel 6 goto EXIT_TOOL
if errorlevel 5 goto OPEN_CONTROL
if errorlevel 4 goto GENERATE_REPORT
if errorlevel 3 goto USB_DETECTOR
if errorlevel 2 goto DRIVER_MANAGER
if errorlevel 1 goto CLEAR_SPOOLER

:: ============================================================
:: OPTION 1: CLEAR PRINT SPOOLER
:: ============================================================
:CLEAR_SPOOLER
cls
echo ============================================================
echo                 CLEAR WINDOWS PRINT SPOOLER
echo ============================================================
echo This will:
echo  1. Stop the Windows Print Spooler service.
echo  2. Delete all stuck print jobs in C:\Windows\System32\spool\PRINTERS
echo  3. Restart the Print Spooler service cleanly.
echo.
choice /c YN /n /m "Are you sure you want to clear the spooler now? (Y/N): "
if errorlevel 2 goto MENU

echo.
echo [1/3] Stopping Print Spooler service...
net stop spooler
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Could not stop Spooler! Please right-click this .bat file
    echo and choose "Run as administrator".
    pause
    goto MENU
)

echo [2/3] Deleting stuck print job files...
del /q /f /s "%SystemRoot%\System32\spool\PRINTERS\*.*" >nul 2>&1

echo [3/3] Starting Print Spooler service...
net start spooler
echo.
echo [SUCCESS] Print Spooler has been cleared and restarted!
pause
goto MENU

:: ============================================================
:: OPTION 2: CHECK DRIVER VERSION, OFFICIAL PAGE & REINSTALL
:: ============================================================
:DRIVER_MANAGER
cls
echo ============================================================
echo          PRINTER DRIVER VERSION CHECK ^& REINSTALL
echo ============================================================
echo Checking currently installed Zebra driver version on this PC...
echo ------------------------------------------------------------
powershell -NoProfile -Command "$d = Get-CimInstance Win32_PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ZDesigner|ZD230|Zebra' }; if ($d) { $d | Select-Object Name, @{Name='InstalledFileVersion';Expression={(Get-Item $_.DriverPath).VersionInfo.FileVersion}}, DriverPath, InfName | Format-List } else { Write-Host 'No installed Zebra driver detected!' -ForegroundColor Yellow }"
echo ------------------------------------------------------------
echo.
echo What would you like to do?
echo  [1] Open Official Zebra ZD230 Page to check/download latest driver
echo  [2] Reinstall / Update Driver (Launch downloaded installer or .INF)
echo  [3] Open Windows Print Management / Driver Properties
echo  [0] Back to Main Menu
echo.
choice /c 1230 /n /m "Choose an option: "

if errorlevel 4 goto MENU
if errorlevel 3 (
    start "" printui.exe /s /t2
    goto DRIVER_MANAGER
)
if errorlevel 2 goto REINSTALL_DRIVER
if errorlevel 1 (
    echo Opening Official Zebra Support Page...
    start "" "%SUPPORT_URL%"
    echo.
    echo Compare your "InstalledFileVersion" above with the latest
    echo "ZDesigner Windows Driver v10 / v5" on the Zebra website.
    pause
    goto DRIVER_MANAGER
)

:REINSTALL_DRIVER
echo.
echo ============================================================
echo                   REINSTALL PRINTER DRIVER
echo ============================================================
echo TIP: Unplug the printer USB cable before running a fresh driver
echo install, or follow the Zebra installer prompts when asked to plug in.
echo.
set "INSTALLER_PATH="
set /p "INSTALLER_PATH=Drag & drop the downloaded Zebra Driver .EXE (or paste full path): "
:: Strip quotes if dragged and dropped
set "INSTALLER_PATH=%INSTALLER_PATH:"=%"

if not exist "%INSTALLER_PATH%" (
    echo.
    echo [ERROR] File not found: "%INSTALLER_PATH%"
    pause
    goto DRIVER_MANAGER
)

echo.
echo Launching driver installer: "%INSTALLER_PATH%"
start "" "%INSTALLER_PATH%"
echo.
echo Follow the on-screen Zebra Setup Wizard to reinstall/update your driver.
pause
goto MENU

:: ============================================================
:: OPTION 3: VERIFY CONNECTION & LIVE USB PORT DETECTOR
:: ============================================================
:USB_DETECTOR
cls
echo ============================================================
echo      VERIFY CONNECTION ^& LIVE USB PORT NUMBER DETECTOR
echo ============================================================
echo Checking current USB connection status and mapped Printer Port...
echo.
powershell -NoProfile -Command ^
    "$usb = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -match 'Zebra|ZD230|ZDesigner|USB Printing Support' };" ^
    "$prn = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ZDesigner|ZD230|Zebra' };" ^
    "if ($usb) { Write-Host '[STATUS] CONNECTED! Active USB Hardware Found:' -ForegroundColor Green; $usb | Select-Object FriendlyName, Status, InstanceId | Format-Table -AutoSize } else { Write-Host '[STATUS] DISCONNECTED! Printer is NOT detected on USB right now.' -ForegroundColor Red };" ^
    "if ($prn) { Write-Host 'Assigned Printer Queue Port(s):' -ForegroundColor Cyan; $prn | Select-Object Name, PortName, PrinterStatus | Format-Table -AutoSize }"

echo ============================================================
echo LIVE USB RECONNECT MONITOR (DETECT USB PORT NUMBER):
echo  - Unplug and reconnect your Zebra ZD230 USB cable now!
echo  - Every 2 seconds, this scanner detects connection changes
echo    and displays the active USB Port Number (e.g., USB001, USB002).
echo  - Press [Q] at any time to stop monitoring and return to Menu.
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$lastState = '';" ^
    "Write-Host 'Waiting for USB plug/unplug events... (Press Q to quit)' -ForegroundColor Yellow;" ^
    "while ($true) {" ^
    "    if ([Console]::KeyAvailable) { $k = [Console]::ReadKey($true); if ($k.Key -eq 'Q') { break } };" ^
    "    $pnp = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -match 'Zebra|ZD230|ZDesigner|USB Printing Support' });" ^
    "    $ports = @(Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^USB' } | Select-Object -ExpandProperty Name);" ^
    "    $prnPort = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ZDesigner|ZD230|Zebra' } | ForEach-Object { $_.Name + ' -> ' + $_.PortName });" ^
    "    $connStatus = if ($pnp.Count -gt 0) { 'CONNECTED (' + ($pnp.FriendlyName -join ', ') + ')' } else { 'DISCONNECTED' };" ^
    "    $portList = if ($ports.Count -gt 0) { $ports -join ', ' } else { 'None' };" ^
    "    $mapped = if ($prnPort.Count -gt 0) { $prnPort -join '; ' } else { 'No Zebra Queue' };" ^
    "    $current = \"Status: $connStatus | Active System USB Ports: [$portList] | Printer Mapped To: [$mapped]\";" ^
    "    if ($current -ne $lastState) {" ^
    "        $time = Get-Date -Format 'HH:mm:ss';" ^
    "        if ($pnp.Count -gt 0) { Write-Host \"[$time] [+] $current\" -ForegroundColor Green }" ^
    "        else { Write-Host \"[$time] [-] $current\" -ForegroundColor Red };" ^
    "        $lastState = $current;" ^
    "    };" ^
    "    Start-Sleep -Milliseconds 1500;" ^
    "}"

goto MENU

:: ============================================================
:: OPTION 5: OPEN WINDOWS PRINTER SETTINGS
:: ============================================================
:OPEN_CONTROL
start "" control printers
goto MENU

:EXIT_TOOL
cls
echo Thank you for using the Zebra ZD230 Diagnostic Toolkit!
timeout /t 2 >nul
endlocal
exit /b 0