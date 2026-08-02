@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "API_ROOT=%ROOT%\xenia api\XeniaManager.Api"

if not exist "%API_ROOT%\XeniaManager.Api.csproj" (
  echo [start-api-admin] ERROR: API project file not found at "%API_ROOT%".
  exit /b 1
)

echo [start-api-admin] Launching elevated API window...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoExit','-Command','Set-Location -LiteralPath ''%API_ROOT%''; $env:ASPNETCORE_ENVIRONMENT=''Development''; dotnet run --urls http://127.0.0.1:5077')"

endlocal
exit /b 0
