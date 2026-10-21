@echo off
setlocal EnableExtensions
chcp 65001 >nul 2>&1
title EK FastPDF - Start Viewer
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0START_EK_FASTPDF.ps1"
if errorlevel 1 (
  echo.
  echo The viewer did not start. Please send EKFastPDF_Start_Log.txt from your Desktop.
  pause
)
