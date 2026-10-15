# Building EK Triangle

End users need none of this – they only run `EK_Triangle_Setup.exe`.

## Windows (release build, same as CI)

Requirements: Visual Studio 2022 or Build Tools with *Desktop development with C++*,
.NET SDK 8 and 10, Inno Setup 6.

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

Output: `out\installer\EK_Triangle_Setup.exe` and `SHA256SUMS.txt`. The script
builds the MSVC native DLL, the helper EXE (.NET Framework 4.8), one loader per
Revit year, runs the unit tests (.NET 8 and 4.8), the native tests and the payload
checks, then compiles the installer. No Revit installation is needed: the Revit API
reference assemblies come from the `Nice3point.Revit.Api.*` NuGet packages.

## CI

`.github/workflows/build.yml` runs the build on a Windows runner, then the
end-to-end installer test (`tests/installer/Test-Installer.ps1`) against simulated
Revit installations, uploads the results and commits `release/<version>/` to the branch.

## Linux (developer check)

`build/build-linux.sh` runs the unit tests, cross-compiles the native DLL with
MinGW and compiles the installer with Inno Setup under Wine. Release builds use MSVC.

## Layout

| Path | Content |
|------|---------|
| `src/native` | `EKTriangleHook.dll` (C++, uxtheme hook, MinHook) |
| `src/loader` | Revit add-in loader, one project per Revit year |
| `src/core`   | install / patch / restore logic (tested) |
| `src/tool`   | `EKTriangle.exe` – used by setup, uninstall and emergency restore |
| `installer`  | Inno Setup script, icon, `glyph.ini` |
| `tests`      | unit tests, native harness, Windows end-to-end installer test |
| `legacy-reference` | the earlier tested sources this release is based on |
