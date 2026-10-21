@echo off
rem Builds EK FastPDF (tests, self-contained app, portable ZIP and, if Inno Setup 6 is installed, the installer).
rem Requires the .NET 8 SDK. Output: out\installer\
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "build\build.ps1" %*
if errorlevel 1 (
  echo.
  echo BUILD FAILED - see the messages above.
  pause
  exit /b 1
)
echo.
echo Done. Installer and portable ZIP are in "%~dp0out\installer".
pause
