@echo off
setlocal
title EK Print C# Installer
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install_EK_Print_CSharp.ps1"
exit /b %errorlevel%
