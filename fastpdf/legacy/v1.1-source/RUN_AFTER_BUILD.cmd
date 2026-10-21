@echo off
cd /d "%~dp0"
if exist "EK FastPDF\EKFastPDF.exe" (
    start "" "EK FastPDF\EKFastPDF.exe" %*
) else (
    echo The application has not been built yet. First run BUILD_WINDOWS.cmd
    pause
)
