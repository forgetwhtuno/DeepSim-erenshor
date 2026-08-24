@echo off
setlocal
cd /d "%~dp0"
echo Deep Sims 0.8.2 Beta - native Lunaris build and install
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_AND_INSTALL.ps1" %*
if errorlevel 1 (
  echo.
  echo Build or install failed.
  exit /b %errorlevel%
)
echo.
echo Build and install completed.
endlocal
