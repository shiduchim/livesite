@echo off
setlocal
title EK Script Launcher - Install
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install EK Script Launcher.ps1"
if errorlevel 1 (
  echo.
  echo Installation did not finish. Keep this window open and send the text above for review.
)
echo.
pause
