@echo off
setlocal EnableExtensions EnableDelayedExpansion

echo [stop-dev-sessions] Stopping duplicate web-port and API dev processes...

REM Kill API executable directly.
taskkill /IM XeniaManager.Api.exe /T /F >nul 2>&1

REM Kill virtual display control/CLI tools that may be spawned by session start.
taskkill /IM "VDD Control.exe" /T /F >nul 2>&1
taskkill /IM NetBox.VirtualDisplayCli.exe /T /F >nul 2>&1

REM Kill CloudMorph/Xenia bridge dev binary and its per-session child
REM processes (ffmpeg capture + syncinput.exe input relay) so a restart never
REM leaves a stale capture pipeline holding the RTP port or window handle.
taskkill /IM cloudmorph-dev.exe /T /F >nul 2>&1
taskkill /IM syncinput.exe /T /F >nul 2>&1
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"name='ffmpeg.exe'\" ^| Where-Object { $_.CommandLine -like '*gdigrab*' } ^| ForEach-Object { $_.ProcessId }"`) do (
  taskkill /PID %%P /T /F >nul 2>&1
)

REM Kill any dotnet host that launched XeniaManager.Api.
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"name='dotnet.exe'\" ^| Where-Object { $_.CommandLine -like '*XeniaManager.Api*' -or $_.CommandLine -like '*XeniaManager.Api.dll*' } ^| ForEach-Object { $_.ProcessId }"`) do (
  taskkill /PID %%P /T /F >nul 2>&1
)

REM Kill any dotnet host running the virtual display CLI project.
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"name='dotnet.exe'\" ^| Where-Object { $_.CommandLine -like '*NetBox.VirtualDisplayCli*' } ^| ForEach-Object { $_.ProcessId }"`) do (
  taskkill /PID %%P /T /F >nul 2>&1
)

REM Kill any web-port dev supervisors/wrappers (node, npm, cmd, powershell).
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Get-CimInstance Win32_Process ^| Where-Object { $_.Name -in @('node.exe','npm.cmd','cmd.exe','powershell.exe') } ^| Where-Object { $_.CommandLine -like '*dashx360-1.2.2\\web-port*' -and ( $_.CommandLine -like '*vite*' -or $_.CommandLine -like '*npm*run*dev*' -or $_.CommandLine -like '*scripts\\dev.mjs*' ) } ^| ForEach-Object { $_.ProcessId }"`) do (
  taskkill /PID %%P /T /F >nul 2>&1
)

REM Clear listeners on expected dev ports in case any orphan process remains.
for %%L in (3600 3601 3602 3603 3604 5077 8080) do (
  for /f "tokens=5" %%P in ('netstat -ano ^| findstr /R /C:":%%L .*LISTENING"') do (
    taskkill /PID %%P /T /F >nul 2>&1
  )
)

REM Give Windows a moment to release handles/ports.
timeout /t 1 /nobreak >nul

echo [stop-dev-sessions] Done. Ports and dev processes cleaned.
echo [stop-dev-sessions] You can now start one fresh session:
echo   npm --prefix "c:\Users\Owner\Desktop\dashx360-1.2.2\web-port" run dev

endlocal
exit /b 0
