@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo [start-dev-sessions] Preparing full dev stack from "%ROOT%"...

if not exist "%ROOT%\web-port\package.json" (
  echo [start-dev-sessions] ERROR: web-port\package.json not found.
  exit /b 1
)

where npm >nul 2>&1
if errorlevel 1 (
  echo [start-dev-sessions] ERROR: npm is not available on PATH.
  exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [start-dev-sessions] ERROR: dotnet is not available on PATH.
  exit /b 1
)

set "API_IN_USE=0"
for /f "tokens=5" %%P in ('netstat -ano ^| findstr /R /C:":5077 .*LISTENING"') do (
  set "API_IN_USE=1"
)

if "%API_IN_USE%"=="0" (
  call "%ROOT%\stop-dev-sessions.bat"
) else (
  echo [start-dev-sessions] Detected an existing API listener on port 5077.
  echo [start-dev-sessions] Running web-only mode and reusing the existing API.
)

if not exist "%ROOT%\web-port\node_modules\vite\bin\vite.js" (
  echo [start-dev-sessions] Installing web dependencies...
  npm --prefix "%ROOT%\web-port" install
  if errorlevel 1 (
    echo [start-dev-sessions] ERROR: npm install failed.
    exit /b 1
  )
)

if "%API_IN_USE%"=="0" (
  echo [start-dev-sessions] Launching full stack: Web + API + CloudMorph...
  echo [start-dev-sessions] Close the new terminal window or run stop-dev-sessions.bat to stop everything.
  start "dashx360-dev" cmd /k "cd /d ^"%ROOT%^" && npm --prefix ^"%ROOT%\web-port^" run dev"
) else (
  echo [start-dev-sessions] Launching web-only: Vite on 3600...
  echo [start-dev-sessions] API is expected at http://127.0.0.1:5077.
  start "dashx360-web" cmd /k "cd /d ^"%ROOT%\web-port^" && node ^"%ROOT%\web-port\node_modules\vite\bin\vite.js^" --host 127.0.0.1 --port 3600 --strictPort"
)

endlocal
exit /b 0