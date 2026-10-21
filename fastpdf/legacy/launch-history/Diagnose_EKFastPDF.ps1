# Read-only launch diagnostics for EK FastPDF (apart from writing the Desktop log).
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$folder = $here
if (-not (Test-Path -LiteralPath (Join-Path $folder 'EKFastPDF.exe'))) {
  $folder = Join-Path $here 'EK FastPDF'
}
$exe = Join-Path $folder 'EKFastPDF.exe'
$dll = Join-Path $folder 'EKFastPDF.dll'
$desktop = [Environment]::GetFolderPath('Desktop')
$log = Join-Path $desktop 'EKFastPDF_Diagnostic.txt'
try { Set-Content -LiteralPath $log -Value 'EK FastPDF launch diagnostics - read-only tests' -Encoding UTF8 -ErrorAction Stop }
catch {
  $log = Join-Path $env:TEMP 'EKFastPDF_Diagnostic.txt'
  Set-Content -LiteralPath $log -Value 'EK FastPDF launch diagnostics - read-only tests' -Encoding UTF8
}
function Say([string]$s) { Write-Host $s; Add-Content -LiteralPath $log -Value $s -Encoding UTF8 }
Say ('Date: ' + (Get-Date -Format o))
Say ('Folder: ' + $folder)
Say ('Windows: ' + [Environment]::OSVersion.VersionString)
Say ('User: ' + $env:USERNAME)
Say ''
foreach ($file in @('EKFastPDF.exe','EKFastPDF.dll','EKFastPDF.runtimeconfig.json','pdfium.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll')) {
    $p = Join-Path $folder $file
    if (Test-Path -LiteralPath $p) {
      $f = Get-Item -LiteralPath $p -Force
      Say ('PRESENT: ' + $file + ' (' + $f.Length + ' bytes)')
    } else { Say ('MISSING: ' + $file) }
}
Say ''
if (-not (Test-Path -LiteralPath $exe)) {
    Say 'The EXE does not exist in the current location. Extract the helper files inside the EK FastPDF folder.'
    Say ('Diagnostic log: ' + $log)
    exit 2
}
try {
  $streams = Get-Item -LiteralPath $exe -Stream * -ErrorAction Stop | Select-Object -ExpandProperty Stream
  Say ('EXE streams: ' + ($streams -join ', '))
} catch { Say ('Stream inspection unavailable: ' + $_.Exception.Message) }
try {
  $signature = Get-AuthenticodeSignature -LiteralPath $exe
  Say ('Digital signature: ' + $signature.Status)
} catch { Say ('Signature check error: ' + $_.Exception.Message) }
try {
  $icacls = & icacls.exe $exe 2>&1 | Out-String
  Say ('Access permissions: ' + $icacls.Trim())
} catch { Say ('Permissions check error: ' + $_.Exception.Message) }
function Launch-Test([string]$label, [string]$program, [string]$arguments) {
  Say ''
  Say ('LAUNCH TEST: ' + $label)
  try {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $program
    $start.Arguments = $arguments
    $start.WorkingDirectory = $folder
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { Say 'ERROR: Process.Start returned null'; return $false }
    Say ('Started process ID: ' + $process.Id)
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited) {
      Say ('Exited within 2 seconds. Exit code: ' + $process.ExitCode)
      return $false
    }
    Say 'SUCCESS: process is still running after 2 seconds.'
    return $true
  } catch {
    $errorEx = $_.Exception
    if ($errorEx.InnerException) { $errorEx = $errorEx.InnerException }
    Say ('Launch error: ' + $errorEx.GetType().FullName + ': ' + $errorEx.Message)
    Say ('HResult: 0x' + $errorEx.HResult.ToString('X8'))
    if ($errorEx -is [System.ComponentModel.Win32Exception]) {
      Say ('Native Win32 error: ' + $errorEx.NativeErrorCode)
    }
    return $false
  }
}
$exeOk = Launch-Test 'Native EXE' $exe ''
if (-not $exeOk -and (Test-Path -LiteralPath $dll)) {
   $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
   if ($dotnet) {
     $null = Launch-Test '.NET DLL alternative' $dotnet.Source ('"' + $dll + '"')
   } else { Say 'dotnet.exe not found on PATH; skipped DLL alternative.' }
}
Say ''
Say 'Recent relevant Windows Application events (last 15 minutes):'
try {
  $events = Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=(Get-Date).AddMinutes(-15)} -ErrorAction Stop |
    Where-Object { $_.Message -match '(?i)EKFastPDF|pdfium.dll' } | Select-Object -First 8
  if ($events) {
    foreach ($evt in $events) { Say ('Event ' + $evt.Id + ' / ' + $evt.ProviderName + ' / ' + $evt.TimeCreated + ': ' + ($evt.Message -replace '\s+',' ').Substring(0,[Math]::Min(900,($evt.Message -replace '\s+',' ').Length))) }
  } else { Say 'None found.' }
} catch { Say ('Windows events unavailable: ' + $_.Exception.Message) }
Say ''
Say ('FINISHED. Send this log: ' + $log)
