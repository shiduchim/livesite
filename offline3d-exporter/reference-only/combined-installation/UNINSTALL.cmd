@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "O3D_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if defined PROCESSOR_ARCHITEW6432 set "O3D_PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%O3D_PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Setup.ps1" -Mode Uninstall
set "O3D_RESULT=%ERRORLEVEL%"
exit /b %O3D_RESULT%
