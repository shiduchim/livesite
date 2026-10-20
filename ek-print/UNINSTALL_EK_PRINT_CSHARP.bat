@echo off
setlocal
title Uninstall EK Print C#
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall_EK_Print_CSharp.ps1"
exit /b %errorlevel%
