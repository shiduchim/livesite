@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install EK Script Launcher.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. Keep this window open and send the error text for review.
  pause
)

