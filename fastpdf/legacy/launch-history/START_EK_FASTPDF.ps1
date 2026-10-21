# Alternative EK FastPDF launcher. Uses the same direct ProcessStartInfo method
# that succeeded in the previous launch diagnostic. Does not change Windows settings.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$folders = @($root, (Join-Path $root 'EK FastPDF'))
$folder = $folders | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'EKFastPDF.exe') } | Select-Object -First 1
$lines = New-Object 'System.Collections.Generic.List[string]'
function Report([string]$line) { $lines.Add($line); Write-Host $line }
function Fail([string]$message) {
    Report "FAILED: $message"
    try {
        $desktop = [Environment]::GetFolderPath('Desktop')
        $logFile = Join-Path $desktop 'EKFastPDF_Start_Log.txt'
        $lines | Set-Content -LiteralPath $logFile -Encoding UTF8
        Write-Host "Saved: $logFile"
    } catch {
        $logFile = Join-Path $env:TEMP 'EKFastPDF_Start_Log.txt'
        $lines | Set-Content -LiteralPath $logFile -Encoding UTF8
        Write-Host "Saved: $logFile"
    }
    exit 1
}
Report ('Date: ' + (Get-Date -Format o))
Report ('Helper folder: ' + $root)
if (-not $folder) {
    Fail 'EKFastPDF.exe not found. Extract both launcher files either into the EK FastPDF output folder or into the project folder containing the EK FastPDF folder. If there is no EXE, rerun BUILD_WINDOWS.cmd and check for Build succeeded.'
}
$exe = Join-Path $folder 'EKFastPDF.exe'
Report ('Application folder: ' + $folder)
foreach ($name in @('EKFastPDF.exe', 'EKFastPDF.dll', 'EKFastPDF.runtimeconfig.json', 'pdfium.dll')) {
    $file = Join-Path $folder $name
    Report (($name + ': ') + $(if (Test-Path -LiteralPath $file) { 'present' } else { 'MISSING' }))
}
try {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $exe
    $start.WorkingDirectory = $folder
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    if (-not $process) { Fail 'Windows did not return a process.' }
    Report ('Started PID: ' + $process.Id)
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited) {
        Fail ('Viewer closed within 2 seconds. Exit code: ' + $process.ExitCode)
    }
    Write-Host 'EK FastPDF started successfully.'
    exit 0
} catch {
    Report ('Error type: ' + $_.Exception.GetType().FullName)
    Report ('Error: ' + $_.Exception.Message)
    $inner = $_.Exception.InnerException
    if ($inner) {
        Report ('Inner error: ' + $inner.Message)
        if ($inner -is [System.ComponentModel.Win32Exception]) {
            Report ('Native Win32 error: ' + $inner.NativeErrorCode)
        }
    }
    Fail 'Windows could not start the application.'
}
