@echo off
setlocal
title EK FastPDF - Launch diagnostic
cd /d "%~dp0"
echo Checking EK FastPDF launch problem...
echo This test will not change any Windows settings.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Diagnose_EKFastPDF.ps1"
echo.
echo Please send the EKFastPDF_Diagnostic.txt file created on your Desktop.
pause
