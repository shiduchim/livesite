@echo off
setlocal DisableDelayedExpansion
pushd "%~dp0" || exit /b 1
if not exist "%~dp0CHECK_STATUS.ps1" goto missing
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -File "%~dp0CHECK_STATUS.ps1" %*
set "EKT_RESULT=%ERRORLEVEL%"
if not "%EKT_RESULT%"=="0" echo Read the result above. If PowerShell did not start, script policy or file permissions may have blocked it. Ask IT; keep security enabled.
goto done
:missing
echo STOPPED: CHECK_STATUS.ps1 is missing. Extract the complete SIMPLE ZIP or ask IT if it was quarantined.
set "EKT_RESULT=1"
:done
echo.
echo Exit code: %EKT_RESULT%
if not defined EKTRIANGLE_NO_PAUSE pause
popd
exit /b %EKT_RESULT%
