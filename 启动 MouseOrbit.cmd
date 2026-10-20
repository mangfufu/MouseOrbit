@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-MouseOrbit.ps1" %*
set "launchResult=%errorlevel%"
if not "%launchResult%"=="0" (
  echo.
  echo MouseOrbit could not start. See the error and log path above.
  pause
)
exit /b %launchResult%
