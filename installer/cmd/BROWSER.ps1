# Only this machine-wide step requests UAC. No user manifests or user payloads
# are touched here, including when alternate administrator credentials are used.
[CmdletBinding()]
param([string]$ApplyYears = '', [string]$RestoreYears = '', [Parameter(Mandatory=$true)][string]$ReportPath)
$ErrorActionPreference = 'Stop'
Write-Host 'BROWSER.ps1 started - EK Triangle SIMPLE'
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { Write-Host 'STOPPED: PowerShell language policy prevents the required .NET operations. Ask IT.'; exit 5 }
$code = 1
$rows = @()
try {
    . (Join-Path $PSScriptRoot 'COMMON.ps1')
    Initialize-Engine
    if (-not [EKTriangle.Core.WindowsRegistry]::IsAdministrator()) { throw 'This protected Autodesk file step requires administrator permission.' }
    Assert-RevitClosed
    [int[]]$apply = @($ApplyYears -split ',' | Where-Object { $_ } | ForEach-Object { if ($_ -notmatch '^202[4-7]$') { throw 'Invalid year.' }; [int]$_ })
    [int[]]$restore = @($RestoreYears -split ',' | Where-Object { $_ } | ForEach-Object { if ($_ -notmatch '^202[4-7]$') { throw 'Invalid year.' }; [int]$_ })
    $report = [EKTriangle.Core.Report]::new()
    $log = [EKTriangle.Core.Log]::new((Join-Path $script:Machine.MachineLogDir 'EKTriangle-SIMPLE.log'))
    $code = [EKTriangle.Core.Operations]::BrowserApply($script:Machine,$log,$apply,$report,$null,$restore)
    foreach ($year in $script:SupportedYears) {
        $outcome = $report.Get([string]$year,'BrowserResult')
        if ($outcome) { $rows += [pscustomobject]@{Year=$year;Outcome=$outcome;Detail=$report.Get([string]$year,'BrowserResultText')} }
    }
} catch {
    Write-Host ('STOPPED: ' + $_.Exception.Message)
    $rows += [pscustomobject]@{Year=0;Outcome='Failed';Detail=$_.Exception.Message}
    $code = 1
}
try {
    $json = [pscustomobject]@{ExitCode=$code;Results=@($rows)} | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($ReportPath,$json,[Text.UTF8Encoding]::new($false))
} catch { Write-Host 'Administrator step could not write its result report.'; exit 1 }
exit $code
