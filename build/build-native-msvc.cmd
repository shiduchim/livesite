@echo off
rem Builds EKTriangleHook.dll and the native test harness with MSVC (x64).
rem Run from a "x64 Native Tools" environment (build.ps1 sets it up).
rem Usage: build-native-msvc.cmd <output folder> [version e.g. 1.0.0]
setlocal
set "ROOT=%~dp0.."
set "OUT=%~1"
set "VER=%~2"
if "%OUT%"=="" set "OUT=%ROOT%\out\native"
if "%VER%"=="" set "VER=1.0.0"
for /f "tokens=1-3 delims=." %%a in ("%VER%") do set "VRC=%%a,%%b,%%c,0"
set "MH=%ROOT%\src\native\third_party\minhook"
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%\obj" || exit /b 1

> "%OUT%\obj\ekt_version.h" echo #define EKT_VERSION_RC %VRC%
>> "%OUT%\obj\ekt_version.h" echo #define EKT_VERSION_STR "%VER%"
rc /nologo /dEKT_HAVE_VERSION_H /i "%OUT%\obj" /fo "%OUT%\obj\EKTriangleHook.res" "%ROOT%\src\native\EKTriangleHook.rc" || exit /b 1

cl /nologo /O2 /MT /W3 /EHsc /std:c++17 /guard:cf /Brepro /DUNICODE /D_UNICODE /DEKT_VERSION=%VER% /I"%MH%\include" /Fo"%OUT%\obj\\" ^
   "%ROOT%\src\native\EKTriangleHook.cpp" "%MH%\src\hook.c" "%MH%\src\buffer.c" "%MH%\src\trampoline.c" "%MH%\src\hde\hde64.c" ^
   "%OUT%\obj\EKTriangleHook.res" /LD /Fe"%OUT%\EKTriangleHook.dll" ^
   /link /Brepro /DEBUG:NONE /GUARD:CF /DYNAMICBASE /NXCOMPAT uxtheme.lib gdiplus.lib gdi32.lib user32.lib || exit /b 1
del "%OUT%\EKTriangleHook.lib" "%OUT%\EKTriangleHook.exp" 2>nul

cl /nologo /O2 /MT /W3 /EHsc /std:c++17 /DUNICODE /D_UNICODE /Fo"%OUT%\obj\\" "%ROOT%\tests\native\NativeTest.cpp" ^
   /Fe"%OUT%\Revit.exe" /link uxtheme.lib gdi32.lib user32.lib || exit /b 1
copy /y "%OUT%\Revit.exe" "%OUT%\NotRevit.exe" >nul
rmdir /s /q "%OUT%\obj"
echo Native build OK: %OUT%
