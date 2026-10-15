@echo off
setlocal DisableDelayedExpansion
pushd "%~dp0" || exit /b 1
if not exist "EKTriangle.CMD.exe" goto missing
"%~dp0EKTriangle.CMD.exe" uninstall %*
set "EKT_RESULT=%ERRORLEVEL%"
goto done
:missing
echo STOPPED: EKTriangle.CMD.exe is missing. Extract the complete ZIP.
echo If company security blocked it, ask IT to review the inventory. Keep security enabled.
set "EKT_RESULT=1"
:done
echo.
echo Exit code: %EKT_RESULT%
if not defined EKTRIANGLE_NO_PAUSE pause
popd
exit /b %EKT_RESULT%
