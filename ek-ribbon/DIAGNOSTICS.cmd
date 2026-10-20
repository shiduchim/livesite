@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer\Diagnostics.ps1" %*
if errorlevel 1 echo.& echo Stopped: see the message above. Nothing else was changed.
echo.
pause
