# EK Triangle 1.0.2 - automated test results

Machine: Microsoft Windows Server 2022 Datacenter 10.0.20348 (GitHub Actions Windows runner)  
Date (UTC): 2026-10-28 22:04  
Commit: 096e58a9a12e86a5f460bf4324e082b13fc6325c

These are automated build, simulation and Windows checks. They do NOT replace the short manual check inside Revit (see docs/ACCEPTANCE-TEST.md); Revit itself is not available on CI.

## Unit tests (simulated Revit installations)
- .NET Framework 4.8 : total 65, passed 65, failed 0
- .NET 8 : total 65, passed 65, failed 0

## native-tests.txt
```
PASS native:exports
   size  9 closed   full= 12 partial=  8 bbox=3x6 long=6 (L=8) outside=0 wrong=0 -> ok
   size  9 opened   full= 12 partial=  8 bbox=6x3 long=6 (L=8) outside=0 wrong=0 -> ok
   size 10 closed   full= 12 partial=  8 bbox=3x6 long=6 (L=8) outside=0 wrong=0 -> ok
   size 10 opened   full= 12 partial=  8 bbox=6x3 long=6 (L=8) outside=0 wrong=0 -> ok
   size 12 closed   full= 20 partial= 10 bbox=4x8 long=8 (L=10) outside=0 wrong=0 -> ok
   size 12 opened   full= 20 partial= 10 bbox=8x4 long=8 (L=10) outside=0 wrong=0 -> ok
   size 13 closed   full= 30 partial= 12 bbox=5x10 long=10 (L=12) outside=0 wrong=0 -> ok
   size 13 opened   full= 30 partial= 12 bbox=10x5 long=10 (L=12) outside=0 wrong=0 -> ok
   size 14 closed   full= 30 partial= 12 bbox=5x10 long=10 (L=12) outside=0 wrong=0 -> ok
   size 14 opened   full= 30 partial= 12 bbox=10x5 long=10 (L=12) outside=0 wrong=0 -> ok
   size 16 closed   full= 42 partial= 14 bbox=6x12 long=12 (L=14) outside=0 wrong=0 -> ok
   size 16 opened   full= 42 partial= 14 bbox=12x6 long=12 (L=14) outside=0 wrong=0 -> ok
   size 18 closed   full= 56 partial= 16 bbox=7x14 long=14 (L=16) outside=0 wrong=0 -> ok
   size 18 opened   full= 56 partial= 16 bbox=14x7 long=14 (L=16) outside=0 wrong=0 -> ok
   size 20 closed   full= 72 partial= 18 bbox=8x16 long=16 (L=18) outside=0 wrong=0 -> ok
   size 20 opened   full= 72 partial= 18 bbox=16x8 long=16 (L=18) outside=0 wrong=0 -> ok
   size 24 closed   full=110 partial= 22 bbox=10x20 long=20 (L=22) outside=0 wrong=0 -> ok
   size 24 opened   full=110 partial= 22 bbox=20x10 long=20 (L=22) outside=0 wrong=0 -> ok
   size 32 closed   full=182 partial= 28 bbox=13x26 long=26 (L=28) outside=0 wrong=0 -> ok
   size 32 opened   full=182 partial= 28 bbox=26x13 long=26 (L=28) outside=0 wrong=0 -> ok
PASS native:render
   EKTriangle_Init -> 2
PASS native:notrevit
   EKTriangle_Init -> 1
PASS native:disabled
   EKTriangle_Init -> 3
PASS native:legacy
   EKTriangle_Init -> 0
   second copy EKTriangle_Init -> 3
PASS native:dup
   EKTriangle_Init -> 0
   hooked DrawThemeBackground part=2 state=1 hr=0x00000000 grey=12
   hooked DrawThemeBackground part=2 state=2 hr=0x00000000 grey=12
   hooked DrawThemeBackground part=4 state=1 hr=0x00000000 grey=12
   hooked DrawThemeBackground part=4 state=2 hr=0x00000000 grey=12
   after shutdown grey=0
PASS native:hook
```

## payload-checks.txt
```
PASS helper EKTriangle.exe present
PASS helper targets .NET Framework 4.8 (.NETFramework,Version=v4.8)
PASS helper has no separate DLL dependency
PASS Revit 2024 loader present
PASS Revit 2024 native DLL present
PASS Revit 2024 native DLL is x64
PASS Revit 2024 loader framework .NETFramework,Version=v4.8
PASS Revit 2024 loader references RevitAPIUI 24.0.0.0
PASS Revit 2024 loader class EKTriangle.Loader.App
PASS Revit 2024 loader is x64
PASS Revit 2024 native export EKTriangle_Init
PASS Revit 2024 native export EKTriangle_Shutdown
PASS Revit 2024 native DLL is not the legacy EKGlyphHook
PASS Revit 2025 loader present
PASS Revit 2025 native DLL present
PASS Revit 2025 native DLL is x64
PASS Revit 2025 loader framework .NETCoreApp,Version=v8.0
PASS Revit 2025 loader references RevitAPIUI 25.0.0.0
PASS Revit 2025 loader class EKTriangle.Loader.App
PASS Revit 2025 loader is x64
PASS Revit 2025 native export EKTriangle_Init
PASS Revit 2025 native export EKTriangle_Shutdown
PASS Revit 2025 native DLL is not the legacy EKGlyphHook
PASS Revit 2026 loader present
PASS Revit 2026 native DLL present
PASS Revit 2026 native DLL is x64
PASS Revit 2026 loader framework .NETCoreApp,Version=v8.0
PASS Revit 2026 loader references RevitAPIUI 26.0.4.0
PASS Revit 2026 loader class EKTriangle.Loader.App
PASS Revit 2026 loader is x64
PASS Revit 2026 native export EKTriangle_Init
PASS Revit 2026 native export EKTriangle_Shutdown
PASS Revit 2026 native DLL is not the legacy EKGlyphHook
PASS Revit 2027 loader present
PASS Revit 2027 native DLL present
PASS Revit 2027 native DLL is x64
PASS Revit 2027 loader framework .NETCoreApp,Version=v10.0
PASS Revit 2027 loader references RevitAPIUI 27.0.0.0
PASS Revit 2027 loader class EKTriangle.Loader.App
PASS Revit 2027 loader is x64
PASS Revit 2027 native export EKTriangle_Init
PASS Revit 2027 native export EKTriangle_Shutdown
PASS Revit 2027 native DLL is not the legacy EKGlyphHook
```

## e2e\installer-e2e.txt
```

=== Simulated machine
PASS no EK Triangle installed at start

=== Install is blocked while Revit runs
PASS setup refused while Revit 2024 runs (exit 1)
PASS no add-in written while Revit runs
PASS main.js untouched while Revit runs

=== First install (all detected versions; Revit 2027 main.js read-only)
PASS setup exit code 0
PASS registered in Installed apps (HKCU uninstall key)
PASS DisplayName 'EK Triangle'
PASS Publisher 'EK'
PASS DisplayVersion '1.0.2'
PASS installed EKTriangle.exe
PASS installed glyph.ini
PASS installed README.txt
PASS installed Revit2024\EKTriangle.dll
PASS installed Revit2024\EKTriangleHook.dll
PASS installed Revit2027\EKTriangle.dll
PASS Revit 2024 add-in manifest installed
PASS Revit 2025 add-in manifest installed
PASS Revit 2027 add-in manifest installed
PASS no manifest for absent Revit 2026
PASS Revit 2024 Project Browser patched
PASS read-only Revit 2027 main.js reported and left untouched
PASS Revit 2025 unsupported main.js left untouched
PASS verified original backup for 2024
PASS emergency restore tool placed in ProgramData
PASS experimental probe manifest deactivated
PASS experimental probe kept as backup
PASS Revit 2024 unrelated add-in untouched
PASS Revit 2025 unrelated add-in untouched
PASS Revit 2027 unrelated add-in untouched
PASS no temporary files left in Revit folder

=== Reinstall over itself (completes the partial install)
PASS reinstall exit code 0
PASS Revit 2027 Project Browser patched on reinstall (BOM file)
PASS Project Browser unchanged by reinstall
PASS exactly one Installed apps entry
PASS exactly one EK Triangle manifest for 2024
PASS backup not duplicated

=== Upgrade/modify: keep only Revit 2024
PASS modify exit code 0
PASS Revit 2027 add-in removed when deselected
PASS Revit 2027 Project Browser restored exactly when deselected
PASS Revit 2024 still patched
PASS Revit 2027 re-added

=== Uninstall is blocked while Revit runs
PASS uninstall refused while Revit 2027 runs (exit 1)
PASS nothing removed while Revit runs

=== Uninstall after an Autodesk update of one version
PASS uninstalled (exit 0)
PASS Revit 2024 Project Browser restored byte-for-byte
PASS Autodesk-updated 2027 file NOT overwritten by old backup
PASS Revit 2025 still untouched
PASS Revit 2024 add-in removed
PASS Revit 2025 add-in removed
PASS Revit 2027 add-in removed
PASS Revit 2024 unrelated add-in untouched
PASS Revit 2025 unrelated add-in untouched
PASS Revit 2027 unrelated add-in untouched
PASS program files removed
PASS original backups preserved after uninstall
PASS other Autodesk files untouched

=== Repeated install/uninstall cycles
PASS cycle 1 install
PASS cycle 1 uninstall restored originals
PASS cycle 1 manifests removed
PASS cycle 2 install
PASS cycle 2 uninstall restored originals
PASS cycle 2 manifests removed
PASS cycle 3 install
PASS cycle 3 uninstall restored originals
PASS cycle 3 manifests removed

=== Emergency restore without the installed app
PASS emergency browser-restore exit 0
PASS emergency tool restored originals
PASS emergency tool removed add-in manifests
PASS uninstall after emergency restore succeeds

=== Clean state
PASS no EK Triangle manifests remain
PASS no stale program files

71 passed, 0 failed
```

## simple\simple-e2e.txt
```
PASS ZIP contains INSTALL.cmd
PASS ZIP contains INSTALL.ps1
PASS ZIP contains UNINSTALL.cmd
PASS ZIP contains UNINSTALL.ps1
PASS ZIP contains CHECK_STATUS.cmd
PASS ZIP contains CHECK_STATUS.ps1
PASS ZIP contains engine/EKTriangle.Core.dll
PASS ZIP contains README.txt
PASS ZIP contains CODE-INVENTORY.csv
PASS SIMPLE ZIP contains zero EXEs
PASS ZIP SHA-256 verified: ACCEPTANCE-TEST.md
PASS ZIP SHA-256 verified: BROWSER.ps1
PASS ZIP SHA-256 verified: CHECK_STATUS.cmd
PASS ZIP SHA-256 verified: CHECK_STATUS.ps1
PASS ZIP SHA-256 verified: CODE-INVENTORY.csv
PASS ZIP SHA-256 verified: COMMON.ps1
PASS ZIP SHA-256 verified: engine/EKTriangle.Core.dll
PASS ZIP SHA-256 verified: INSTALL.cmd
PASS ZIP SHA-256 verified: INSTALL.ps1
PASS ZIP SHA-256 verified: LAUNCH-INVESTIGATION.md
PASS ZIP SHA-256 verified: MINHOOK-LICENSE.txt
PASS ZIP SHA-256 verified: payload/glyph.ini
PASS ZIP SHA-256 verified: payload/Revit2024/EKTriangle.dll
PASS ZIP SHA-256 verified: payload/Revit2024/EKTriangleHook.dll
PASS ZIP SHA-256 verified: payload/Revit2025/EKTriangle.dll
PASS ZIP SHA-256 verified: payload/Revit2025/EKTriangleHook.dll
PASS ZIP SHA-256 verified: payload/Revit2026/EKTriangle.dll
PASS ZIP SHA-256 verified: payload/Revit2026/EKTriangleHook.dll
PASS ZIP SHA-256 verified: payload/Revit2027/EKTriangle.dll
PASS ZIP SHA-256 verified: payload/Revit2027/EKTriangleHook.dll
PASS ZIP SHA-256 verified: README.txt
PASS ZIP SHA-256 verified: THIRD-PARTY-NOTICES.txt
PASS ZIP SHA-256 verified: UNINSTALL.cmd
PASS ZIP SHA-256 verified: UNINSTALL.ps1
PASS inventory contains nine DLLs, five PS1 scripts and three CMD scripts
PASS compiled loader for 2024
PASS compiled native DLL for 2024
PASS compiled loader for 2025
PASS compiled native DLL for 2025
PASS compiled loader for 2026
PASS compiled native DLL for 2026
PASS compiled loader for 2027
PASS compiled native DLL for 2027
PASS CMD uses CRLF, no BOM: CHECK_STATUS.cmd
PASS CMD uses CRLF, no BOM: INSTALL.cmd
PASS CMD uses CRLF, no BOM: UNINSTALL.cmd
PASS no old helper invocation in BROWSER.ps1
PASS no policy bypass, encoded commands or runtime compilation in BROWSER.ps1
PASS PowerShell syntax valid: BROWSER.ps1
PASS no old helper invocation in CHECK_STATUS.cmd
PASS no policy bypass, encoded commands or runtime compilation in CHECK_STATUS.cmd
PASS ordinary PowerShell -File launcher: CHECK_STATUS.cmd
PASS no old helper invocation in CHECK_STATUS.ps1
PASS no policy bypass, encoded commands or runtime compilation in CHECK_STATUS.ps1
PASS PowerShell syntax valid: CHECK_STATUS.ps1
PASS no old helper invocation in COMMON.ps1
PASS no policy bypass, encoded commands or runtime compilation in COMMON.ps1
PASS PowerShell syntax valid: COMMON.ps1
PASS no old helper invocation in INSTALL.cmd
PASS no policy bypass, encoded commands or runtime compilation in INSTALL.cmd
PASS ordinary PowerShell -File launcher: INSTALL.cmd
PASS no old helper invocation in INSTALL.ps1
PASS no policy bypass, encoded commands or runtime compilation in INSTALL.ps1
PASS PowerShell syntax valid: INSTALL.ps1
PASS no old helper invocation in UNINSTALL.cmd
PASS no policy bypass, encoded commands or runtime compilation in UNINSTALL.cmd
PASS ordinary PowerShell -File launcher: UNINSTALL.cmd
PASS no old helper invocation in UNINSTALL.ps1
PASS no policy bypass, encoded commands or runtime compilation in UNINSTALL.ps1
PASS PowerShell syntax valid: UNINSTALL.ps1
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2024\EKTriangle.dll
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2024\EKTriangleHook.dll
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2025\EKTriangle.dll
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2025\EKTriangleHook.dll
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2026\EKTriangle.dll
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2026\EKTriangleHook.dll
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2027\EKTriangle.dll
PASS application DLL is byte-identical to existing 1.0.2 release: payload\Revit2027\EKTriangleHook.dll
PASS old recovery EXE absent before SIMPLE tests
PASS status CMD succeeds
PASS absent 2026 detected correctly
PASS status CMD does not write files, logs or recovery state
PASS INSTALL.cmd blocks a running Revit
PASS blocked installation writes no manifest
PASS default installs all four years; unsupported 2025 browser reports partial
PASS attached original T-symbol pattern migrated to current dark-gray triangle
PASS original SymbolSwap backup preserved
PASS installation did not recreate an EXE helper
PASS direct INSTALL.ps1 repairs a missing backup safely
PASS missing backup reconstructed and verified against original bytes
PASS Revit 2024 managed DLL in stable installed location
PASS Revit 2024 native DLL installed and verified
PASS Revit 2025 managed DLL in stable installed location
PASS Revit 2025 native DLL installed and verified
PASS Revit 2026 managed DLL in stable installed location
PASS Revit 2026 native DLL installed and verified
PASS Revit 2027 managed DLL in stable installed location
PASS Revit 2027 native DLL installed and verified
PASS compatible browser components patched in 2024,2026,2027
PASS unsupported 2025 browser left byte-for-byte unchanged
PASS known user EKGlyph prototype deactivated
PASS legacy manifest backup preserved
PASS reinstall succeeds with an explicit partial result
PASS verified reinstall requires no unnecessary elevation
PASS exactly one active manifest for 2024
PASS exactly one active manifest for 2025
PASS exactly one active manifest for 2026
PASS exactly one active manifest for 2027
PASS payload upgrade succeeds with unsupported browser explicitly skipped
PASS upgrade activates a verified immutable payload while retaining the previous version
PASS upgrade keeps a single active manifest
PASS version deselection succeeds
PASS deselected 2025 manifest removed
PASS deselected 2026 manifest removed
PASS deselected 2027 manifest removed
PASS deselected browser files restored exactly
PASS read-only Autodesk resource returns an unresolved error
PASS permission failure preserves original 2027 resource
PASS reinstall repairs the partial installation
PASS UNINSTALL.cmd blocks a running Revit
PASS blocked uninstall leaves active manifest
PASS missing Autodesk target reports unresolved restoration
PASS unresolved restore preserves payload/recovery data
PASS retry uninstall succeeds after missing target is recovered
PASS new Autodesk file never overwritten by old backup
PASS 2026 restore verified byte-for-byte
PASS cycle 1 install after interrupted temporary file
PASS cycle 1 interrupted temporary file cleaned
PASS cycle 1 uninstall
PASS cycle 1 exact restoration for 2024
PASS cycle 1 no active manifest for 2024
PASS cycle 1 unrelated EK app preserved for 2024
PASS cycle 1 exact restoration for 2025
PASS cycle 1 no active manifest for 2025
PASS cycle 1 unrelated EK app preserved for 2025
PASS cycle 1 exact restoration for 2026
PASS cycle 1 no active manifest for 2026
PASS cycle 1 unrelated EK app preserved for 2026
PASS cycle 1 exact restoration for 2027
PASS cycle 1 no active manifest for 2027
PASS cycle 1 unrelated EK app preserved for 2027
PASS cycle 2 install after interrupted temporary file
PASS cycle 2 interrupted temporary file cleaned
PASS cycle 2 uninstall
PASS cycle 2 exact restoration for 2024
PASS cycle 2 no active manifest for 2024
PASS cycle 2 unrelated EK app preserved for 2024
PASS cycle 2 exact restoration for 2025
PASS cycle 2 no active manifest for 2025
PASS cycle 2 unrelated EK app preserved for 2025
PASS cycle 2 exact restoration for 2026
PASS cycle 2 no active manifest for 2026
PASS cycle 2 unrelated EK app preserved for 2026
PASS cycle 2 exact restoration for 2027
PASS cycle 2 no active manifest for 2027
PASS cycle 2 unrelated EK app preserved for 2027
PASS cycle 3 install after interrupted temporary file
PASS cycle 3 interrupted temporary file cleaned
PASS cycle 3 uninstall
PASS cycle 3 exact restoration for 2024
PASS cycle 3 no active manifest for 2024
PASS cycle 3 unrelated EK app preserved for 2024
PASS cycle 3 exact restoration for 2025
PASS cycle 3 no active manifest for 2025
PASS cycle 3 unrelated EK app preserved for 2025
PASS cycle 3 exact restoration for 2026
PASS cycle 3 no active manifest for 2026
PASS cycle 3 unrelated EK app preserved for 2026
PASS cycle 3 exact restoration for 2027
PASS cycle 3 no active manifest for 2027
PASS cycle 3 unrelated EK app preserved for 2027
PASS SIMPLE-installed DLLs removed after successful uninstall
PASS original backups retained
PASS no old recovery EXE was recreated or required
PASS missing/quarantined DLL stops safely
PASS missing DLL stops before touching installation
PASS final status succeeds
PASS final status remains read-only
PASS direct CHECK_STATUS.ps1 succeeds
PASS direct status is read-only
PASS direct INSTALL.ps1 installs selected version
PASS independent browser PowerShell step uses the same existing engine
PASS browser script accepts the restore-only elevation argument shape
PASS direct UNINSTALL.ps1 restores without Revit starting
PASS denied execute permission produces Win32 error 5 before any application status handler runs
PASS denied DLL read permission stops script with a readable error
PASS denied DLL error is reported explicitly
PASS denied DLL status failure changes no application files
PASS complete SIMPLE test suite used no old recovery helper
189 passed, 0 failed
```
