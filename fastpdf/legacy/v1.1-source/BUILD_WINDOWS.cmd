@echo off
setlocal
cd /d "%~dp0"
title EK FastPDF - Windows Builder
color 1F
echo.
echo  ========================================================
echo          EK FastPDF 1.1 - Windows 64-bit
 echo  ========================================================
echo.
where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET 8 SDK was not found. Install it first:
    echo https://dotnet.microsoft.com/en-us/download/dotnet/8.0
    echo.
    start "" "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
    pause
    exit /b 1
)
echo Building Windows application and bundling the runtime...
echo This first build will restore the PDFium native engine from NuGet.
echo.
dotnet publish "src\EKFastPDF\EKFastPDF.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o "EK FastPDF"
if errorlevel 1 (
    echo.
    echo ERROR: Build failed. Please copy the error shown above for troubleshooting.
    pause
    exit /b 1
)
echo.
echo Build succeeded.
echo Your viewer: "%CD%\EK FastPDF\EKFastPDF.exe"
echo.
set /p launch=Start EK FastPDF now? [Y/n]: 
if /I not "%launch%"=="n" start "" "EK FastPDF\EKFastPDF.exe"
exit /b 0
