@echo off
setlocal
title EK Script Launcher - Uninstall
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall EK Script Launcher.ps1"
echo.
pause
