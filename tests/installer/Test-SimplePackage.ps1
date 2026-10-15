param(
    [Parameter(Mandatory=$true)][string]$Zip,
    [string]$Fixtures,
    [string]$FakeRevitExe,
    [string]$ResultDir = 'out/test-results/simple',
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$Zip = (Resolve-Path $Zip).Path
$ResultDir = (New-Item -ItemType Directory -Force -Path $ResultDir).FullName
$extract = Join-Path $ResultDir ('extracted package ' + [guid]::NewGuid().ToString('N'))
Expand-Archive $Zip $extract
$pkg = Join-Path $extract 'EK_Triangle_SIMPLE_Installer'
$report = [Collections.Generic.List[string]]::new()
$script:passed = 0; $script:failed = 0; $script:run = 0
function Ok([bool]$condition, [string]$text) {
    if ($condition) { $script:passed++; $line = "PASS $text" } else { $script:failed++; $line = "FAIL $text" }
    $report.Add($line); Write-Host $line
}
function Hash([string]$path) { if (Test-Path -LiteralPath $path -PathType Leaf) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'MISSING' } }
function Finish {
    $report.Add("$script:passed passed, $script:failed failed")
    $report | Set-Content (Join-Path $ResultDir $(if ($VerifyOnly) {'archive-checks.txt'} else {'simple-e2e.txt'})) -Encoding UTF8
    if ($script:failed) { throw "$script:failed SIMPLE package checks failed" }
}
foreach ($name in 'INSTALL.cmd','INSTALL.ps1','UNINSTALL.cmd','UNINSTALL.ps1','CHECK_STATUS.cmd','CHECK_STATUS.ps1','engine/EKTriangle.Core.dll','README.txt','CODE-INVENTORY.csv') {
    Ok (Test-Path (Join-Path $pkg $name)) "ZIP contains $name"
}
Ok (@(Get-ChildItem $pkg -Recurse -Filter '*.exe').Count -eq 0) 'SIMPLE ZIP contains zero EXEs'
foreach ($line in Get-Content (Join-Path $pkg 'SHA256SUMS.txt')) {
    $hash, $rel = $line -split '  ', 2
    Ok ((Hash (Join-Path $pkg $rel)) -eq $hash) "ZIP SHA-256 verified: $rel"
}
$inventory = @(Import-Csv (Join-Path $pkg 'CODE-INVENTORY.csv'))
Ok ($inventory.Count -eq 17) 'inventory contains nine DLLs, five PS1 scripts and three CMD scripts'
foreach ($y in 2024,2025,2026,2027) {
    Ok (Test-Path (Join-Path $pkg "payload/Revit$y/EKTriangle.dll")) "compiled loader for $y"
    Ok (Test-Path (Join-Path $pkg "payload/Revit$y/EKTriangleHook.dll")) "compiled native DLL for $y"
}
foreach ($f in Get-ChildItem $pkg -Filter '*.cmd') {
    $bytes = [IO.File]::ReadAllBytes($f.FullName)
    Ok (([Text.Encoding]::ASCII.GetString($bytes) -notmatch '(?<!\r)\n') -and ($bytes[0] -eq 64)) "CMD uses CRLF, no BOM: $($f.Name)"
}
foreach ($file in Get-ChildItem $pkg -File | Where-Object Extension -In '.cmd','.ps1') {
    $text=[IO.File]::ReadAllText($file.FullName)
    Ok ($text -notmatch '(?i)EKTriangle(?:\.CMD)?\.exe|EK_Triangle_Setup\.exe') "no old helper invocation in $($file.Name)"
    Ok ($text -notmatch '(?i)(?:^|\s)-ExecutionPolicy\b|-EncodedCommand|Set-ExecutionPolicy|Unblock-File|Add-MpPreference|Set-MpPreference|FromBase64String|Add-Type\s+-(?:TypeDefinition|MemberDefinition)') "no policy bypass, encoded commands or runtime compilation in $($file.Name)"
    if ($file.Extension -eq '.cmd') { Ok ($text -match 'powershell.exe"?\s+-NoLogo -NoProfile -File') "ordinary PowerShell -File launcher: $($file.Name)" }
    else {
        $tokens=$null; $errors=$null
        $null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
        Ok ($errors.Count -eq 0) "PowerShell syntax valid: $($file.Name)"
    }
}
if ($VerifyOnly) { Finish; return }
# The full test is deliberately confined to GitHub's disposable Windows runner.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'E2E requires a disposable GitHub Windows runner' }
$Fixtures = (Resolve-Path $Fixtures).Path
$FakeRevitExe = (Resolve-Path $FakeRevitExe).Path
$env:EKTRIANGLE_NO_PAUSE = '1'
$ourId = 'D24DC5EF-6D2B-4013-B5D0-37B8F2763892'
$store = Join-Path $env:ProgramData 'EKTriangle'
$userStore = Join-Path $env:LOCALAPPDATA 'EKTriangle'
function RevitDir([int]$y) { Join-Path $env:ProgramFiles "Autodesk/Revit $y" }
function MainJs([int]$y) { Join-Path (RevitDir $y) 'AddIns/ProjectBrowser/resource/static/js/main.js' }
function Addins([int]$y) { Join-Path $env:APPDATA "Autodesk/Revit/Addins/$y" }
function Manifest([int]$y) { Join-Path (Addins $y) 'EKTriangle.addin' }
function Patched([int]$y) { (Test-Path (MainJs $y)) -and (Get-Content (MainJs $y) -Raw).Contains('M6 4L10 8L6 12Z') }
function Run([string]$entry, [string]$extra = '') {
    $script:run++
    if ($entry.EndsWith('.ps1')) {
        $psi=[Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'), ('-NoLogo -NoProfile -File "'+(Join-Path $pkg $entry)+'" '+$extra))
    } else {
        $psi=[Diagnostics.ProcessStartInfo]::new($env:ComSpec, ('/d /s /c ""' + (Join-Path $pkg $entry) + '" ' + $extra + '"'))
    }
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $p = [Diagnostics.Process]::Start($psi)
    $script:lastOutput = $p.StandardOutput.ReadToEnd() + $p.StandardError.ReadToEnd()
    $p.WaitForExit(); $code = $p.ExitCode; $p.Dispose()
    $script:lastOutput | Set-Content (Join-Path $ResultDir "command-$script:run.txt") -Encoding UTF8
    Write-Host $script:lastOutput
    return $code
}
function Snapshot {
    $lines = foreach ($dir in @($store, $userStore, (Join-Path $env:ProgramFiles 'Autodesk'), (Join-Path $env:APPDATA 'Autodesk/Revit/Addins'))) {
        if (Test-Path $dir) { Get-ChildItem $dir -Recurse -File | ForEach-Object { $_.FullName + ':' + (Hash $_.FullName) } }
    }
    return (($lines | Sort-Object) -join "`n")
}

# Previous EXE E2E leaves 2024/2025/2027 fixtures and no active installation.
# Remove its recovery EXE on this disposable machine: SIMPLE must not need it.
$oldRecovery=Join-Path $store 'EK Triangle Restore.exe'
if (Test-Path $oldRecovery) { Remove-Item $oldRecovery }
Ok (-not (Test-Path $oldRecovery)) 'old recovery EXE absent before SIMPLE tests' 
$before = Snapshot
Ok ((Run 'CHECK_STATUS.cmd') -eq 0) 'status CMD succeeds'
Ok ($lastOutput.Contains('Revit 2026: not detected')) 'absent 2026 detected correctly'
Ok ((Snapshot) -eq $before) 'status CMD does not write files, logs or recovery state'

# Add 2026, making this test exercise the real frontend on all four years.
New-Item -ItemType Directory -Force -Path (Split-Path (MainJs 2026)) | Out-Null
Copy-Item $FakeRevitExe (Join-Path (RevitDir 2026) 'Revit.exe')
Copy-Item (Join-Path $Fixtures 'main-original.js') (MainJs 2026)
$original = @{}; $other = @{}
foreach ($y in 2024,2025,2026,2027) {
    $original[$y] = Hash (MainJs $y)
    New-Item -ItemType Directory -Force -Path (Addins $y) | Out-Null
    Set-Content (Join-Path (Addins $y) 'EKOtherApp.addin') '<RevitAddIns><AddIn><AddInId>11111111-3333-4444-5555-777777777777</AddInId><Name>Unrelated EK</Name></AddIn></RevitAddIns>'
    $other[$y] = Hash (Join-Path (Addins $y) 'EKOtherApp.addin')
}
$legacy = Join-Path (Addins 2024) 'legacy-probe.addin'
Set-Content $legacy '<RevitAddIns><AddIn><AddInId>6F0C2E7B-6B0E-4C58-9E1B-3C1A3D9F2E41</AddInId><FullClassName>EKGlyph.App</FullClassName></AddIn></RevitAddIns>'
$fake = Start-Process (Join-Path (RevitDir 2024) 'Revit.exe') -ArgumentList 'sleep','600' -PassThru
try {
    Start-Sleep -Seconds 1
    Ok ((Run 'INSTALL.cmd' '-Yes') -eq 2) 'INSTALL.cmd blocks a running Revit'
    Ok (-not (Test-Path (Manifest 2024))) 'blocked installation writes no manifest'
} finally { Stop-Process -Id $fake.Id -Force; $fake.WaitForExit() }

# Reproduce the original attached browser installer's legacy T symbol and
# matching state format, then let the unchanged compatibility engine migrate it.
$legacyStore=Join-Path $env:ProgramData 'EKSymbolSwap/Revit2026'
New-Item -ItemType Directory -Force -Path $legacyStore | Out-Null
$legacyBackup=Join-Path $legacyStore 'main.js.original-ci.bak'
Copy-Item (MainJs 2026) $legacyBackup
$t=[IO.File]::ReadAllText((MainJs 2026)).Replace('d:"M7 7V4h2v3h3v2H9v3H7V9H4V7h3z"','d:"M7 3h2v5h4v2H3V8h4z"')
[IO.File]::WriteAllText((MainJs 2026),$t,[Text.UTF8Encoding]::new($false))
@{PatchId='EKSymbolSwap_2024_ConfirmedSvg_T_v2';Year=2026;Target=(MainJs 2026);Backup=$legacyBackup;OriginalSHA256=$original[2026];PatchedSHA256=(Hash (MainJs 2026))} | ConvertTo-Json | Set-Content (Join-Path $legacyStore 'fouryear-install-state.json') -Encoding UTF8
Ok ((Run 'INSTALL.cmd' '-Yes') -eq 4) 'default installs all four years; unsupported 2025 browser reports partial'
Ok (Patched 2026) 'attached original T-symbol pattern migrated to current dark-gray triangle'
Ok ((Hash $legacyBackup) -eq $original[2026]) 'original SymbolSwap backup preserved'
Ok (-not (Test-Path $oldRecovery)) 'installation did not recreate an EXE helper'
# Missing original backup: unchanged restore engine reconstructs it by exact reversal.
Get-ChildItem (Join-Path $store 'Browser/Revit2026') -Filter '*.original' | Remove-Item
Ok ((Run 'INSTALL.ps1' '-Yes') -eq 4) 'direct INSTALL.ps1 repairs a missing backup safely'
Ok (@(Get-ChildItem (Join-Path $store 'Browser/Revit2026') -Filter '*.original' | Where-Object { (Hash $_.FullName) -eq $original[2026] }).Count -ge 1) 'missing backup reconstructed and verified against original bytes'

foreach ($y in 2024,2025,2026,2027) {
    [xml]$manifestXml = Get-Content (Manifest $y)
    $loader = [string]$manifestXml.RevitAddIns.AddIn.Assembly
    Ok (Test-Path $loader) "Revit $y managed DLL in stable installed location"
    Ok ((Hash (Join-Path (Split-Path $loader) 'EKTriangleHook.dll')) -eq (Hash (Join-Path $pkg "payload/Revit$y/EKTriangleHook.dll"))) "Revit $y native DLL installed and verified"
}
Ok ((Patched 2024) -and (Patched 2026) -and (Patched 2027)) 'compatible browser components patched in 2024,2026,2027'
Ok ((Hash (MainJs 2025)) -eq $original[2025]) 'unsupported 2025 browser left byte-for-byte unchanged'
Ok (-not (Test-Path $legacy)) 'known user EKGlyph prototype deactivated'
Ok (@(Get-ChildItem (Join-Path $userStore 'LegacyBackup') -Recurse -Filter '*legacy-probe.addin').Count -ge 1) 'legacy manifest backup preserved'

Copy-Item (Manifest 2024) (Join-Path (Addins 2024) 'renamed-triangle.addin')
Ok ((Run 'INSTALL.cmd' '-Yes') -eq 4) 'reinstall succeeds with an explicit partial result'
Ok (-not $lastOutput.Contains('Project Browser step')) 'verified reinstall requires no unnecessary elevation'
foreach ($y in 2024,2025,2026,2027) {
    $ours = @(Get-ChildItem (Addins $y) -Filter '*.addin' | Where-Object { (Get-Content $_.FullName -Raw).Contains($ourId) })
    Ok ($ours.Count -eq 1) "exactly one active manifest for $y"
}
# Simulate an approved payload update by changing the documented settings and
# regenerating its inventory; binaries remain the same compiled artifacts.
[xml]$oldManifest = Get-Content (Manifest 2024)
$oldLoader = [string]$oldManifest.RevitAddIns.AddIn.Assembly
$config = Join-Path $pkg 'payload/glyph.ini'
$hashList = Join-Path $pkg 'SHA256SUMS.txt'
$savedConfig = [IO.File]::ReadAllBytes($config)
$savedHashes = [IO.File]::ReadAllBytes($hashList)
Add-Content $config '; simulated approved upgrade'
$updatedLines = Get-Content $hashList | ForEach-Object {
    if ($_ -match '^([A-F0-9]{64})  payload/glyph.ini$') { (Hash $config) + '  payload/glyph.ini' } else { $_ }
}
$updatedLines | Set-Content $hashList -Encoding ASCII
try {
    Ok ((Run 'INSTALL.cmd' '-Yes') -eq 4) 'payload upgrade succeeds with unsupported browser explicitly skipped'
    [xml]$newManifest = Get-Content (Manifest 2024)
    $newLoader = [string]$newManifest.RevitAddIns.AddIn.Assembly
    Ok (($newLoader -ne $oldLoader) -and (Test-Path $newLoader) -and (Test-Path $oldLoader)) 'upgrade activates a verified immutable payload while retaining the previous version'
    Ok (@(Get-ChildItem (Addins 2024) -Filter '*.addin' | Where-Object { (Get-Content $_.FullName -Raw).Contains($ourId) }).Count -eq 1) 'upgrade keeps a single active manifest'
} finally {
    [IO.File]::WriteAllBytes($config, $savedConfig)
    [IO.File]::WriteAllBytes($hashList, $savedHashes)
}
Ok ((Run 'INSTALL.cmd' '-Yes -Years 2024') -eq 0) 'version deselection succeeds'
foreach ($y in 2025,2026,2027) { Ok (-not (Test-Path (Manifest $y))) "deselected $y manifest removed" }
Ok (((Hash (MainJs 2026)) -eq $original[2026]) -and ((Hash (MainJs 2027)) -eq $original[2027])) 'deselected browser files restored exactly'

# Permission failure must be observable, with original content preserved.
Set-ItemProperty (MainJs 2027) -Name IsReadOnly -Value $true
try {
    Ok ((Run 'INSTALL.cmd' '-Yes') -eq 1) 'read-only Autodesk resource returns an unresolved error'
    Ok ((Hash (MainJs 2027)) -eq $original[2027]) 'permission failure preserves original 2027 resource'
} finally { Set-ItemProperty (MainJs 2027) -Name IsReadOnly -Value $false }
Ok ((Run 'INSTALL.cmd' '-Yes') -eq 4) 'reinstall repairs the partial installation'

$fake = Start-Process (Join-Path (RevitDir 2027) 'Revit.exe') -ArgumentList 'sleep','600' -PassThru
try {
    Start-Sleep -Seconds 1
    Ok ((Run 'UNINSTALL.cmd' '-Yes') -eq 2) 'UNINSTALL.cmd blocks a running Revit'
    Ok (Test-Path (Manifest 2024)) 'blocked uninstall leaves active manifest'
} finally { Stop-Process -Id $fake.Id -Force; $fake.WaitForExit() }

# Missing target with active record must fail; restoring the unchanged patched
# bytes permits retry without inventing an outdated original.
$savedPatched = [IO.File]::ReadAllBytes((MainJs 2026))
Remove-Item (MainJs 2026)
Ok ((Run 'UNINSTALL.cmd' '-Yes') -eq 1) 'missing Autodesk target reports unresolved restoration'
Ok (@(Get-ChildItem (Join-Path $userStore 'SIMPLE/Payloads') -Recurse -Filter '*.dll').Count -gt 0) 'unresolved restore preserves payload/recovery data'
[IO.File]::WriteAllBytes((MainJs 2026), $savedPatched)
Copy-Item (Join-Path $Fixtures 'main-autodesk-update.js') (MainJs 2027) -Force
$updated = Hash (MainJs 2027)
Ok ((Run 'UNINSTALL.cmd' '-Yes') -eq 0) 'retry uninstall succeeds after missing target is recovered'
Ok ((Hash (MainJs 2027)) -eq $updated) 'new Autodesk file never overwritten by old backup'
Ok ((Hash (MainJs 2026)) -eq $original[2026]) '2026 restore verified byte-for-byte'
Copy-Item (Join-Path $Fixtures 'main-original-bom.js') (MainJs 2027) -Force

for ($cycle = 1; $cycle -le 3; $cycle++) {
    # Simulate leftovers from an interrupted browser operation.
    Set-Content ((MainJs 2024) + '.EKTriangle.interrupted.tmp') 'partial bytes'
    Ok ((Run 'INSTALL.cmd' '-Yes') -eq 4) "cycle $cycle install after interrupted temporary file"
    Ok (-not (Test-Path ((MainJs 2024) + '.EKTriangle.interrupted.tmp'))) "cycle $cycle interrupted temporary file cleaned"
    Ok ((Run 'UNINSTALL.cmd' '-Yes') -eq 0) "cycle $cycle uninstall"
    foreach ($y in 2024,2025,2026,2027) {
        Ok ((Hash (MainJs $y)) -eq $original[$y]) "cycle $cycle exact restoration for $y"
        Ok (-not (Test-Path (Manifest $y))) "cycle $cycle no active manifest for $y"
        Ok ((Hash (Join-Path (Addins $y) 'EKOtherApp.addin')) -eq $other[$y]) "cycle $cycle unrelated EK app preserved for $y"
    }
}
Ok (@(Get-ChildItem (Join-Path $userStore 'SIMPLE/Payloads') -Recurse -Filter '*.dll').Count -eq 0) 'SIMPLE-installed DLLs removed after successful uninstall'
Ok (@(Get-ChildItem (Join-Path $store 'Browser/Revit2024') -Filter '*.original').Count -ge 1) 'original backups retained'
Ok (-not (Test-Path $oldRecovery)) 'no old recovery EXE was recreated or required'

# Corruption/quarantine simulation: the coordinator refuses before changes.
$dll = Join-Path $pkg 'payload/Revit2024/EKTriangleHook.dll'
$savedDll = [IO.File]::ReadAllBytes($dll)
Remove-Item $dll
$before = Snapshot
Ok ((Run 'INSTALL.cmd' '-Yes') -eq 1) 'missing/quarantined DLL stops safely'
Ok ((Snapshot) -eq $before) 'missing DLL stops before touching installation'
[IO.File]::WriteAllBytes($dll, $savedDll)
$before = Snapshot
Ok ((Run 'CHECK_STATUS.cmd') -eq 0) 'final status succeeds'
Ok ((Snapshot) -eq $before) 'final status remains read-only'
# Exercise the PS1 entry points directly, not just their CMD launchers.
$before=Snapshot
Ok ((Run 'CHECK_STATUS.ps1') -eq 0) 'direct CHECK_STATUS.ps1 succeeds'
Ok ((Snapshot) -eq $before) 'direct status is read-only'
Ok ((Run 'INSTALL.ps1' '-Yes -Years 2024') -eq 0) 'direct INSTALL.ps1 installs selected version'
$browserReport=Join-Path $ResultDir 'browser-direct.json'
Ok ((Run 'BROWSER.ps1' ('-ApplyYears 2024 -ReportPath "'+$browserReport+'"')) -eq 0) 'independent browser PowerShell step uses the same existing engine'
# This uses the same ordinary -File argument shape as the UAC branch, including
# an empty apply list. CI is already elevated, so no interactive UAC is attempted.
Ok ((Run 'BROWSER.ps1' ('-ApplyYears "" -RestoreYears 2024 -ReportPath "'+$browserReport+'"')) -eq 0) 'browser script accepts the restore-only elevation argument shape'
Ok ((Run 'UNINSTALL.ps1' '-Yes') -eq 0) 'direct UNINSTALL.ps1 restores without Revit starting'

# Investigate error 5 and distinguish file read failures from process launch
# denial using disposable CI fixtures. No application-control policy is changed.
$denied=Join-Path $ResultDir 'DeniedLaunch.exe'
Copy-Item $FakeRevitExe $denied
$identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$originalAcl=Get-Acl $denied
try {
    $acl=Get-Acl $denied
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity,[Security.AccessControl.FileSystemRights]::ExecuteFile,[Security.AccessControl.AccessControlType]::Deny))
    Set-Acl -LiteralPath $denied -AclObject $acl
    $nativeCode=0
    try {
        $psi=[Diagnostics.ProcessStartInfo]::new($denied,'exports')
        $psi.UseShellExecute=$false
        $p=[Diagnostics.Process]::Start($psi)
        $p.WaitForExit(); $p.Dispose()
    } catch {
        $errorObject=$_.Exception
        while ($null -ne $errorObject.InnerException) { $errorObject=$errorObject.InnerException }
        if ($errorObject -is [ComponentModel.Win32Exception]) { $nativeCode=$errorObject.NativeErrorCode }
    }
    Ok ($nativeCode -eq 5) 'denied execute permission produces Win32 error 5 before any application status handler runs'
} finally { Set-Acl -LiteralPath $denied -AclObject $originalAcl }

$core=Join-Path $pkg 'engine/EKTriangle.Core.dll'
$originalAcl=Get-Acl $core
$before=Snapshot
try {
    $acl=Get-Acl $core
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity,[Security.AccessControl.FileSystemRights]::ReadData,[Security.AccessControl.AccessControlType]::Deny))
    Set-Acl -LiteralPath $core -AclObject $acl
    Ok ((Run 'CHECK_STATUS.cmd') -eq 1) 'denied DLL read permission stops script with a readable error'
    Ok ($lastOutput.Contains('STOPPED:')) 'denied DLL error is reported explicitly'
    Ok ((Snapshot) -eq $before) 'denied DLL status failure changes no application files'
} finally { Set-Acl -LiteralPath $core -AclObject $originalAcl }
Ok (-not (Test-Path $oldRecovery)) 'complete SIMPLE test suite used no old recovery helper'
Finish
