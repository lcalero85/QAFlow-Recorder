@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Installer.ps1"
if errorlevel 1 (
  echo.
  echo Installer build failed. Review the message above.
  pause
  exit /b 1
)
echo.
echo Installer generated successfully.
pause
