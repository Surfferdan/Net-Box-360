@echo off
REM Builds all buildable components of the NetBox 360 project.
REM Requires: .NET 8 SDK, Go 1.21+, Node.js 18+ (npm) on PATH.

setlocal enabledelayedexpansion
set ROOT=%~dp0
set FAILED=0

echo ===============================================
echo  NetBox 360 - Build All
echo ===============================================

echo.
echo [1/3] Web dashboard (web-port)...
pushd "%ROOT%web-port"
call npm install
if errorlevel 1 (
    echo   FAILED: npm install
    set FAILED=1
    popd
    goto :xenia_api
)
call npx tsc --noEmit
if errorlevel 1 (
    echo   FAILED: tsc type-check
    set FAILED=1
)
call npx vite build
if errorlevel 1 (
    echo   FAILED: vite build
    set FAILED=1
)
popd

:xenia_api
echo.
echo [2/3] NetBox / Xenia API (xenia api\XeniaManager.Api)...
pushd "%ROOT%xenia api\XeniaManager.Api"
call dotnet build
if errorlevel 1 (
    echo   FAILED: dotnet build
    set FAILED=1
)
popd

echo.
echo [3/3] CloudMorph streaming bridge (cloud morph code\cloud-morph-master)...
pushd "%ROOT%cloud morph code\cloud-morph-master"
call go build ./...
if errorlevel 1 (
    echo   FAILED: go build
    set FAILED=1
)
popd

echo.
echo ===============================================
if "%FAILED%"=="1" (
    echo  Build finished WITH ERRORS - see output above.
) else (
    echo  All components built successfully.
)
echo ===============================================

endlocal
exit /b %FAILED%
