<#
.SYNOPSIS
  End-to-end Windows test of the EK FastPDF installer and of every normal way to start the app.

  Installs silently, then launches EK FastPDF through: direct process start, Explorer
  (shell double-click equivalent), Start-menu and desktop shortcuts, App Paths, the "Open with"
  ProgID and Applications registration (exactly what Explorer uses), a quoted path with spaces
  and Unicode from another working directory, and single-instance hand-over. Also checks that
  the default PDF handler is not changed, runs diagnostics and a print-to-file, performs an
  upgrade over the existing installation and finally uninstalls and verifies removal.
#>
param(
    [Parameter(Mandatory)] [string]$Setup,
    [Parameter(Mandatory)] [string]$SamplePdf,
    [Parameter(Mandatory)] [string]$ResultDir,
    [switch]$KeepInstalled
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $ResultDir | Out-Null
$Setup = (Resolve-Path $Setup).Path
$SamplePdf = (Resolve-Path $SamplePdf).Path
$results = New-Object System.Collections.Generic.List[object]
$appDir = Join-Path $env:ProgramFiles 'EK FastPDF'
$app = Join-Path $appDir 'EKFastPDF.exe'
$logFile = Join-Path $env:LOCALAPPDATA 'EK FastPDF\logs\fastpdf.log'

function Record($name, [bool]$ok, $detail = '') {
    $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $color
}

function Stop-Viewer {
    Get-Process EKFastPDF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.CloseMainWindow() | Out-Null } catch {} }
    Start-Sleep -Milliseconds 800
    Get-Process EKFastPDF -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

function Wait-Window([string]$expectTitle = '', [int]$timeoutSec = 40) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $win = Get-Process EKFastPDF -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
        foreach ($w in $win) {
            $w.Refresh()
            if (-not $expectTitle -or $w.MainWindowTitle -like "*$expectTitle*") {
                return [pscustomobject]@{ Ok = $true; Ms = [int]$sw.Elapsed.TotalMilliseconds; Title = $w.MainWindowTitle; Count = @($win).Count }
            }
        }
        Start-Sleep -Milliseconds 100
    }
    $titles = (Get-Process EKFastPDF -ErrorAction SilentlyContinue | ForEach-Object { $_.MainWindowTitle }) -join ' | '
    return [pscustomobject]@{ Ok = $false; Ms = [int]$sw.Elapsed.TotalMilliseconds; Title = $titles; Count = 0 }
}

function Test-Launch($name, [scriptblock]$start, [string]$expectTitle = '') {
    Stop-Viewer
    try {
        & $start
        $w = Wait-Window $expectTitle
        Record $name $w.Ok "window in $($w.Ms) ms; title '$($w.Title)'"
    } catch {
        Record $name $false $_.Exception.Message
    }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellLaunch {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO {
        public int cbSize; public uint fMask; public IntPtr hwnd;
        public string lpVerb; public string lpFile; public string lpParameters; public string lpDirectory;
        public int nShow; public IntPtr hInstApp; public IntPtr lpIDList; public string lpClass;
        public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon; public IntPtr hProcess;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
    // Opens a file through a registered class (ProgID) exactly like Explorer does once that
    // class is the user's choice for the extension.
    public static void OpenWithClass(string file, string progId, string workingDir) {
        var info = new SHELLEXECUTEINFO();
        info.cbSize = Marshal.SizeOf(info);
        info.fMask = 0x00000001 /*SEE_MASK_CLASSNAME*/ | 0x00000100 /*SEE_MASK_NOASYNC*/;
        info.lpVerb = "open"; info.lpFile = file; info.lpClass = progId; info.lpDirectory = workingDir; info.nShow = 1;
        if (!ShellExecuteEx(ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}
'@

$userChoiceKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice'
$choiceBefore = (Get-ItemProperty $userChoiceKey -ErrorAction SilentlyContinue).ProgId

# ---------------------------------------------------------------- install
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS="desktopicon,pdfopenwith"', "/LOG=`"$ResultDir\install.log`"" -Wait -PassThru
Record 'Silent install (Program Files, admin)' ($p.ExitCode -eq 0 -and (Test-Path $app)) "exit $($p.ExitCode) in $([int]$sw.Elapsed.TotalSeconds) s"
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{31680A75-F231-4280-A95E-9D3AF023FD36}_is1'
$u = Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue
Record 'Apps & features entry' ($null -ne $u -and $u.DisplayName -eq 'EK FastPDF') "$($u.DisplayName) $($u.DisplayVersion)"
$startLnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\EK FastPDF\EK FastPDF.lnk'
$deskLnk = Join-Path $env:PUBLIC 'Desktop\EK FastPDF.lnk'
Record 'Start menu shortcut' (Test-Path $startLnk) $startLnk
Record 'Desktop shortcut (task selected)' (Test-Path $deskLnk) $deskLnk
$cmd = (Get-ItemProperty 'HKLM:\SOFTWARE\Classes\EKFastPDF.Document\shell\open\command' -ErrorAction SilentlyContinue).'(default)'
Record 'ProgID registered' ($cmd -eq "`"$app`" `"%1`"") $cmd
$owp = Get-Item 'HKLM:\SOFTWARE\Classes\.pdf\OpenWithProgids' -ErrorAction SilentlyContinue
Record 'Listed in Open with (.pdf OpenWithProgids)' ($null -ne $owp -and $owp.GetValueNames() -contains 'EKFastPDF.Document')
$reg = (Get-ItemProperty 'HKLM:\SOFTWARE\RegisteredApplications' -ErrorAction SilentlyContinue).'EK FastPDF'
Record 'Default Apps capability registered' ($reg -eq 'Software\EK\EK FastPDF\Capabilities') $reg
$choiceAfter = (Get-ItemProperty $userChoiceKey -ErrorAction SilentlyContinue).ProgId
Record 'Default PDF app unchanged' ($choiceBefore -eq $choiceAfter) "before '$choiceBefore' after '$choiceAfter'"
$zone = Get-Item -LiteralPath $app -Stream Zone.Identifier -ErrorAction SilentlyContinue
Record 'Installed exe has no Mark-of-the-Web' ($null -eq $zone)

# ---------------------------------------------------------------- launches
$work = Join-Path $env:TEMP 'EK FastPDF test – Pläne (Ø 1)'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$spacedPdf = Join-Path $work 'E-101 Power Plan – Level 1 (rev Ø).pdf'
Copy-Item $SamplePdf $spacedPdf -Force
$second = Join-Path $work 'second document.pdf'
Copy-Item $SamplePdf $second -Force

Test-Launch 'Direct start from install folder' { Start-Process -FilePath $app -WorkingDirectory $appDir -UseNewEnvironment:$false | Out-Null }
Test-Launch 'Explorer start of EKFastPDF.exe (double-click path)' { Start-Process explorer.exe -ArgumentList "`"$app`"" | Out-Null }
Test-Launch 'ShellExecute of EKFastPDF.exe (cwd C:\Windows)' { Start-Process -FilePath $app -WorkingDirectory $env:SystemRoot | Out-Null }
Test-Launch 'Start menu shortcut' { Start-Process -FilePath $startLnk | Out-Null }
Test-Launch 'Desktop shortcut' { Start-Process -FilePath $deskLnk | Out-Null }
Test-Launch 'App Paths (Win+R "EKFastPDF")' { Start-Process -FilePath 'EKFastPDF' -WorkingDirectory $env:USERPROFILE | Out-Null }
Test-Launch 'Quoted Unicode path with spaces, other working dir' { Start-Process -FilePath $app -ArgumentList "`"$spacedPdf`"" -WorkingDirectory $env:SystemRoot | Out-Null } 'E-101 Power Plan'
Test-Launch 'Open with: EKFastPDF.Document ProgID (file association)' { [ShellLaunch]::OpenWithClass($spacedPdf, 'EKFastPDF.Document', $env:USERPROFILE) } 'E-101 Power Plan'
Test-Launch 'Open with: Applications\EKFastPDF.exe' { [ShellLaunch]::OpenWithClass($second, 'Applications\EKFastPDF.exe', $env:SystemRoot) } 'second document'

# Single instance: second launch with a file opens a tab in the running window.
Stop-Viewer
Start-Process -FilePath $app -ArgumentList "`"$spacedPdf`"" | Out-Null
$first = Wait-Window 'E-101 Power Plan'
Start-Process -FilePath $app -ArgumentList "`"$second`"" | Out-Null
$w = Wait-Window 'second document' 20
$windows = @(Get-Process EKFastPDF -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }).Count
Record 'Single instance: second file opens as tab in same window' ($first.Ok -and $w.Ok -and $windows -eq 1) "main windows: $windows; title '$($w.Title)'"
$workers = @(Get-Process EKFastPDF -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -eq 0 }).Count
Record 'Render worker processes running' ($workers -ge 1) "$workers workers"
Stop-Viewer
Start-Sleep -Seconds 1
$left = @(Get-Process EKFastPDF -ErrorAction SilentlyContinue).Count
Record 'No processes left after closing' ($left -eq 0) "$left remaining"

# Software rendering fallback.
Test-Launch 'Software rendering fallback (--software-rendering)' { Start-Process -FilePath $app -ArgumentList '--software-rendering', "`"$second`"" | Out-Null } 'second document'
Stop-Viewer
$swLog = (Get-Content $logFile -Tail 400 -ErrorAction SilentlyContinue) -join "`n"
Record 'Software mode reported in log' ($swLog -match 'process render mode SoftwareOnly')

# Corrupt / missing file must not crash.
$bad = Join-Path $work 'broken.pdf'
[IO.File]::WriteAllBytes($bad, [byte[]](1..2000 | ForEach-Object { $_ % 251 }))
Test-Launch 'Corrupt PDF shows an error tab, no crash' { Start-Process -FilePath $app -ArgumentList "`"$bad`"" | Out-Null } 'broken.pdf'
Start-Sleep -Seconds 2
$alive = @(Get-Process EKFastPDF -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }).Count
Record 'Viewer still running after corrupt file' ($alive -eq 1)
Stop-Viewer

# ---------------------------------------------------------------- diagnostics & print
$diag = Start-Process -FilePath $app -ArgumentList '--diagnose' -Wait -PassThru -RedirectStandardOutput "$ResultDir\diagnose-path.txt"
$reportPath = (Get-Content "$ResultDir\diagnose-path.txt" -ErrorAction SilentlyContinue | Select-Object -First 1)
if ($reportPath -and (Test-Path $reportPath)) { Copy-Item $reportPath "$ResultDir\diagnostics-report.txt" }
$reportText = Get-Content "$ResultDir\diagnostics-report.txt" -Raw -ErrorAction SilentlyContinue
Record 'Diagnostics report (--diagnose)' ($diag.ExitCode -eq 0 -and $reportText -match 'Security zone') ($reportText -split "`n" | Select-String 'Security zone|Mark-of-the-Web|GPU adapter|CPU ' | ForEach-Object { $_.Line.Trim() }) -join '; '

$printer = Get-Printer -Name 'Microsoft Print to PDF' -ErrorAction SilentlyContinue
if ($printer) {
    $printed = Join-Path $ResultDir 'print-test-output.pdf'
    $pp = Start-Process -FilePath $app -ArgumentList '--print-file', '"Microsoft Print to PDF"', "`"$printed`"", "`"$SamplePdf`"" -Wait -PassThru
    Start-Sleep -Seconds 3
    $ok = (Test-Path $printed) -and ((Get-Item $printed).Length -gt 1000)
    Record 'Vector printing (Microsoft Print to PDF)' ($pp.ExitCode -eq 0 -and $ok) "exit $($pp.ExitCode), $(if (Test-Path $printed) { (Get-Item $printed).Length } else { 0 }) bytes"
} else {
    Record 'Vector printing (Microsoft Print to PDF)' $true 'SKIPPED: printer not present on this machine'
}

# ---------------------------------------------------------------- upgrade
$p = Start-Process $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$ResultDir\upgrade.log`"" -Wait -PassThru
Record 'Upgrade / reinstall over existing installation' ($p.ExitCode -eq 0) "exit $($p.ExitCode)"
$dirs = @(Get-ChildItem (Split-Path $appDir) -Directory | Where-Object Name -like 'EK FastPDF*').Count
Record 'Upgrade kept a single install folder' ($dirs -eq 1)
Test-Launch 'Start menu shortcut after upgrade' { Start-Process -FilePath $startLnk | Out-Null }
Stop-Viewer

# ---------------------------------------------------------------- uninstall
if (-not $KeepInstalled) {
    $un = Join-Path $appDir 'unins000.exe'
    $p = Start-Process $un -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    Start-Sleep -Seconds 3
    Record 'Uninstall' ($p.ExitCode -eq 0 -and -not (Test-Path $app)) "exit $($p.ExitCode)"
    Record 'Uninstall removed shortcuts' (-not (Test-Path $startLnk) -and -not (Test-Path $deskLnk))
    Record 'Uninstall removed ProgID' (-not (Test-Path 'HKLM:\SOFTWARE\Classes\EKFastPDF.Document'))
    $owp = Get-Item 'HKLM:\SOFTWARE\Classes\.pdf\OpenWithProgids' -ErrorAction SilentlyContinue
    Record 'Uninstall removed Open with entry' ($null -eq $owp -or -not ($owp.GetValueNames() -contains 'EKFastPDF.Document'))
    Record 'Uninstall removed Apps & features entry' (-not (Test-Path $uninstallKey))
}

if (Test-Path $logFile) { Copy-Item $logFile "$ResultDir\fastpdf.log" -Force }
$md = @('| Test | Result | Detail |', '|---|---|---|') + ($results | ForEach-Object { "| $($_.Test) | $($_.Result) | $($_.Detail -replace '\|', '/') |" })
$md | Set-Content "$ResultDir\install-tests.md" -Encoding UTF8
$failed = @($results | Where-Object Result -eq 'FAIL')
Write-Host "`n$($results.Count - $failed.Count)/$($results.Count) install and launch tests passed"
if ($failed.Count -gt 0) { throw "$($failed.Count) install/launch test(s) failed" }
